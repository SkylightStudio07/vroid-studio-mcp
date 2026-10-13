using System;
using System.Collections.Generic;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Test-only helpers that reach into the internal VMs directly. Kept out of the production
/// <c>Editor/</c> facade; tests use these where a production handler would not fit (raw VM handles).</summary>
internal static class TestSupport
{
    public static object FindParamByLabel(object category, string label)
    {
        if (category == null) return null;
        var pars = Get(category, "Parameters");
        if (pars != null)
            foreach (var p in Enumerate(pars))
                if ((TryGetLabel(p) ?? "") == label) return p;
        foreach (var ch in Enumerate(Get(category, "Children"))) { var r = FindParamByLabel(ch, label); if (r != null) return r; }
        return FindParamByLabel(Get(category, "ParameterCategoryViewModel"), label);
    }

    /// <summary>Find the newest captured SubColorSelection whose category type contains
    /// <paramref name="catTypePart"/>. Prefers the newest so a stale instance from a previous model
    /// is never returned.</summary>
    public static object FindSubByCatType(string catTypePart)
    {
        object best = null; long bestSeq = -1;
        foreach (var o in ColorRegistry.Vms)
        {
            if (!((Interop.TypeName(o) ?? "").Contains("SubColorSelection"))) continue;
            var catType = Get(Get(Get(Get(o, "_parent"), "_parent"), "_model"), "TransferableGroupType")?.ToString() ?? "";
            if (!catType.Contains(catTypePart)) continue;
            long seq = ColorRegistry.SeqOf(o);
            if (seq > bestSeq) { bestSeq = seq; best = o; }
        }
        return best;
    }

    public static void SetColorParamByIndex(int i, UnityEngine.Color c)
    {
        if (i < 0 || i >= Colors.Entries.Count) return;
        Invoke(Colors.Entries[i].Obj, Colors.Entries[i].SetMethod, c);
    }

    /// <summary>Clear every hair category (Overall/Front/Back/... + Base Hair) via ClearTransferableGroup.</summary>
    public static void ClearAllHair()
    {
        var selector = Categories.FindSelectorByShort("HairEditor.PresetsSelectorViewModel");
        if (selector == null) return;
        foreach (var tab in Enumerate(Get(Get(selector, "Categories"), "Children")))
        {
            var presetNode = Get(tab, "PresetCategoryViewModel");
            var src = (object)presetNode ?? tab;
            var options = Get(src, "PresetOptions");
            if (options == null) continue;
            foreach (var o in Enumerate(options))
                if ((TypeName(o) ?? "").Contains("ClearOption"))
                {
                    try { Invoke(o, "ClearTransferableGroup"); } catch { }
                }
        }
    }

    public static bool InvokePreset(object option)
    {
        if (option == null) return false;
        try
        {
            // PresetOptionViewModel.set_IsSelected(true) builds fallbacks + GetColorOverwrites()
            // and calls the engine's own PresetSelectAction (correct callbacks) internally.
            Invoke(option, "set_IsSelected", true);
            return Get(option, "IsSelected") is bool b && b;
        }
        catch (Exception e) { PluginLog.LogError($"invoke preset failed: {e.Message}"); }
        return false;
    }
}
