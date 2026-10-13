using System;
using System.Linq;
using System.Reflection;
using static VroidStudioMcp.Interop;
using static VroidStudioMcp.VroidState;

namespace VroidStudioMcp;

/// <summary>Direct preset-thumbnail fetch straight from the asset store, bypassing the preset
/// ViewModel (PrepareResources/Preview.Create). Built-in thumbnails are Texture2D assets addressed by
/// the preset's resourceName: bundle "vroid-thumbnails-&lt;slug&gt;", path
/// "Assets/vroid-vroidcoredata/AssetBundles/Thumbnails/&lt;bundle&gt;/thumbnail.png". They are fetched
/// synchronously via ReadOnlyBundleManager.OpenReadOnly -> ResourcesFileSystem.GetObject&lt;Texture2D&gt;
/// and PNG-encoded once. Custom items / non-standard names return null so the caller can fall back to
/// the ViewModel. No PNG cache: the engine keeps the Texture2D loaded, the remaining EncodeToPNG cost
/// is ~1-2 ms, and caching would trade tens of MB for a few ms.</summary>
public static class Thumbnails
{
    private const string PathPrefix = "Assets/vroid-vroidcoredata/AssetBundles/Thumbnails/";

    private static object _bundleManager;
    private static MethodInfo _getObject;

    /// <summary>PNG bytes for a built-in preset, or null when the direct path cannot serve it
    /// (custom item, unknown name, no model). Never throws.</summary>
    public static byte[] Get(string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName)) return null;
        if (!TryDerive(resourceName, out var bundle, out var path)) return null;
        try
        {
            var fs = OpenBundle(bundle);
            if (fs == null) return null;
            var tex = LoadTexture(fs, path);
            return Icons.TextureToPng(tex);
        }
        catch (Exception e) { PluginLog.LogError($"thumbnail fetch failed '{resourceName}': {e.Message}"); return null; }
    }

    /// <summary>resourceName -> (bundle, path) for a built-in preset. Exposed for tests.</summary>
    internal static bool TryDerive(string resourceName, out string bundle, out string path)
    {
        bundle = null; path = null;
        var cut = resourceName.IndexOf("/TransferableGroupType.", StringComparison.Ordinal);
        if (cut < 0) return false;
        var slug = resourceName.Substring(0, cut).ToLowerInvariant().Replace('/', '-');
        bundle = "vroid-thumbnails-" + slug;
        path = PathPrefix + bundle + "/thumbnail.png";
        return true;
    }

    private static object OpenBundle(string bundle)
    {
        if (_bundleManager == null)
        {
            var mainModel = Interop.Get(_mainViewModel, "_mainModel");
            _bundleManager = Interop.Get(Interop.Get(mainModel, "assetStorage"), "bundleManager");
        }
        return _bundleManager != null ? Invoke(_bundleManager, "OpenReadOnly", bundle) : null;
    }

    private static object LoadTexture(object fs, string path)
    {
        if (_getObject == null)
            _getObject = (RealType(fs) ?? fs.GetType())
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetObject" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
        if (_getObject == null) return null;
        return _getObject.MakeGenericMethod(FindType("UnityEngine.Texture2D")).Invoke(RealObj(fs), new object[] { path });
    }
}
