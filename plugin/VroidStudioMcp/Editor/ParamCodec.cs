using System;

namespace VroidStudioMcp;

/// <summary>Parsing/formatting of parameter values (colours and finite floats) shared by the
/// command surface and the parameter model.</summary>
public static class ParamCodec
{
    public static string Hex(object color)
    {
        if (color is not UnityEngine.Color c) return null;
        // Clamp each channel: a stored value slightly outside 0..1 (engine does not enforce it) must
        // never emit a malformed hex string like "#1FE0000".
        return $"#{Channel(c.r):X2}{Channel(c.g):X2}{Channel(c.b):X2}";
    }

    public static int Channel(float v) =>
        Math.Clamp((int)Math.Round(v * 255, MidpointRounding.AwayFromZero), 0, 255);

    public static bool TryParseColor(string text, out UnityEngine.Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(text)) return false;
        if (text[0] == '#')
        {
            var hex = text.Substring(1);
            // Only #RRGGBB: an #RRGGBBAA value would be silently stripped of its alpha on read-back
            // (Hex() emits RGB only), so reject it instead of applying a colour we cannot report.
            if (hex.Length != 6 || !IsHex(hex)) return false;
            float H(int i) => Convert.ToInt32(hex.Substring(i, 2), 16) / 255f;
            color = new UnityEngine.Color(H(0), H(2), H(4), 1f);
            return true;
        }
        var parts = text.Split(',');
        if (parts.Length == 3 &&
            TryParseFinite(parts[0], out var r) &&
            TryParseFinite(parts[1], out var g) &&
            TryParseFinite(parts[2], out var b))
        {
            // Components are defined as 0..1; reject anything else rather than writing a broken colour.
            if (r < 0f || r > 1f || g < 0f || g > 1f || b < 0f || b > 1f) return false;
            color = new UnityEngine.Color(r, g, b, 1f);
            return true;
        }
        return false;
    }

    private static bool IsHex(string s)
    {
        foreach (var ch in s) if (!Uri.IsHexDigit(ch)) return false;
        return true;
    }

    // Reject NaN/Infinity: float.TryParse happily accepts "NaN"/"Infinity", and feeding such a value
    // to the engine's set_CombinedValue hangs the main thread (infinite rebuild). See docs/quirks.md.
    public static bool TryParseFinite(string s, out float f)
    {
        f = 0f;
        if (string.IsNullOrEmpty(s)) return false;
        if (!float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) return false;
        return !float.IsNaN(f) && !float.IsInfinity(f);
    }

    public static bool TryDbl(object v, out double d)
    {
        d = 0;
        if (v == null) return false;
        try { d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); return true; }
        catch { return false; }
    }

    // Parameters are surfaced as floats; the UI shows 3 decimal places, so match it (a raw float
    // would otherwise read back as 0.4000000059604645). min/max are left as-is (integers/UI range).
    public static object SafeNum(object v)
    {
        try { return v == null ? null : (object)Math.Round(Convert.ToDouble(v), 3, MidpointRounding.AwayFromZero); }
        catch { return v?.ToString(); }
    }
}
