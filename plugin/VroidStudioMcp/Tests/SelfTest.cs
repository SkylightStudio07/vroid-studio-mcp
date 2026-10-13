using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using static VroidStudioMcp.VroidState;
using static VroidStudioMcp.CameraApi;
using static VroidStudioMcp.Screenshot;

namespace VroidStudioMcp;

public sealed class SelfTest : VroidTest
{
    protected override IEnumerator Run()
    {
        var sw = Perf.Start();
        yield return Open();
        yield return Pause(0.3f);

        CaptureScreenshot();
        var shot1 = LastCanonicalPng;
        ApplyCamera("angles 90 0");
        yield return Pause(0.6f);
        CaptureScreenshot();
        var cameraChanged = shot1 != null && LastCanonicalPng != null && !shot1.SequenceEqual(LastCanonicalPng);

        var result = new Dictionary<string, object>
        {
            ["totalMs"] = Perf.Since("selftest: total", sw),
            ["selectors"] = Selectors.Count,
            ["cameraController"] = _cameraController != null,
            ["previewRt"] = ResolvePreviewRT() != null,
            ["avatarCamera"] = AvatarCamera() != null,
            ["screenshotExists"] = File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "screenshot.png")),
            ["cameraChanged"] = cameraChanged,
        };
        WriteResult("selftest_result.json", result);
        PluginLog.LogWarning($"selftest: cameraChanged={cameraChanged}");
    }
}
