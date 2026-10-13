using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Selects representative presets of every kind and verifies a render change.</summary>
public sealed class PresetTest : VroidTest
{
    private static readonly (string Sec, string Cat, int Idx, string Kind)[] Targets =
    {
        ("ClothingEditor", "Tops", 1, "clothing-regular"),
        ("ClothingEditor", "Whole Sets", 1, "clothing-set"),
        ("HairEditor", "Front", 1, "hair-regular"),
        ("HairEditor", "Hairstyle Sets", 1, "hair-set"),
        ("FaceEditor", "Face Sets", 25, "face-set"),
        ("FaceEditor", "Irises", 2, "face-color"),
        ("BodyEditor", "Whole Body", 3, "body"),
    };

    protected override IEnumerator Run()
    {
        var results = new List<Dictionary<string, object>>();
        yield return Open();

        string current = null;
        foreach (var (sec, cat, idx, kind) in Targets)
        {
            if (current != sec)
            {
                current = sec;
                Sections.SwitchSectionForCat(sec);
                yield return WaitUntil(() => SelectorLoaded(SelectorFor(sec)), 15f);
            }
            var selector = SelectorFor(sec);
            var tab = Categories.FindTab(selector, cat);
            if (tab == null) { Record(results, kind, false, "no tab " + cat); continue; }

            Invoke(selector, "set_SelectedCategory", tab);
            yield return Pause(0.6f);

            var presetNode = Get(tab, "PresetCategoryViewModel");
            var src = (object)presetNode ?? tab;
            var candidates = Enumerate(Get(src, "PresetOptions"))
                .Where(o => !((TypeName(o) ?? "").Contains("ClearOption"))).ToList();
            if (candidates.Count == 0) { Record(results, kind, false, "no option"); continue; }

            var option = candidates[Math.Min(idx, candidates.Count - 1)];
            var before = Shot();
            Signals.Arm();
            Invoke(option, "set_IsSelected", true);
            yield return Settle(1.5f);
            var after = Shot();

            Record(results, kind, !Equals(after, before), new Dictionary<string, object>
            {
                ["category"] = cat,
                ["resourceName"] = Get(Get(option, "Model"), "resourceName"),
                ["before"] = before,
                ["after"] = after,
                ["isSelected"] = Get(option, "IsSelected"),
            });
        }

        WriteResult("preset_result.json", new Dictionary<string, object> { ["results"] = results });
    }

    private void Record(List<Dictionary<string, object>> results, string kind, bool ok, object detail)
    {
        results.Add(new Dictionary<string, object> { ["target"] = kind, ["ok"] = ok, ["detail"] = detail });
        Report(kind, ok, detail);
    }
}
