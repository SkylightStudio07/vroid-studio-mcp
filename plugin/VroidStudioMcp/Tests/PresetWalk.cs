using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;


namespace VroidStudioMcp;

/// <summary>Paced selection of 3 presets per category across Face/Hair/Clothing.</summary>
public sealed class PresetWalk : VroidTest
{
    private static readonly string[] SectionsToWalk = { "FaceEditor", "HairEditor", "ClothingEditor" };

    protected override IEnumerator Run()
    {
        var results = new List<Dictionary<string, object>>();
        int pass = 0, total = 0;
        yield return Open();

        foreach (var section in SectionsToWalk)
        {
            yield return SwitchSection(section);
            var selector = SelectorFor(section);
            foreach (var tab in Enumerate(Get(Get(selector, "Categories"), "Children")).ToList())
            {
                Invoke(selector, "set_SelectedCategory", tab);
                yield return Pause(0.6f);

                var presetNode = Get(tab, "PresetCategoryViewModel");
                var src = (object)presetNode ?? tab;
                var all = Enumerate(Get(src, "PresetOptions"))
                    .Where(o => !((TypeName(o) ?? "").Contains("ClearOption"))).ToList();
                var picks = new List<object>();
                if (all.Count > 0) picks.Add(all[0]);
                if (all.Count > 1) picks.Add(all[all.Count / 2]);
                if (all.Count > 1) picks.Add(all[all.Count - 1]);

                foreach (var option in picks.Distinct())
                {
                    var before = Shot();
                    Signals.Arm();
                    Invoke(option, "set_IsSelected", true);
                    yield return Settle(1.5f);
                    var after = Shot();
                    bool changed = after != before;
                    total++; if (changed) pass++;
                    var category = Get(tab, "Name");
                    results.Add(new Dictionary<string, object>
                    {
                        ["section"] = section,
                        ["category"] = category,
                        ["resourceName"] = Get(Get(option, "Model"), "resourceName"),
                        ["before"] = before,
                        ["after"] = after,
                        ["changed"] = changed,
                    });
                    Report($"{section}/{category}", changed, Get(Get(option, "Model"), "resourceName"));
                }
            }
        }

        WriteResult("presetwalk_result.json", new Dictionary<string, object> { ["total"] = total, ["pass"] = pass, ["results"] = results });
        PluginLog.LogWarning($"presetwalk: done pass={pass}/{total}");
    }
}
