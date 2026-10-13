using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

public static class Icons
{
    internal sealed class PreviewItem { public int Index; public string Name; public byte[] Png; }

    // Last batch of preset previews produced by `preset_preview_list`/`preset_preview_range`
    // (read by the MCP front for inline images), in requested order.
    private static readonly List<PreviewItem> _lastPreviews = new();
    internal static IReadOnlyList<PreviewItem> LastPreviews => _lastPreviews;
    internal static void BeginPreviews() => _lastPreviews.Clear();
    internal static void AddPreview(PreviewItem item) => _lastPreviews.Add(item);

    /// <summary>All preset options of a category, in the same order as the `presets` command (incl. Clear).</summary>
    public static List<object> Options(string selectorShort, string tab)
    {
        var selector = Categories.FindSelectorByShort(selectorShort);
        var category = Categories.FindTab(selector, tab);
        if (category == null) return new List<object>();
        Interop.Invoke(selector, "set_SelectedCategory", category);
        var presetNode = Get(category, "PresetCategoryViewModel");
        var src = (object)presetNode ?? category;
        return Enumerate(Get(src, "PresetOptions")).ToList();
    }

    /// <summary>Preset options of a category that carry a real resourceName (skips the Clear option).</summary>
    public static List<object> PresetOptions(string selectorShort, string tab) =>
        Options(selectorShort, tab)
            .Where(o => !string.IsNullOrEmpty(Get(Get(o, "Model"), "resourceName") as string))
            .ToList();

    public static void Prepare(object option) { if (option != null) Invoke(option, "PrepareResources"); }

    public static byte[] TextureToPng(object texture)
    {
        try
        {
            var t2 = texture as UnityEngine.Texture2D ?? (texture as Il2CppObjectBase)?.TryCast<UnityEngine.Texture2D>();
            if (t2 != null) return UnityEngine.ImageConversion.EncodeToPNG(t2);
            var rt = texture as UnityEngine.RenderTexture ?? (texture as Il2CppObjectBase)?.TryCast<UnityEngine.RenderTexture>();
            if (rt != null)
            {
                var prev = UnityEngine.RenderTexture.active;
                UnityEngine.RenderTexture.active = rt;
                var t = new UnityEngine.Texture2D(rt.width, rt.height, UnityEngine.TextureFormat.ARGB32, false);
                t.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
                t.Apply();
                var png = UnityEngine.ImageConversion.EncodeToPNG(t);
                UnityEngine.RenderTexture.active = prev;
                UnityEngine.Object.Destroy(t);
                return png;
            }
        }
        catch (Exception e) { PluginLog.LogError($"icon convert failed: {e.Message}"); }
        return null;
    }
}
