using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>set_param: address one parameter of a category by index/path/name and apply it.</summary>
public static class ParamHandlers
{
    // Address a parameter by index or name. section/category are REQUIRED on purpose: a shorter form
    // that reuses the "last entered category" was deliberately removed - hidden state makes correctness
    // depend on call history and can silently write to the wrong category after a rebuild/re-enter.
    public static IEnumerator Set(CommandDispatcher.Context ctx)
    {
        var section = CommandUtil.ResolveSectionArg(ctx, 0, "usage: set_param <section> <category> <index|name> <value>"); var tab = CommandUtil.Arg(ctx, 1); var sel = CommandUtil.Arg(ctx, 2); var value = CommandUtil.Arg(ctx, 3);
        if (section == null) yield break;
        if (tab == null || sel == null || value == null)
        { ctx.Error = "usage: set_param <section> <category> <index|name> <value>"; yield break; }
        yield return CommandUtil.EnterSection(section);
        var category = CommandUtil.EnterCategory(section, tab);
        if (category == null) { ctx.Error = $"no tab '{tab}'{CommandUtil.TabHint(section)}"; yield break; }
        yield return CommandUtil.Pause(0.6f);
        var list = Parameters.Build(section, tab, category);

        Parameters.ParamRef param;
        if (int.TryParse(sel, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i))
        {
            if (i < 0 || i >= list.Count) { ctx.Error = "index out of range"; yield break; }
            param = list[i];
        }
        else
        {
            var matches = list.Where(p =>
                string.Equals(p.Path, sel, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Label, sel, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, sel, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Id, sel, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Key, sel, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                // A near-miss on the global hair colours gets a targeted hint: either the tab is empty
                // (no preset) or the caller used a tab-prefixed path instead of the stable one.
                if (section == "HairEditor" && tab != "Base Hair" && IsHairGlobalSelector(sel))
                    ctx.Error = list.Count == 0
                        ? $"no parameter '{sel}' in '{tab}': select a preset first; global hair colours are shared across non-Base tabs"
                        : $"no parameter '{sel}' in '{tab}'; global hair colours are addressed as 'Hairstyle/Main' / 'Hairstyle/Emission'";
                else ctx.Error = $"no parameter '{sel}' in '{tab}'";
                yield break;
            }
            if (matches.Count > 1) { ctx.Error = $"ambiguous parameter '{sel}' ({matches.Count} matches)"; yield break; }
            param = matches[0]; i = param.Index;
        }

        bool clamped = false;
        if (param.Kind == "color")
        {
            if (!ParamCodec.TryParseColor(value, out var color)) { ctx.Error = "color must be #RRGGBB or r,g,b (0-1)"; yield break; }
            Signals.Arm();
            Invoke(param.Target, param.Setter ?? "set_Color", color);
            // Normalise the echo so the client sees the canonical form it can feed back (e.g. "0,1,0"
            // -> "#00FF00"), matching what enter reports.
            value = ParamCodec.Hex(color);
        }
        else
        {
            if (!ParamCodec.TryParseFinite(value, out var f)) { ctx.Error = "value must be a finite float"; yield break; }
            // min/max mirror the UI slider range. The engine itself does not clamp (preset defaults
            // can even sit outside the range). Clamp to the union of the slider range and the current
            // value, so read-modify-write never moves an existing out-of-range default, while clearly
            // bogus input is still rejected and reported via clamped:true.
            if (ParamCodec.TryDbl(param.Min, out var min) && ParamCodec.TryDbl(param.Max, out var max) && max > min)
            {
                double lo = min, hi = max;
                if (ParamCodec.TryDbl(param.Value, out var cur)) { lo = Math.Min(lo, cur); hi = Math.Max(hi, cur); }
                if (f < lo) { f = (float)lo; clamped = true; }
                else if (f > hi) { f = (float)hi; clamped = true; }
            }
            Signals.Arm();
            Invoke(param.Target, param.Kind == "combined" ? "set_CombinedValue" : "set_Value", f);
            value = Math.Round((double)f, 3, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        yield return CommandUtil.Settle(1.5f);
        var result = new Dictionary<string, object> { ["index"] = i, ["label"] = param.Label, ["value"] = value };
        if (clamped) result["clamped"] = true;
        ctx.Result = result;
    }

    // True when a selector clearly refers to one of the two global hair colours (stable path, label
    // or id). Used to produce a targeted hint instead of a bare "no parameter".
    private static bool IsHairGlobalSelector(string sel)
    {
        if (string.IsNullOrEmpty(sel)) return false;
        return sel.Equals("Main", StringComparison.OrdinalIgnoreCase)
            || sel.Equals("Emission", StringComparison.OrdinalIgnoreCase)
            || sel.Equals("Hair|Main", StringComparison.OrdinalIgnoreCase)
            || sel.Equals("Hair|Emission", StringComparison.OrdinalIgnoreCase)
            || sel.EndsWith("/Main", StringComparison.OrdinalIgnoreCase)
            || sel.EndsWith("/Emission", StringComparison.OrdinalIgnoreCase);
    }
}
