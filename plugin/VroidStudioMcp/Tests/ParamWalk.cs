using System;
using System.Collections;
using System.Collections.Generic;
using static VroidStudioMcp.Interop;


namespace VroidStudioMcp;

/// <summary>Paced Body parameter changes with hash verification and restore.</summary>
public sealed class ParamWalk : VroidTest
{
    private static readonly string[] Labels = { "Fem Height", "Body Size", "Arm Length", "Leg Length" };

    protected override IEnumerator Run()
    {
        var results = new List<Dictionary<string, object>>();
        yield return Open();
        yield return SwitchSection("BodyEditor");

        var selector = SelectorFor("BodyEditor");
        var tab = Categories.FindTab(selector, "Whole Body");
        if (tab == null) { PluginLog.LogError("paramwalk: no Whole Body tab"); yield break; }
        Invoke(selector, "set_SelectedCategory", tab);
        yield return Pause(0.6f);
        var paramNode = Get(tab, "ParameterCategoryViewModel");

        foreach (var label in Labels)
        {
            var p = TestSupport.FindParamByLabel(paramNode, label);
            if (p == null) { PluginLog.LogWarning($"paramwalk: '{label}' not found"); continue; }

            bool combined = Get(p, "CombinedValue") != null;
            double orig = Convert.ToDouble(combined ? Get(p, "CombinedValue") : Get(p, "Value"));
            double min = Convert.ToDouble(Get(p, "MinValue") ?? -1.0);
            double max = Convert.ToDouble(Get(p, "MaxValue") ?? 1.0);
            double span = max - min;
            double next = orig + span * 0.3;
            if (next > max) next = orig - span * 0.3;
            next = Math.Max(min, Math.Min(max, next));

            var before = Shot();
            Signals.Arm();
            Invoke(p, combined ? "set_CombinedValue" : "set_Value", (float)next);
            yield return Settle(1.5f);
            var after = Shot();

            Signals.Arm();
            Invoke(p, combined ? "set_CombinedValue" : "set_Value", (float)orig);
            yield return Settle(1.5f);
            var restored = Shot();

            var detail = new Dictionary<string, object>
            {
                ["combined"] = combined,
                ["orig"] = orig,
                ["before"] = before,
                ["after"] = after,
                ["changed"] = after != before,
                ["restoreHash"] = restored,
                ["restored"] = restored == before,
            };
            results.Add(new Dictionary<string, object> { ["label"] = label, ["detail"] = detail });
            Report(label, after != before, detail);
        }

        WriteResult("param_result.json", new Dictionary<string, object> { ["results"] = results });
    }
}
