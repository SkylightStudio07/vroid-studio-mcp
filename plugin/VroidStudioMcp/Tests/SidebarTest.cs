using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace VroidStudioMcp;

/// <summary>
/// Section switching must change the fixed-size UI chrome: the top section tab bar (active-tab
/// underline moves) and the left tool strip. Hashes a fixed fractional region of the window for each
/// section and requires all four to differ - a UI-level "the menu actually switched" check that does
/// not depend on the 3D preview.
/// </summary>
public sealed class SidebarTest : VroidTest
{
    private static readonly string[] Sections = { "FaceEditor", "HairEditor", "BodyEditor", "ClothingEditor" };

    protected override IEnumerator Run()
    {
        yield return Open();

        var hashes = new Dictionary<string, string>();
        bool allNonNull = true;
        foreach (var s in Sections)
        {
            yield return SwitchSection(s);
            yield return StableRegionHash(0.02f, 0.0f, 0.40f, 0.05f);   // section tab bar + active underline
            string top = Stable;
            yield return StableRegionHash(0.0f, 0.04f, 0.035f, 0.46f);  // left tool strip
            string left = Stable;
            hashes[s] = (top ?? "?") + ":" + (left ?? "?");
            if (top == null || left == null) allNonNull = false;
        }

        int distinct = hashes.Values.Distinct().Count();
        bool ok = allNonNull && distinct == Sections.Length;
        var detail = new Dictionary<string, object> { ["distinct"] = distinct, ["hashes"] = hashes };
        WriteResult("sidebar_result.json", new Dictionary<string, object> { ["ok"] = ok, ["distinct"] = distinct, ["hashes"] = hashes });
        Report("sidebar", ok, detail);
    }
}
