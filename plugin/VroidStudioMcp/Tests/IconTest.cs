using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using static VroidStudioMcp.Interop;

using static VroidStudioMcp.Registries;

namespace VroidStudioMcp;

/// <summary>Extract preset icons for all (or filtered) sections into run/game/icons.</summary>
public sealed class IconTest : VroidTest
{
    private static readonly string[] AllSections = { "FaceEditor", "HairEditor", "BodyEditor", "ClothingEditor" };

    protected override IEnumerator Run()
    {
        yield return Open();
        var root = Path.Combine(BepInEx.Paths.GameRootPath, "icons");
        Directory.CreateDirectory(root);
        var manifest = new List<Dictionary<string, object>>();
        int ok = 0, fail = 0;

        var sections = AllSections.ToList();

        foreach (var section in sections)
        {
            yield return SwitchSection(section);
            var selector = SelectorFor(section);
            foreach (var tab in Enumerate(Get(Get(selector, "Categories"), "Children")).ToList())
            {
                Invoke(selector, "set_SelectedCategory", tab);
                yield return Pause(0.6f);

                var presetNode = Get(tab, "PresetCategoryViewModel");
                var src = (object)presetNode ?? tab;
                var options = Enumerate(Get(src, "PresetOptions"))
                    .Where(o => !string.IsNullOrEmpty(Get(Get(o, "Model"), "resourceName") as string)).ToList();

                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    Icons.Prepare(option);
                    object preview = null;
                    yield return WaitUntil(() => { preview = Get(option, "Preview"); return preview != null; }, 4f);
                    if (preview == null) { fail++; continue; }

                    var task = Invoke(preview, "Create");
                    yield return WaitUntil(() => Get(task, "IsCompleted") is bool b && b, 3f);
                    var png = Icons.TextureToPng(Get(task, "Result"));
                    if (png == null) { fail++; continue; }

                    var resourceName = Get(Get(option, "Model"), "resourceName") as string;
                    var dir = Path.Combine(root, Sanitize(section), Sanitize(Get(tab, "Name") as string ?? "cat"));
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, $"{i:D3}_{Sanitize(resourceName.Split('/').Last())}.png");
                    File.WriteAllBytes(file, png);
                    ok++;
                    manifest.Add(new Dictionary<string, object>
                    {
                        ["selector"] = section,
                        ["category"] = Get(tab, "Name"),
                        ["resourceName"] = resourceName,
                        ["file"] = Path.GetRelativePath(root, file).Replace('\\', '/'),
                    });
                }
                WriteManifest(root, manifest, ok, fail);
            }
        }

        Report("icons", ok > 0, new Dictionary<string, object> { ["ok"] = ok, ["fail"] = fail });
        PluginLog.LogWarning($"icons: done ok={ok} fail={fail}");
    }

    private static void WriteManifest(string root, List<Dictionary<string, object>> manifest, int ok, int fail)
    {
        var payload = new Dictionary<string, object> { ["count"] = manifest.Count, ["ok"] = ok, ["fail"] = fail, ["icons"] = manifest };
        File.WriteAllText(Path.Combine(root, "manifest.json"), MiniJson.Serialize(payload, true), Utf8);
    }
}
