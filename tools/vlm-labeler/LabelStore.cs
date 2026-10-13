using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VroidVlmLabeler;

/// <summary>
/// Reads/writes the plugin's preset-label overlay: every *.json in the labels dir is a flat map
/// {resourceId: label}, merged in ascending filename order (last wins; empty value = tombstone).
/// The harness writes one file per category, e.g. 10-outfit-tops.json.
/// </summary>
public static class LabelStore
{
    public static Dictionary<string, string> LoadMerged(string dir)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(dir)) return merged;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var (key, value) in ReadFile(file))
            {
                if (value.Length == 0) merged.Remove(key);
                else merged[key] = value;
            }
        }
        return merged;
    }

    public static Dictionary<string, string> ReadFile(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return map;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var body = root;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Object)
                body = l;
            if (body.ValueKind != JsonValueKind.Object) return map;
            foreach (var prop in body.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = prop.Value.GetString() ?? "";
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"labels: cannot parse {Path.GetFileName(path)}: {e.Message}");
        }
        return map;
    }

    public static void Save(string dir, string fileName, string section, string category, string model, Dictionary<string, string> labels)
    {
        Directory.CreateDirectory(dir);
        var payload = new Dictionary<string, object>
        {
            ["$meta"] = new Dictionary<string, object>
            {
                ["source"] = "vlm-labeler",
                ["model"] = model,
                ["section"] = section,
                ["category"] = category,
                ["generated"] = DateTime.UtcNow.ToString("o"),
            },
            ["labels"] = labels,
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(dir, fileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, true);
    }

    /// <summary>Filesystem-safe slug for a section/category pair, e.g. "Outfit"/"Whole Sets" -> outfit-whole-sets.</summary>
    public static string Slug(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
