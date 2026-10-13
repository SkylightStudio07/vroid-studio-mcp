using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;

namespace VroidStudioMcp;

/// <summary>
/// Human-readable labels for presets, keyed by the preset's resource id (resourceName, or itemId
/// as a fallback). VRoid exposes no names, so labels are our own overlay data.
///
/// Storage: every *.json in &lt;GameRoot&gt;/labels is a flat map {resourceId: label}; a file may
/// also wrap it as {"$meta": {...}, "labels": {...}}. Files are merged in ascending filename order
/// (Linux conf.d style, use numeric prefixes) and the last definition wins. `label_set` writes
/// 99-user.json, which sorts late on purpose. In memory this is a single flat map.
/// </summary>
public static class PresetLabels
{
    private static readonly Dictionary<string, string> Merged = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> UserOverride = new(StringComparer.Ordinal);
    private static readonly List<string> LoadedFiles = new();
    private static bool _loaded;
    private static string _dir;

    public static string Dir => _dir ??= Path.Combine(BepInEx.Paths.GameRootPath, "labels");
    public static string UserFile => Path.Combine(Dir, "99-user.json");
    public static int Count => Merged.Count;
    public static IReadOnlyList<string> Files => LoadedFiles;

    public static void EnsureLoaded() { if (!_loaded) Reload(); }

    public static void Reload()
    {
        _loaded = true;
        Merged.Clear();
        UserOverride.Clear();
        LoadedFiles.Clear();
        try
        {
            Directory.CreateDirectory(Dir);
            var files = Directory.EnumerateFiles(Dir, "*.json")
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();
            foreach (var f in files)
                LoadFile(f, IsUserFile(f) ? UserOverride : null);
        }
        catch (Exception e) { PluginLog.LogError($"labels: reload failed: {e.Message}"); }
    }

    private static bool IsUserFile(string path) =>
        Path.GetFileName(path).Equals("99-user.json", StringComparison.OrdinalIgnoreCase);

    private static void LoadFile(string file, Dictionary<string, string> mirror)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var body = root;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Object)
                body = l;
            if (body.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in body.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var text = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    // empty string is a tombstone: it clears an earlier (e.g. curated) label
                    Merged.Remove(prop.Name);
                    if (mirror != null) mirror[prop.Name] = "";
                }
                else
                {
                    Merged[prop.Name] = text;
                    if (mirror != null) mirror[prop.Name] = text;
                }
            }
            LoadedFiles.Add(Path.GetFileName(file));
        }
        catch (Exception e) { PluginLog.LogError($"labels: {Path.GetFileName(file)}: {e.Message}"); }
    }

    public static string Get(string resourceId)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(resourceId)) return null;
        return Merged.TryGetValue(resourceId, out var v) ? v : null;
    }

    /// <summary>Look up by resourceName first, then by itemId.</summary>
    public static string Get(string resourceName, string itemId)
    {
        var label = Get(resourceName);
        return label ?? Get(itemId);
    }

    public static void Set(string resourceId, string label)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(resourceId)) return;
        var text = label?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            UserOverride[resourceId] = "";
            Merged.Remove(resourceId);
        }
        else
        {
            UserOverride[resourceId] = text;
            Merged[resourceId] = text;
        }
        SaveUser();
    }

    private static void SaveUser()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(UserFile, MiniJson.Serialize(UserOverride, true));
        }
        catch (Exception e) { PluginLog.LogError($"labels: save failed: {e.Message}"); }
    }
}
