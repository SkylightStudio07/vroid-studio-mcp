using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;


namespace VroidStudioMcp;

/// <summary>Overall Hair preset -> Clear -> Base Hair color.</summary>
public sealed class BaseHairTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return SwitchSection("HairEditor");

        var selector = SelectorFor("HairEditor");
        var tab = Categories.FindTab(selector, "Overall Hair");
        if (tab == null) { PluginLog.LogError("basehairtest: no Overall Hair tab"); yield break; }
        Invoke(selector, "set_SelectedCategory", tab);
        yield return Pause(0.6f);

        var presetNode = Get(tab, "PresetCategoryViewModel");
        var src = (object)presetNode ?? tab;
        var options = Enumerate(Get(src, "PresetOptions")).ToList();
        var nonNull = options.FirstOrDefault(o => !((TypeName(o) ?? "").Contains("ClearOption")));
        var clear = options.FirstOrDefault(o => (TypeName(o) ?? "").Contains("ClearOption"));

        Signals.Arm();
        if (nonNull != null) Invoke(nonNull, "set_IsSelected", true);
        yield return Settle(1.5f);
        var beforeClear = Shot();

        Signals.Arm();
        if (clear != null) Invoke(clear, "ClearTransferableGroup");
        yield return Settle(1.5f);
        var afterClear = Shot();
        bool clearChanged = afterClear != beforeClear;
        PluginLog.LogWarning($"basehairtest: clearEffect changed={clearChanged} {beforeClear}->{afterClear}");

        yield return EnterCategory("HairEditor", "Base Hair");
        var sub = TestSupport.FindSubByCatType("BaseHair");
        if (sub == null) { Report("basehair", false, "no BaseHair SubColorSelection"); yield break; }

        var before = Shot();
        Signals.Arm();
        Invoke(sub, "set_Color", new UnityEngine.Color(0.9f, 0.1f, 0.8f, 1f));
        yield return Settle(1.5f);
        var after = Shot();

        WriteResult("basehair_result.json", new Dictionary<string, object>
        {
            ["clearChanged"] = clearChanged,
            ["before"] = before,
            ["after"] = after,
            ["changed"] = after != before,
        });
        Report("basehair", after != before, new Dictionary<string, object> { ["before"] = before, ["after"] = after });
    }
}
