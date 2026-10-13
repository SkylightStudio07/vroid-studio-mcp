using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

public static class Colors
{
    internal sealed class ColorEntry
    {
        public string Id;
        public object Obj;
        public string SetMethod;
        public string Label;
        // Which transferable groups the colour's accessor actually writes (comma-joined, sorted). A
        // colour touching more than one group is "shared" (engine quirk: the base skin colour is one
        // accessor for BodySkin/FaceSkin/NoseLighting/Eyelid).
        public string Group;
        // Stable identity of the accessor's key set; colours with the same signature are aliases.
        public string Sig;
    }

    // Flat colour-parameter registry; rebuilt from ColorRegistry whenever a category is entered.
    internal static readonly List<ColorEntry> Entries = new();
    internal static object _colorHairVm;

    /// <summary>(Re)build the flat color-parameter registry from all captured ColorSelection VMs.
    /// When several generations of the same colour exist (model reloads), the newest is kept.</summary>
    public static void RefreshRegistry()
    {
        Entries.Clear();
        var seenParents = new HashSet<IntPtr>();
        var bestSeq = new Dictionary<string, long>();
        var idxById = new Dictionary<string, int>();
        foreach (var o in ColorRegistry.Vms)
        {
            var type = Interop.TypeName(o) ?? "";
            if (type.Contains("SubColorSelection"))
            {
                var colorSel = Get(o, "_parent");
                if (colorSel == null) continue;
                if (colorSel is Il2CppObjectBase pb && !seenParents.Add(pb.Pointer)) continue;
                var catModel = Get(Get(colorSel, "_parent"), "_model");
                var catType = Get(catModel, "TransferableGroupType")?.ToString();
                var group = Get(colorSel, "TranslatedDisplayName")?.ToString();
                var subs = Get(colorSel, "_subColorSelections") ?? Get(colorSel, "SubColorSelections");
                if (subs == null) continue;
                foreach (var s in Interop.Enumerate(subs))
                {
                    var name = Get(s, "TranslatedDisplayName")?.ToString();
                    var id = $"{catType}|{group}|{name}";
                    long seq = ColorRegistry.SeqOf(s);
                    if (bestSeq.TryGetValue(id, out var cur) && seq <= cur) continue;
                    var (sig, groups) = SharedInfo(s);
                    if (idxById.TryGetValue(id, out var idx))
                    {
                        Entries[idx].Obj = s; Entries[idx].Sig = sig; Entries[idx].Group = groups;
                    }
                    else
                    {
                        idxById[id] = Entries.Count;
                        Entries.Add(new ColorEntry
                        {
                            Id = id, Obj = s, SetMethod = "set_Color",
                            Label = $"{catType?.Split('.').Last()}/{group}/{name}", Sig = sig, Group = groups,
                        });
                    }
                    bestSeq[id] = seq;
                }
            }
            else if (_colorHairVm == null && type.EndsWith("HairEditor.ViewModel"))
            {
                // Fallback capture. Normally _colorHairVm is set by the hooks to the latest instance
                // the editor constructed/used, so the panel-active signal and the setter agree even
                // if several instances linger across model (re)loads.
                _colorHairVm = o;
            }
        }
        if (_colorHairVm != null)
        {
            Entries.Add(new ColorEntry { Id = "Hair|Main", Obj = _colorHairVm, SetMethod = "set_HairMaterialTemplateColor", Label = "Hair/Main" });
            Entries.Add(new ColorEntry { Id = "Hair|Emission", Obj = _colorHairVm, SetMethod = "set_HairMaterialTemplateEmissionColor", Label = "Hair/Emission" });
        }
    }

    /// <summary>Number of color leaves a category exposes, read from the eager model collection
    /// (CategoryColorSelectionCollection.Get), so it is correct before the category is entered.</summary>
    public static int CountForType(object selector, string catType)
    {
        try
        {
            if (selector == null || string.IsNullOrEmpty(catType)) return 0;
            var coll = GetBaseField(selector, "_categoryColorSelectionCollection");
            if (coll == null) return 0;
            var ccs = Invoke(coll, "Get", catType);
            if (ccs == null) return 0;
            int grouped = CountLeaves(Get(ccs, "_groupedSubColorSelections"));
            if (grouped > 0) return grouped;
            int n = 0;
            foreach (var cs in Enumerate(Get(ccs, "_colorSelections")))
                n += CountLeaves(Get(cs, "_subColorSelections"));
            return n;
        }
        catch { return 0; }
    }

    private static int CountLeaves(object collection)
    {
        if (collection == null) return 0;
        int n = 0;
        foreach (var _ in Enumerate(collection)) n++;
        return n;
    }

    /// <summary>Identity of a SubColorSelection's accessor: the exact transferable keys it writes.
    /// Two colours with the same signature are aliases (setting either changes both); the group list
    /// tells whether the accessor spans several transferable groups (a cross-cutting base colour).</summary>
    private static (string sig, string groups) SharedInfo(object subVm)
    {
        try
        {
            var model = Get(subVm, "_model") ?? subVm;
            var accessor = Invoke(model, "get_Accessor") ?? Get(model, "_accessor");
            if (accessor == null) return (null, null);
            var keys = Get(accessor, "_keysToSet") ?? Get(accessor, "_keysToModify");
            if (keys == null) return (null, null);
            var parts = new List<string>();
            var groups = new List<string>();
            foreach (var k in Enumerate(keys))
            {
                var g = (Invoke(k, "get_TransferableGroupType") ?? Get(k, "_transferableGroupType")) as string;
                var key = (Invoke(k, "get_Key") ?? Get(k, "_key")) as string;
                if (string.IsNullOrEmpty(g)) continue;
                if (!groups.Contains(g)) groups.Add(g);
                parts.Add($"{g}:{key}");
            }
            if (parts.Count == 0) return (null, null);
            parts.Sort(); groups.Sort();
            return (string.Join(";", parts), string.Join(",", groups));
        }
        catch { return (null, null); }
    }

    // Reflection Get does not return private fields declared on base types; walk them via AccessTools.
    private static object GetBaseField(object instance, string name)
    {
        var v = Get(instance, name);
        if (v != null) return v;
        try
        {
            var o = RealObj(instance);
            return HarmonyLib.AccessTools.Field(o.GetType(), name)?.GetValue(o);
        }
        catch { return v; }
    }

    /// <summary>True when the accessor signature maps to more than one transferable group across the
    /// registry — i.e. the colour is a single shared value (the base skin colour).</summary>
    public static bool IsSharedSig(string sig)
    {
        if (string.IsNullOrEmpty(sig)) return false;
        var groups = new HashSet<string>();
        foreach (var e in Entries)
        {
            if (e.Sig != sig) continue;
            foreach (var g in (e.Group ?? "").Split(','))
                if (g.Length > 0) groups.Add(g);
        }
        return groups.Count > 1;
    }
}
