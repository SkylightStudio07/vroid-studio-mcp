using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using static VroidStudioMcp.Interop;
using static VroidStudioMcp.VroidState;

namespace VroidStudioMcp;

/// <summary>Aggregate smoke run: enumerate, determinism, param round-trips, preset, icon, camera, save.</summary>
public sealed class ScopeTest : VroidTest
{
    private static readonly string[] CamDof = { "angles 60 0", "angles 0 40", "zoom -2", "pan 0.2 0", "projection" };
    private readonly List<Dictionary<string, object>> _results = new();

    protected override IEnumerator Run()
    {
        var openSw = Perf.Start();
        yield return Open();
        Rec("open", true, new Dictionary<string, object> { ["ms"] = Perf.Since("scopetest: open", openSw), ["selectors"] = Selectors.Count });
        Rec("sections", true, string.Join(",", Enum.GetNames(FindType("VRoid.Studio.EditRootScreen.EditMode"))));
        EnumerateSelectors();

        // combined param (Face Sets -> Eye Size X)
        yield return SwitchSection("FaceEditor");
        yield return EnterCategory("FaceEditor", "Face Sets");
        var faceSel = SelectorFor("FaceEditor");
        var faceSets = Categories.FindTab(faceSel, "Face Sets");
        var eye = TestSupport.FindParamByLabel(Get(faceSets, "ParameterCategoryViewModel"), "Eye Size X");
        float eyeOrig = eye != null ? (float)Convert.ToDouble(Get(eye, "CombinedValue")) : 0f;
        Rec("combined_param_found", eye != null, eyeOrig);

        // determinism: 3 identical frames
        string last = null; int stable = 0; string baseHash = null;
        yield return WaitUntil(() =>
        {
            var h = Shot();
            if (h == last) stable++; else { stable = 1; last = h; }
            if (stable >= 3) { baseHash = h; return true; }
            return false;
        }, 12f);
        Rec("determinism", baseHash != null, new Dictionary<string, object> { ["stableHash"] = baseHash, ["stableCount"] = stable });

        yield return ParamRoundTrip("param_combined_influence", eye, true, 0.42f, eyeOrig, baseHash);

        // single param (Skin -> Ear Stretch)
        yield return EnterCategory("FaceEditor", "Skin");
        var skin = Categories.FindTab(SelectorFor("FaceEditor"), "Skin");
        var single = TestSupport.FindParamByLabel(Get(skin, "ParameterCategoryViewModel"), "Ear Stretch");
        float singleOrig = single != null ? (float)Convert.ToDouble(Get(single, "Value")) : 0f;
        yield return ParamRoundTrip("param_single_influence", single, false, 0.5f, singleOrig, baseHash);

        // preset select (Clothing Tops): pick a preset that is not the current one and verify the
        // observable outcome (it becomes selected and the render changes). The old check counted the
        // internal LoadAndReplaceTransferableGroup calls, which is 0 when the chosen preset is
        // already active even though selection succeeded.
        yield return SwitchSection("ClothingEditor");
        yield return EnterCategory("ClothingEditor", "Tops");
        var tops = Categories.FindTab(SelectorFor("ClothingEditor"), "Tops");
        var candidates = Enumerate(Get(tops, "PresetOptions")).Where(o => !((TypeName(o) ?? "").Contains("ClearOption"))).ToList();
        var chosen = candidates.FirstOrDefault(o => !(Get(o, "IsSelected") is bool b && b)) ?? candidates.FirstOrDefault();
        var beforePreset = Shot();
        Signals.Arm();
        bool invoked = TestSupport.InvokePreset(chosen);
        yield return Settle(1.5f);
        var afterPreset = Shot();
        bool selected = Get(chosen, "IsSelected") is bool sel && sel;
        Rec("select_preset", invoked && selected && !Equals(afterPreset, beforePreset),
            new Dictionary<string, object> { ["res"] = Get(Get(chosen, "Model"), "resourceName"), ["before"] = beforePreset, ["after"] = afterPreset, ["isSelected"] = selected });
        Rec("sets_effect", false, "TODO: set presets need colorOverwrites/fallbacks marshaling");
        Rec("hair_preset_direct_api", false, "TODO: hair presets need colorOverwrites marshaling");

        yield return ExtractIcon();
        yield return CameraDof(baseHash);
        yield return Save();

        WriteResult("scope_result.json", new Dictionary<string, object> { ["results"] = _results });
    }

    private IEnumerator ParamRoundTrip(string name, object param, bool combined, float value, float original, string baseHash)
    {
        if (param == null) { Rec(name, false, "param not found"); yield break; }
        string setter = combined ? "set_CombinedValue" : "set_Value";

        Signals.Arm();
        Invoke(param, setter, value);
        string changed = null;
        yield return WaitUntil(() => { var h = Shot(); if (h != baseHash) { changed = h; return true; } return false; }, 8f);

        Signals.Arm();
        Invoke(param, setter, original);
        bool restored = false;
        yield return WaitUntil(() => { if (Shot() == baseHash) { restored = true; return true; } return false; }, 8f);

        Rec(name, changed != null, new Dictionary<string, object> { ["base"] = baseHash, ["changed"] = changed, ["restored"] = restored });
    }

    private IEnumerator ExtractIcon()
    {
        var options = Icons.PresetOptions("ClothingEditor.PresetsSelectorViewModel", "Tops");
        var option = options.FirstOrDefault();
        if (option == null) { Rec("preset_icon", false, "no option"); yield break; }
        Icons.Prepare(option);

        object preview = null;
        yield return WaitUntil(() => { preview = Get(option, "Preview"); return preview != null; }, 6f);
        if (preview == null) { Rec("preset_icon", false, "Preview null"); yield break; }

        var task = Invoke(preview, "Create");
        yield return WaitUntil(() => Get(task, "IsCompleted") is bool b && b, 6f);
        var png = Icons.TextureToPng(Get(task, "Result"));
        var path = Path.Combine(BepInEx.Paths.GameRootPath, "preset_icon.png");
        if (png != null) File.WriteAllBytes(path, png);
        Rec("preset_icon", png != null && File.Exists(path), new Dictionary<string, object> { ["size"] = png?.Length ?? 0 });
    }

    private IEnumerator CameraDof(string baseHash)
    {
        var hashes = new Dictionary<string, string> { ["base"] = baseHash };
        foreach (var cmd in CamDof)
        {
            CameraApi.ApplyCamera("reset");
            CameraApi.ApplyCamera(cmd);
            yield return Pause(0.4f);
            hashes[cmd] = Shot();
        }
        CameraApi.ApplyCamera("reset");
        Rec("camera_5dof", hashes.Values.Distinct().Count() > 1, hashes);
    }

    private IEnumerator Save()
    {
        var vroid = Path.Combine(BepInEx.Paths.GameRootPath, "test.vroid");
        long before = File.Exists(vroid) ? File.GetLastWriteTimeUtc(vroid).Ticks : 0L;
        if (_editRootVm != null) { Invoke(_editRootVm, "Save"); Rec("save_invoked", true, "EditRootScreen.Save"); }
        else Rec("save_invoked", false, "no edit root vm");
        yield return Pause(4f);
        long after = File.Exists(vroid) ? File.GetLastWriteTimeUtc(vroid).Ticks : 0L;
        Rec("save_file", File.Exists(vroid) && after > before,
            new Dictionary<string, object> { ["path"] = vroid, ["changed"] = after > before, ["size"] = File.Exists(vroid) ? new FileInfo(vroid).Length : 0 });
    }

    private void EnumerateSelectors()
    {
        foreach (var kv in Selectors.All)
        {
            int categories = 0, paramCats = 0, parameters = 0, presets = 0;
            void Rec(object c)
            {
                if (c == null) return;
                categories++;
                var pars = Get(c, "Parameters");
                if (pars != null) { paramCats++; foreach (var _ in Enumerate(pars)) parameters++; }
                var opts = Get(c, "PresetOptions");
                if (opts != null) foreach (var _ in Enumerate(opts)) presets++;
                foreach (var ch in Enumerate(Get(c, "Children"))) Rec(ch);
                Rec(Get(c, "ParameterCategoryViewModel"));
                Rec(Get(c, "PresetCategoryViewModel"));
            }
            Rec(Get(kv.Value, "Categories"));
            RecResult("selector:" + kv.Key.Split('.').Last(), categories, paramCats, parameters, presets);
        }
    }

    private void RecResult(string item, int categories, int paramCats, int parameters, int presets)
        => Rec(item, true, new Dictionary<string, object> { ["categories"] = categories, ["paramCats"] = paramCats, ["params"] = parameters, ["presets"] = presets });

    private void Rec(string item, bool ok, object detail)
    {
        _results.Add(new Dictionary<string, object> { ["item"] = item, ["ok"] = ok, ["detail"] = detail });
        PluginLog.LogWarning($"SCOPETEST {(ok ? "PASS" : "FAIL")} {item} {MiniJson.Serialize(detail, false)}");
    }
}
