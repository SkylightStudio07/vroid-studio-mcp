using System.Collections;
using System.Collections.Generic;

namespace VroidStudioMcp;

/// <summary>API surface checks that need no model: strict (case-sensitive) section-name resolution
/// and the masking of editor-only tabs (Expression Editor). Complements the transport-level
/// api_tests; see docs/external-api.md (names) and docs/internal-api.md (Expression Editor).</summary>
public sealed class ApiTest : VroidTest
{
    protected override IEnumerator Run()
    {
        var d = new Dictionary<string, object>
        {
            ["section_exact"] = Sections.ResolveSection("Face"),
            ["section_lower"] = Sections.ResolveSection("face"),
            ["section_upper"] = Sections.ResolveSection("FACE"),
            ["section_enum_leak"] = Sections.ResolveSection("FaceEditor"),
            ["tab_unsupported"] = Categories.IsUnsupportedTab("Expression Editor"),
            ["tab_normal"] = Categories.IsUnsupportedTab("Irises"),
        };

        bool ok = true;
        ok &= (string)d["section_exact"] == "FaceEditor";
        ok &= d["section_lower"] == null;
        ok &= d["section_upper"] == null;
        ok &= d["section_enum_leak"] == null;
        ok &= (bool)d["tab_unsupported"];
        ok &= !(bool)d["tab_normal"];

        Report("apitest", ok, d);
        WriteResult("apitest_result.json", new Dictionary<string, object> { ["ok"] = ok, ["detail"] = d });
        yield break;
    }
}
