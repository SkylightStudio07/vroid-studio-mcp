using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace VroidStudioMcp;

/// <summary>Preset-label overlay: numeric-prefix merge order, last-wins, tombstones, user.json override.
/// Pure file/logic test; no model needed.</summary>
public sealed class LabelTest : VroidTest
{
    protected override IEnumerator Run()
    {
        Directory.CreateDirectory(PresetLabels.Dir);
        var token = "LABELTEST_" + Guid.NewGuid().ToString("N") + "/";
        var a = Path.Combine(PresetLabels.Dir, "00-labeltest-a.json");
        var b = Path.Combine(PresetLabels.Dir, "01-labeltest-b.json");
        var c = Path.Combine(PresetLabels.Dir, "02-labeltest-c.json");
        var userBackup = File.Exists(PresetLabels.UserFile) ? File.ReadAllBytes(PresetLabels.UserFile) : null;

        bool ok;
        var detail = new Dictionary<string, object>();
        try
        {
            File.WriteAllText(a, $"{{\"{token}x\":\"A\",\"{token}y\":\"Y\"}}");
            File.WriteAllText(b, $"{{\"{token}x\":\"B\",\"{token}z\":\"Z\"}}");
            File.WriteAllText(c, $"{{\"{token}y\":\"\"}}");
            PresetLabels.Reload();

            detail["x"] = PresetLabels.Get(token + "x");
            detail["y"] = PresetLabels.Get(token + "y");
            detail["z"] = PresetLabels.Get(token + "z");
            ok = Equals(detail["x"], "B") && detail["y"] == null && Equals(detail["z"], "Z");
        }
        finally
        {
            try { File.Delete(a); File.Delete(b); File.Delete(c); } catch { }
            if (userBackup != null) File.WriteAllBytes(PresetLabels.UserFile, userBackup);
            else if (File.Exists(PresetLabels.UserFile)) File.Delete(PresetLabels.UserFile);
            PresetLabels.Reload();
        }

        Report("labeltest", ok, detail);
        WriteResult("labeltest_result.json", new Dictionary<string, object> { ["ok"] = ok, ["detail"] = detail });
        yield break;
    }
}
