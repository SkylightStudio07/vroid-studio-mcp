using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Global hair colours are tab-agnostic: every non-Base Hairstyle tab exposes the same
/// stable paths `Hairstyle/Main` / `Hairstyle/Emission` (flagged global:true) and the value is
/// shared. Contract checks: only documented selectors are accepted (unknown parameter/section error
/// instead of silently passing through), and Base Hair is excluded.</summary>
public sealed class HairGlobalColorTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return SwitchSection("HairEditor");
        // The global colour panel is enabled by the Level-0 set preset, not by group presets.
        yield return SelectRealPreset("Hairstyle Sets");
        yield return SelectRealPreset("Front");
        yield return SelectRealPreset("Back");

        var front = new CommandDispatcher.Context { Method = "enter", Args = new[] { "Hairstyle", "Front", "keep" } };
        yield return CommandDispatcher.TestEnter(front);
        var frontMain = FindColor(front.Result, "Main");
        var back = new CommandDispatcher.Context { Method = "enter", Args = new[] { "Hairstyle", "Back", "keep" } };
        yield return CommandDispatcher.TestEnter(back);
        var backMain = FindColor(back.Result, "Main");

        bool pathOk = frontMain != null && backMain != null
            && (string)frontMain["path"] == "Hairstyle/Main" && (string)backMain["path"] == "Hairstyle/Main"
            && frontMain.TryGetValue("global", out var g) && g is bool gb && gb;
        Report("hairglobal-paths", pathOk, new Dictionary<string, object>
        { ["front"] = frontMain?["path"], ["back"] = backMain?["path"], ["frontError"] = front.Error, ["backError"] = back.Error });

        // Set the shared value through the stable path on Front...
        var setFront = new CommandDispatcher.Context { Method = "set_param", Args = new[] { "Hairstyle", "Front", "Hairstyle/Main", "#FFAA55" } };
        yield return CommandDispatcher.TestSetParam(setFront);

        // ...and through the same stable path on Back (works from any non-Base tab).
        var setBack = new CommandDispatcher.Context { Method = "set_param", Args = new[] { "Hairstyle", "Back", "Hairstyle/Main", "#00FF00" } };
        yield return CommandDispatcher.TestSetParam(setBack);
        bool setOk = setFront.Error == null && setBack.Error == null;
        Report("hairglobal-set", setOk, new Dictionary<string, object> { ["front"] = setFront.Error, ["back"] = setBack.Error });

        // Contract: an unknown selector is an error, not a silent no-op.
        var badSel = new CommandDispatcher.Context { Method = "set_param", Args = new[] { "Hairstyle", "Front", "Hairstyle/Nope", "#000000" } };
        yield return CommandDispatcher.TestSetParam(badSel);
        bool badSelectorRejected = badSel.Error != null;
        Report("hairglobal-bad-selector", badSelectorRejected, badSel.Error);

        // The writes went to one shared value: read it back through the API (enter).
        var read = new CommandDispatcher.Context { Method = "enter", Args = new[] { "Hairstyle", "Front", "keep" } };
        yield return CommandDispatcher.TestEnter(read);
        var readMain = FindColor(read.Result, "Main");
        bool shared = readMain != null && (string)readMain["value"] == "#00FF00";
        Report("hairglobal-shared", shared, readMain?["value"] ?? "no colour");

        // Base Hair must not accept the global path.
        var baseHair = new CommandDispatcher.Context { Method = "set_param", Args = new[] { "Hairstyle", "Base Hair", "Hairstyle/Main", "#123456" } };
        yield return CommandDispatcher.TestSetParam(baseHair);
        bool baseRejected = baseHair.Error != null;

        // The colour panel is global (IsRightPanelActive): once any hair preset is selected, the two
        // global colours are exposed even on a tab with no preset of its own, so set_param succeeds.
        // The "needs a preset" hint is therefore only reachable on a fully empty hair section.
        var empty = new CommandDispatcher.Context { Method = "set_param", Args = new[] { "Hairstyle", "Extensions", "Hairstyle/Main", "#123456" } };
        yield return CommandDispatcher.TestSetParam(empty);
        bool globalOnEmpty = empty.Error == null;

        // Contract: an unknown section is an error, not a silent pass-through.
        var badSection = new CommandDispatcher.Context { Method = "enter", Args = new[] { "Nope", "Front", "keep" } };
        yield return CommandDispatcher.TestEnter(badSection);
        bool unknownSectionRejected = badSection.Error != null;
        Report("hairglobal-section", unknownSectionRejected, badSection.Error);

        bool ok = pathOk && setOk && badSelectorRejected && shared && baseRejected && unknownSectionRejected;
        WriteResult("hairglobal_result.json", new Dictionary<string, object>
        {
            ["ok"] = ok,
            ["path_ok"] = pathOk,
            ["set_ok"] = setOk,
            ["bad_selector_rejected"] = badSelectorRejected,
            ["shared"] = shared,
            ["base_rejected"] = baseRejected,
            ["unknown_section_rejected"] = unknownSectionRejected,
            ["global_on_empty_tab"] = globalOnEmpty,
        });
        Report("hairglobal", ok, new Dictionary<string, object>
        { ["paths"] = pathOk, ["set"] = setOk, ["badSelector"] = badSelectorRejected, ["shared"] = shared, ["base"] = baseRejected, ["unknownSection"] = unknownSectionRejected });
    }

    private IEnumerator SelectRealPreset(string tab)
    {
        var selector = SelectorFor("HairEditor");
        var t = Categories.FindTab(selector, tab);
        if (t != null) Invoke(selector, "set_SelectedCategory", t);
        yield return Pause(0.6f);
        var presetNode = Get(t, "PresetCategoryViewModel");
        var src = (object)presetNode ?? t;
        var option = Enumerate(Get(src, "PresetOptions")).FirstOrDefault(o => !((TypeName(o) ?? "").Contains("ClearOption")));
        Signals.Arm();
        if (option != null) Invoke(option, "set_IsSelected", true);
        yield return Settle(1.5f);
    }

    private static Dictionary<string, object> FindColor(object result, string name)
    {
        if (result is not Dictionary<string, object> d || d["parameters"] is not List<object> ps) return null;
        return ps.OfType<Dictionary<string, object>>()
            .FirstOrDefault(p => p.TryGetValue("name", out var n) && (string)n == name && (string)p["kind"] == "color");
    }
}
