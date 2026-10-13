using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Dump ThumbnailCameraConfigResolver output per category (recon).</summary>
public sealed class CamTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();

        var tgTypes = new[]
        {
            "TransferableGroupType.N00.Level0.FaceSet",
            "TransferableGroupType.N00.Level3.Iris",
            "TransferableGroupType.N00.Level1.FaceSkin",
            "TransferableGroupType.N00.Level1.BaseHair",
            "TransferableGroupType.N00.Level0.HairSet",
            "TransferableGroupType.N00.Level1.Tops",
            "TransferableGroupType.N00.Level1.Shoes",
            "TransferableGroupType.N00.Level1.BodySkin",
        };
        var type = FindType("VRoid.Studio.Thumbnail.ThumbnailCameraConfigResolver");
        var resolve = type != null ? AccessTools.Method(type, "Resolve") : null;
        var results = new List<Dictionary<string, object>>();
        foreach (var tg in tgTypes)
        {
            object cfg = null;
            try { cfg = resolve?.Invoke(null, new object[] { tg }); } catch (Exception e) { PluginLog.LogError($"camtest resolve {tg}: {e.Message}"); }
            var euler = cfg != null ? SafeStr(Get(cfg, "EulerAngles")) : "null";
            var margin = cfg != null ? SafeStr(Get(cfg, "Margin")) : "null";
            results.Add(new Dictionary<string, object> { ["tg"] = tg, ["euler"] = euler, ["margin"] = margin });
            PluginLog.LogWarning($"CAMTEST {tg} euler={euler} margin={margin}");
        }
        WriteResult("cam_result.json", new Dictionary<string, object> { ["configs"] = results });
    }
}

/// <summary>Paced cycle through camera region presets (for visual inspection).</summary>
public sealed class CamWalk : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        for (int i = 0; i < CameraApi._cwPresets.Length; i++)
        {
            CameraApi.ApplyCameraPreset(CameraApi._cwPresets[i]);
            PluginLog.LogWarning($"camwalk: [{i + 1}/{CameraApi._cwPresets.Length}] {CameraApi._cwPresets[i]}");
            yield return Pause(1.5f);
        }
    }
}
