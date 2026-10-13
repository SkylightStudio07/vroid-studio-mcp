using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;


namespace VroidStudioMcp;

/// <summary>Clear all hair, then set Iris colors (Both / R / L) with a paced visual check.</summary>
public sealed class EyeTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();

        yield return SwitchSection("HairEditor");
        TestSupport.ClearAllHair();
        Signals.Arm();
        yield return Settle(1.5f);
        PluginLog.LogWarning($"eyetest: ClearAllHair (render stable {Shot()})");

        yield return SwitchSection("FaceEditor");
        yield return EnterCategory("FaceEditor", "Irises");
        // Frame the face so the iris colour changes are actually visible in the preview crop.
        CameraApi.ApplyCameraPreset("face");
        yield return CameraApi.WaitCameraIdle();

        var any = TestSupport.FindSubByCatType("Iris");
        if (any == null) { Report("iris", false, "no iris SubColorSelection"); yield break; }
        var subs = Enumerate(Get(Get(any, "_parent"), "_subColorSelections")).ToList();

        foreach (var sub in subs)
        {
            var name = Get(sub, "TranslatedDisplayName") as string ?? "";
            UnityEngine.Color color;
            if (name.StartsWith("L ")) color = new UnityEngine.Color(0.85f, 0.1f, 0.1f, 1f);
            else if (name.StartsWith("R ")) color = new UnityEngine.Color(0.1f, 0.1f, 0.85f, 1f);
            else if (name.Contains("Both")) color = new UnityEngine.Color(0.1f, 0.8f, 0.2f, 1f);
            else continue;

            var before = Shot();
            Signals.Arm();
            Invoke(sub, "set_Color", color);
            yield return Settle(1.5f);
            var after = Shot();
            Report($"eye {name}", after != before, new Dictionary<string, object> { ["before"] = before, ["after"] = after });
        }
    }
}
