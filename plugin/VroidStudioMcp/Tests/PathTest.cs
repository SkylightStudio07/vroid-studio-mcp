using System.Collections;
using System.Collections.Generic;

namespace VroidStudioMcp;

/// <summary>Host&lt;-&gt;Wine path translation. Model-free: verifies Wine detection, the "Z: is the
/// host root" round-trip and Windows-path passthrough. On real Windows translation is a no-op.</summary>
public sealed class PathTest : VroidTest
{
    protected override IEnumerator Run()
    {
        var d = new Dictionary<string, object>
        {
            ["isWine"] = WinePaths.IsWine,
            ["dos_tmp"] = WinePaths.ToDos("/tmp"),
            ["host_roundtrip"] = WinePaths.ToHost(WinePaths.ToDos("/tmp/x.vroid")),
            ["win_passthrough"] = WinePaths.ToDos("C:\\Users\\x"),
            ["isWin1"] = WinePaths.IsWindowsPath("Z:\\a"),
            ["isWin2"] = WinePaths.IsWindowsPath("/a"),
        };

        bool ok = true;
        ok &= (bool)d["isWin1"] && !(bool)d["isWin2"];
        ok &= (string)d["win_passthrough"] == "C:\\Users\\x";
        if ((bool)d["isWine"])
        {
            ok &= (string)d["host_roundtrip"] == "/tmp/x.vroid";
            ok &= (string)d["dos_tmp"] == "Z:\\tmp";
        }

        Report("pathtest", ok, d);
        WriteResult("pathtest_result.json", new Dictionary<string, object> { ["ok"] = ok, ["detail"] = d });
        yield break;
    }
}
