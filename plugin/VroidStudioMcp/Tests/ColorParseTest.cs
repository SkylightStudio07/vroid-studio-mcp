using System;
using System.Collections;
using System.Collections.Generic;

namespace VroidStudioMcp;

/// <summary>Colour parsing/formatting edge cases (model-free): r,g,b must be 0..1, a malformed
/// hex is rejected, and Hex() clamps channels so it can never emit a value like "#1FE0000".</summary>
public sealed class ColorParseTest : VroidTest
{
    protected override IEnumerator Run()
    {
        bool ok = true;
        var d = new Dictionary<string, object>();

        bool oob = ParamCodec.TryParseColor("2,0,0", out _);
        bool mid = ParamCodec.TryParseColor("0.5,0.25,1", out var c2) && Math.Abs(c2.r - 0.5f) < 1e-4f && Math.Abs(c2.b - 1f) < 1e-4f;
        bool badHex = ParamCodec.TryParseColor("#1FE0000", out _);   // 7 hex digits
        bool goodHex = ParamCodec.TryParseColor("#FF8000", out _);
        string clamped = ParamCodec.Hex(new UnityEngine.Color(2f, 0f, 0f, 1f));
        string clamped2 = ParamCodec.Hex(new UnityEngine.Color(-1f, 0.5f, 0.5f, 1f));

        ok &= !oob && mid && !badHex && goodHex;
        ok &= clamped == "#FF0000" && clamped2 == "#008080";

        d["reject_rgb_gt1"] = !oob;
        d["accept_rgb_mid"] = mid;
        d["reject_bad_hex"] = !badHex;
        d["accept_good_hex"] = goodHex;
        d["hex_clamp_hi"] = clamped;
        d["hex_clamp_lo"] = clamped2;

        Report("colorparse", ok, d);
        WriteResult("colorparse_result.json", new Dictionary<string, object> { ["ok"] = ok, ["detail"] = d });
        yield break;
    }
}
