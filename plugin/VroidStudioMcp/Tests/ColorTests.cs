using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Catalogue all color parameters.</summary>
public sealed class ColorMap : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return WalkAllCategories();
        Colors.RefreshRegistry();

        var list = new List<object>();
        foreach (var e in Colors.Entries)
            list.Add(new Dictionary<string, object>
            {
                ["id"] = e.Id,
                ["label"] = e.Label,
                ["color"] = SafeStr(e.Id == "Hair|Emission"
                    ? Get(e.Obj, "HairMaterialTemplateEmissionColor")
                    : (Get(e.Obj, "Color") ?? Get(e.Obj, "HairMaterialTemplateColor"))),
            });

        WriteResult("colormap.json", new Dictionary<string, object> { ["count"] = list.Count, ["colors"] = list });
        Report("colormap", list.Count > 0, list.Count);
    }
}

/// <summary>Set every color parameter and verify a render change.</summary>
public sealed class ColorTestAll : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return WalkAllCategories();
        Colors.RefreshRegistry();

        var results = new List<Dictionary<string, object>>();
        for (int i = 0; i < Colors.Entries.Count; i++)
        {
            var entry = Colors.Entries[i];
            var before = Shot();
            var color = (i % 2 == 0) ? new UnityEngine.Color(0.9f, 0.1f, 0.1f, 1f) : new UnityEngine.Color(0.1f, 0.1f, 0.9f, 1f);
            Signals.Arm();
            TestSupport.SetColorParamByIndex(i, color);
            yield return Settle(1.5f);
            var after = Shot();
            bool changed = after != before;
            results.Add(new Dictionary<string, object>
            {
                ["id"] = entry.Id,
                ["label"] = entry.Label,
                ["before"] = before,
                ["after"] = after,
                ["changed"] = changed,
            });
            Report(entry.Label, changed, new Dictionary<string, object> { ["before"] = before, ["after"] = after });
        }
        WriteResult("colorall_result.json", new Dictionary<string, object> { ["count"] = Colors.Entries.Count, ["results"] = results });
    }
}

/// <summary>Iris "Both Eyes" color round-trip.</summary>
public sealed class ColorTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return SwitchSection("FaceEditor");
        yield return EnterCategory("FaceEditor", "Irises");

        var any = TestSupport.FindSubByCatType("Iris");
        if (any == null) { Report("iris", false, "no iris SubColorSelection"); yield break; }
        var sub = Enumerate(Get(Get(any, "_parent"), "_subColorSelections"))
            .FirstOrDefault(s => (Get(s, "TranslatedDisplayName") as string)?.Contains("Both") == true) ?? any;

        var before = Shot();
        Signals.Arm();
        Invoke(sub, "set_Color", new UnityEngine.Color(0.580f, 0.714f, 0.475f, 1f));
        yield return Settle(1.5f);
        var after = Shot();
        WriteResult("color_result.json", new Dictionary<string, object> { ["before"] = before, ["after"] = after, ["changed"] = after != before });
        Report("iris", after != before, new Dictionary<string, object> { ["before"] = before, ["after"] = after });
    }
}

/// <summary>Hair main color (HairEditor.ViewModel.HairMaterialTemplateColor).</summary>
public sealed class HairColorTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return SwitchSection("HairEditor");
        Colors.RefreshRegistry();
        var vm = Colors._colorHairVm;
        if (vm == null) { Report("hair-main", false, "no HairEditor.ViewModel"); yield break; }

        var before = Shot();
        Signals.Arm();
        Invoke(vm, "set_HairMaterialTemplateColor", new UnityEngine.Color(0.580f, 0.714f, 0.475f, 1f));
        yield return Settle(1.5f);
        var after = Shot();
        WriteResult("haircolor_result.json", new Dictionary<string, object> { ["before"] = before, ["after"] = after, ["changed"] = after != before });
        Report("hair-main", after != before, new Dictionary<string, object> { ["before"] = before, ["after"] = after });
    }
}
