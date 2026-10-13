using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace VroidStudioMcp;

/// <summary>
/// End-to-end "change really changed the render" guard, exercised through the production command
/// handlers (CommandDispatcher.Test*), i.e. the same path TCP/MCP use. For each domain it applies two
/// distinct valid values and asserts the preview hash differs - catching "changed but didn't change".
/// Also asserts read-back matches what was set and that an invalid set is an error with no render
/// change (no silent swallow). Restores the original values at the end.
/// </summary>
public sealed class RenderChangeTest : VroidTest
{
    private bool _ok;

    private static CommandDispatcher.Context Ctx(string method, params string[] args)
        => new() { Method = method, Args = args };

    private static Dictionary<string, object> Param(Dictionary<string, object> enter, string path)
        => (enter != null && enter.TryGetValue("parameters", out var ps) ? ps as List<object> : null)
              ?.OfType<Dictionary<string, object>>()
              .FirstOrDefault(p => (p.TryGetValue("path", out var v) ? v as string : null) == path);

    protected override IEnumerator Run()
    {
        yield return Open();
        _ok = true;
        var detail = new Dictionary<string, object>();

        yield return NumChange(detail);
        yield return ColorChange(detail);
        yield return PresetChange(detail);
        yield return ErrorNoChange(detail);

        detail["ok"] = _ok;
        WriteResult("renderchange_result.json", detail);
        Report("renderchange", _ok, detail);
    }

    // ---- numeric (combined) param ----
    private IEnumerator NumChange(Dictionary<string, object> detail)
    {
        var enter = Ctx("enter", "Face", "Face Sets", "keep");
        yield return CommandDispatcher.TestEnter(enter);
        if (Param(enter.Result as Dictionary<string, object>, "Face/Face Sets/Eye Size X") == null)
        { detail["numeric"] = "param not found"; _ok = false; yield break; }

        var s1 = Ctx("set_param", "Face", "Face Sets", "Face/Face Sets/Eye Size X", "1.0");
        yield return CommandDispatcher.TestSetParam(s1);
        yield return StableShot();
        string hi = Stable;

        var s2 = Ctx("set_param", "Face", "Face Sets", "Face/Face Sets/Eye Size X", "-1.0");
        yield return CommandDispatcher.TestSetParam(s2);
        yield return StableShot();
        string lo = Stable;

        bool changed = hi != null && lo != null && hi != lo;

        // read-back: set 1.0, re-enter, expect ~1.0
        yield return CommandDispatcher.TestSetParam(Ctx("set_param", "Face", "Face Sets", "Face/Face Sets/Eye Size X", "1.0"));
        var re = Ctx("enter", "Face", "Face Sets", "keep");
        yield return CommandDispatcher.TestEnter(re);
        var rp = Param(re.Result as Dictionary<string, object>, "Face/Face Sets/Eye Size X");
        double rb = rp != null && rp.TryGetValue("value", out var vv) ? Convert.ToDouble(vv) : double.NaN;
        bool readback = Math.Abs(rb - 1.0) < 0.001;

        detail["numeric"] = new Dictionary<string, object>
        { ["hi"] = hi, ["lo"] = lo, ["changed"] = changed, ["readback"] = rb, ["set_ok"] = s1.Error == null && s2.Error == null };
        if (!(changed && readback && s1.Error == null && s2.Error == null)) _ok = false;

        yield return CommandDispatcher.TestSetParam(Ctx("set_param", "Face", "Face Sets", "Face/Face Sets/Eye Size X", "0.537"));
    }

    // ---- color ----
    private IEnumerator ColorChange(Dictionary<string, object> detail)
    {
        var enter = Ctx("enter", "Face", "Irises", "keep");
        yield return CommandDispatcher.TestEnter(enter);
        if (enter.Error != null) { detail["color"] = "enter failed: " + enter.Error; _ok = false; yield break; }

        var r = Ctx("set_param", "Face", "Irises", "Face/Irises/Both Eyes", "#FF0000");
        yield return CommandDispatcher.TestSetParam(r);
        yield return StableShot();
        string red = Stable;

        var g = Ctx("set_param", "Face", "Irises", "Face/Irises/Both Eyes", "#00FF00");
        yield return CommandDispatcher.TestSetParam(g);
        yield return StableShot();
        string green = Stable;

        bool changed = red != null && green != null && red != green;

        var re = Ctx("enter", "Face", "Irises", "keep");
        yield return CommandDispatcher.TestEnter(re);
        var rp = Param(re.Result as Dictionary<string, object>, "Face/Irises/Both Eyes");
        string rb = rp != null && rp.TryGetValue("value", out var vv) ? vv as string : null;
        bool readback = string.Equals(rb, "#00FF00", StringComparison.OrdinalIgnoreCase);

        detail["color"] = new Dictionary<string, object>
        { ["red"] = red, ["green"] = green, ["changed"] = changed, ["readback"] = rb, ["set_ok"] = r.Error == null && g.Error == null };
        if (!(changed && readback && r.Error == null && g.Error == null)) _ok = false;

        yield return CommandDispatcher.TestSetParam(Ctx("set_param", "Face", "Irises", "Face/Irises/Both Eyes", "#6C4CF1"));
    }

    // ---- preset ----
    private IEnumerator PresetChange(Dictionary<string, object> detail)
    {
        var enter = Ctx("enter", "Outfit", "Tops", "keep");
        yield return CommandDispatcher.TestEnter(enter);
        if (enter.Error != null) { detail["preset"] = "enter failed: " + enter.Error; _ok = false; yield break; }

        var a = Ctx("select_preset", "Outfit", "Tops", "3");
        yield return CommandDispatcher.TestSelectPreset(a);
        yield return StableShot();
        string ha = Stable;

        var b = Ctx("select_preset", "Outfit", "Tops", "4");
        yield return CommandDispatcher.TestSelectPreset(b);
        yield return StableShot();
        string hb = Stable;

        bool changed = ha != null && hb != null && ha != hb;
        detail["preset"] = new Dictionary<string, object>
        { ["a"] = ha, ["b"] = hb, ["changed"] = changed, ["set_ok"] = a.Error == null && b.Error == null };
        if (!(changed && a.Error == null && b.Error == null)) _ok = false;
    }

    // ---- invalid set must error and not change the render ----
    private IEnumerator ErrorNoChange(Dictionary<string, object> detail)
    {
        yield return CommandDispatcher.TestEnter(Ctx("enter", "Face", "Face Sets", "keep"));
        yield return StableShot();
        string before = Stable;

        var bad = Ctx("set_param", "Face", "Face Sets", "Face/Face Sets/Eye Size X", "NaN");
        yield return CommandDispatcher.TestSetParam(bad);
        var bad2 = Ctx("set_param", "Face", "Face Sets", "Face/Face Sets/No Such Param", "0");
        yield return CommandDispatcher.TestSetParam(bad2);

        yield return StableShot();
        string after = Stable;
        bool errored = bad.Error != null && bad2.Error != null;
        bool unchanged = before != null && before == after;

        detail["error"] = new Dictionary<string, object>
        { ["nan_error"] = bad.Error, ["missing_error"] = bad2.Error, ["unchanged"] = unchanged };
        if (!(errored && unchanged)) _ok = false;
    }
}
