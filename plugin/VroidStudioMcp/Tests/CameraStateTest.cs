using System;
using System.Collections;
using System.Collections.Generic;

namespace VroidStudioMcp;

/// <summary>Camera commands must settle, report state, and nudge continuously. Focuses the face,
/// checks GetState() carries normalized angles (yaw/pitch) plus distance/projection, and verifies
/// that re-applying the reported angles (a "nudge" of 0) keeps the exact same camera position -
/// i.e. ChangeAngles orbits the current look-at center, not some other pivot.</summary>
public sealed class CameraStateTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();

        CameraApi.ApplyCamera("reset");
        yield return CameraApi.WaitCameraIdle();
        var reset = CameraApi.GetState();

        CameraApi.ApplyCameraPreset("face");
        yield return CameraApi.WaitCameraIdle();
        var face = CameraApi.GetState();
        bool stateOk = HasState(face);

        // Re-apply the reported angles (0° nudge): position must not jump.
        var ang = (Dictionary<string, object>)face["angles"];
        CameraApi.ApplyCamera(System.FormattableString.Invariant($"angles {ang["yaw"]} {ang["pitch"]}"));
        yield return CameraApi.WaitCameraIdle();
        var face2 = CameraApi.GetState();
        bool continuous = Near(face, face2, 0.02);

        // Normalized: within (-180,180].
        bool normalized = InRange(ang["yaw"]) && InRange(ang["pitch"]);

        // reset must still report a valid state
        CameraApi.ApplyCamera("reset");
        yield return CameraApi.WaitCameraIdle();
        bool resetOk = HasState(CameraApi.GetState());

        // projection is sticky; reset restores the mode captured at model build (toggle off/on)
        CameraApi.CaptureInitialProjection();
        var proj0 = CameraApi.GetState()["projection"];
        CameraApi.ApplyCamera("projection");
        yield return CameraApi.WaitCameraIdle();
        var projToggle = CameraApi.GetState()["projection"];
        CameraApi.ApplyCamera("reset");
        yield return CameraApi.WaitCameraIdle();
        var projReset = CameraApi.GetState()["projection"];
        bool projectionOk = !Equals(proj0, projToggle) && Equals(proj0, projReset);

        bool ok = stateOk && continuous && normalized && resetOk && projectionOk;
        Report("camstate", ok, new Dictionary<string, object>
        { ["state"] = stateOk, ["continuous"] = continuous, ["normalized"] = normalized, ["reset"] = resetOk,
          ["projection"] = projectionOk });
        WriteResult("camstate_result.json", new Dictionary<string, object>
        {
            ["ok"] = ok,
            ["reset"] = reset,
            ["focus_face"] = face,
            ["focus_face_renudge"] = face2,
            ["projection"] = new Dictionary<string, object>
            { ["initial"] = proj0, ["toggled"] = projToggle, ["reset"] = projReset },
        });
    }

    private static bool HasState(Dictionary<string, object> s)
    {
        if (s == null || !s.ContainsKey("angles") || !s.ContainsKey("distance") || !s.ContainsKey("projection")) return false;
        return s["angles"] is Dictionary<string, object> a && a.ContainsKey("yaw") && a.ContainsKey("pitch");
    }

    private static bool InRange(object d) => d is double v && v > -180.0001 && v <= 180.0001;

    private static bool Near(Dictionary<string, object> a, Dictionary<string, object> b, double eps)
    {
        if (a["position"] is not Dictionary<string, object> pa || b["position"] is not Dictionary<string, object> pb) return false;
        foreach (var k in new[] { "x", "y", "z" })
            if (Math.Abs((double)pa[k] - (double)pb[k]) > eps) return false;
        return true;
    }
}
