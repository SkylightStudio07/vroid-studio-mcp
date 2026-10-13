using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Clear every Hairstyle tab (all Clear presets) and check the section becomes empty:
/// a cleared non-Base tab exposes only the two section-level global colours (global:true) or
/// nothing + needs_preset. The colour panel is enabled by the Level-0 "Hairstyle Sets" preset and
/// is deactivated again when every tab is cleared.</summary>
public sealed class HairClearTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return SwitchSection("HairEditor");

        var selector = SelectorFor("HairEditor");
        var tabs = Enumerate(Get(Get(selector, "Categories"), "Children")).ToList();

        int cleared = 0;
        foreach (var tab in tabs)
        {
            Invoke(selector, "set_SelectedCategory", tab);
            yield return Pause(0.5f);
            var presetNode = Get(tab, "PresetCategoryViewModel");
            var src = (object)presetNode ?? tab;
            var clear = Enumerate(Get(src, "PresetOptions"))
                .FirstOrDefault(o => (TypeName(o) ?? "").Contains("ClearOption"));
            if (clear == null) continue;
            Signals.Arm();
            Invoke(clear, "ClearTransferableGroup");
            yield return Settle(1.0f);
            cleared++;
        }

        // After clearing, a non-Base tab must expose no tab-specific parameters; only the two global
        // hair colours may remain. If it is fully empty, needs_preset must be set.
        bool onlyGlobals = true;
        bool sawEmpty = false;
        foreach (var t in new[] { "Front", "Back", "Overall Hair" })
        {
            var e = new CommandDispatcher.Context { Method = "enter", Args = new[] { "Hairstyle", t, "keep" } };
            yield return CommandDispatcher.TestEnter(e);
            var res = e.Result as Dictionary<string, object>;
            var ps = res != null ? res["parameters"] as List<object> : null;
            if (res == null || e.Error != null) { onlyGlobals = false; continue; }
            if (ps == null || ps.Count == 0)
            {
                sawEmpty = true;
                if (!(res.TryGetValue("needs_preset", out var np) && np is bool nb && nb)) onlyGlobals = false;
            }
            else foreach (var p in ps.OfType<Dictionary<string, object>>())
                if (!(p.TryGetValue("global", out var g) && g is bool gb && gb)) onlyGlobals = false;
        }

        // Setting a global path still works (panel active) or gives the "needs a preset" hint (empty).
        var set = new CommandDispatcher.Context { Method = "set_param", Args = new[] { "Hairstyle", "Front", "Hairstyle/Main", "#112233" } };
        yield return CommandDispatcher.TestSetParam(set);
        bool setSane = set.Error == null || set.Error.Contains("preset");

        // Core assertion: after clearing every tab the section is empty (only globals could remain
        // if a preset were still active; clearing all deactivates the panel).
        bool ok = onlyGlobals && sawEmpty && setSane;
        WriteResult("hairclear_result.json", new Dictionary<string, object>
        {
            ["ok"] = ok,
            ["cleared"] = cleared,
            ["tabs"] = tabs.Count,
            ["only_globals_or_empty"] = onlyGlobals,
            ["saw_empty"] = sawEmpty,
            ["set_sane"] = setSane,
        });
        Report("hairclear", ok, new Dictionary<string, object>
        { ["onlyGlobals"] = onlyGlobals, ["empty"] = sawEmpty, ["set"] = setSane });
    }
}
