using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Walks every tab of every section, verifying selection and counting params/presets.</summary>
public sealed class CatTest : VroidTest
{
    protected override IEnumerator Run()
    {
        var results = new List<Dictionary<string, object>>();
        yield return Open();

        var modeType = FindType("VRoid.Studio.EditRootScreen.EditMode");
        var modes = modeType != null ? Enum.GetNames(modeType) : Array.Empty<string>();
        foreach (var mode in modes)
        {
            Sections.SwitchSectionForCat(mode);
            yield return WaitUntil(() => SelectorLoaded(SelectorFor(mode)), 15f);
            var selector = SelectorFor(mode);
            if (selector == null)
            {
                results.Add(new Dictionary<string, object> { ["selector"] = mode, ["reason"] = "no selector", ["categories"] = 0 });
                continue;
            }
            results.Add(WalkSelector(selector, mode));
        }

        WriteResult("cat_result.json", new Dictionary<string, object> { ["selectors"] = results });
    }

    private static Dictionary<string, object> WalkSelector(object selector, string mode)
    {
        var cats = Enumerate(Get(Get(selector, "Categories"), "Children")).ToList();
        var entries = new List<object>();
        var paramCounts = new Dictionary<string, int>();
        var presetCounts = new Dictionary<string, int>();
        int selected = 0, nonEmpty = 0;

        foreach (var cat in cats)
        {
            var name = Get(cat, "Name") as string ?? TypeName(cat);
            Invoke(selector, "set_SelectedCategory", cat);

            var selCat = Get(selector, "SelectedCategory");
            bool byRef = selCat != null && (Get(selCat, "Name") as string) == name
                && Equals(Get(selCat, "TransferableGroupType"), Get(cat, "TransferableGroupType"));
            if (byRef) selected++;

            var paramNode = Get(cat, "ParameterCategoryViewModel");
            var presetNode = Get(cat, "PresetCategoryViewModel");
            int pc = paramNode != null ? Categories.ParamCount(paramNode) : Categories.ParamCount(cat);
            int prc = presetNode != null ? Categories.PresetCount(presetNode) : Categories.PresetCount(cat);
            if (pc > 0) { nonEmpty++; paramCounts[name] = pc; }
            if (prc > 0) presetCounts[name] = prc;

            entries.Add(new Dictionary<string, object>
            {
                ["name"] = name,
                ["type"] = (TypeName(cat) ?? "").Split('.').Last(),
                ["tgtype"] = Get(cat, "TransferableGroupType"),
                ["selByRef"] = byRef,
                ["params"] = pc,
                ["presets"] = prc,
            });
        }

        var result = new Dictionary<string, object>
        {
            ["selector"] = mode,
            ["categories"] = cats.Count,
            ["selected"] = selected,
            ["nonEmpty"] = nonEmpty,
            ["distinctParamCounts"] = paramCounts.Values.Select(v => v.ToString()).Distinct().Count(),
            ["distinctPresetCounts"] = presetCounts.Values.Select(v => v.ToString()).Distinct().Count(),
            ["entries"] = entries,
        };
        PluginLog.LogWarning($"CATTEST {mode}: cats={cats.Count} selected={selected} distinctParams={result["distinctParamCounts"]} distinctPresets={result["distinctPresetCounts"]}");
        return result;
    }
}
