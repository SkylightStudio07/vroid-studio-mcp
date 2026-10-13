using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Paced walk through every category of every section (for visual inspection).</summary>
public sealed class CatWalk : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();

        var modeType = FindType("VRoid.Studio.EditRootScreen.EditMode");
        var modes = modeType != null ? Enum.GetNames(modeType) : Array.Empty<string>();
        foreach (var mode in modes)
        {
            Sections.SwitchSectionForCat(mode);
            yield return WaitUntil(() => SelectorLoaded(SelectorFor(mode)), 15f);
            var selector = SelectorFor(mode);
            if (selector == null) { PluginLog.LogWarning($"catwalk: {mode}: no selector"); continue; }

            var tabs = Enumerate(Get(Get(selector, "Categories"), "Children")).ToList();
            PluginLog.LogWarning($"catwalk: section {mode} ({tabs.Count} categories)");
            for (int i = 0; i < tabs.Count; i++)
            {
                var tab = tabs[i];
                var name = Get(tab, "Name") as string ?? TypeName(tab);
                Invoke(selector, "set_SelectedCategory", tab);
                PluginLog.LogWarning($"catwalk: [{mode}:{i + 1}/{tabs.Count}] {name} ({(TypeName(tab) ?? "").Split('.').Last()}) tg={Get(tab, "TransferableGroupType")}");
                yield return Pause(1.5f);
            }
        }
    }
}
