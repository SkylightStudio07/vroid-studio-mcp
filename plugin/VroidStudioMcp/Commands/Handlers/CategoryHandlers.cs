using System.Collections;
using System.Collections.Generic;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Section/category listing and category entry (with camera framing).</summary>
public static class CategoryHandlers
{
    public static IEnumerator List(CommandDispatcher.Context ctx)
    {
        var section = CommandUtil.ResolveSectionArg(ctx, 0, "usage: categories <section>");
        if (section == null) yield break;
        yield return CommandUtil.EnterSection(section);
        Colors.RefreshRegistry();
        var selector = Categories.FindSelectorByShort(section + ".PresetsSelectorViewModel");
        var list = new List<object>();
        foreach (var tab in Enumerate(Get(Get(selector, "Categories"), "Children")))
        {
            var tabName = Get(tab, "Name") as string;
            // Hidden editor-only tabs (Expression Editor) are not listed; enter() treats them as
            // nonexistent (Categories.FindTab returns null).
            if (Categories.IsUnsupportedTab(tabName)) continue;
            var paramNode = Get(tab, "ParameterCategoryViewModel");
            var presetNode = Get(tab, "PresetCategoryViewModel");
            int pc = paramNode != null ? Categories.ParamCount(paramNode) : Categories.ParamCount(tab);
            int prc = presetNode != null ? Categories.PresetCount(presetNode) : Categories.PresetCount(tab);
            int colors = System.Math.Max(Parameters.CountColors(section, tabName, tab),
                                         Colors.CountForType(selector, Get(tab, "TransferableGroupType")?.ToString()));
            list.Add(new Dictionary<string, object>
            {
                ["name"] = tabName,
                ["kind"] = Parameters.TabKind(section, tabName, (pc + colors) > 0),
                ["params"] = pc,
                ["colors"] = colors,
                ["presets"] = prc,
                ["has_clear_preset"] = Parameters.HasClearPreset(tab),
            });
        }
        ctx.Result = new Dictionary<string, object>
        {
            // categories switches the editor section as a side effect; report where it landed so the
            // caller never has to infer it from a later `state`.
            ["section"] = Sections.SectionLabel(section),
            ["categories"] = list,
        };
    }

    public static IEnumerator Enter(CommandDispatcher.Context ctx)
    {
        var section = CommandUtil.ResolveSectionArg(ctx, 0, "usage: enter <section> <tab> [view]"); var tab = CommandUtil.Arg(ctx, 1);
        if (section == null) yield break;
        if (tab == null) { ctx.Error = "usage: enter <section> <tab> [view]"; yield break; }
        // view: auto (default) frames the category's subject, keep leaves the camera alone,
        // reset restores the initial view, or an explicit <region>[:<angle>] (body|face|head|
        // upper|legs|feet with an optional front|back|left|right|threequarter_left|threequarter_right).
        var viewRaw = CommandUtil.Arg(ctx, 2) ?? "auto";
        var viewArg = viewRaw.ToLowerInvariant();
        bool viewSpecial = viewArg == "auto" || viewArg == "keep" || viewArg == "reset";
        string viewRegion = null; float viewYaw = 0f;
        if (!viewSpecial && !CameraApi.TryParseView(viewRaw, out viewRegion, out viewYaw))
        { ctx.Error = $"enter: unknown view '{viewRaw}' (auto|keep|reset|<region>[:<angle>])"; yield break; }
        yield return CommandUtil.EnterSection(section);
        var category = CommandUtil.EnterCategory(section, tab);
        if (category == null) { ctx.Error = $"no tab '{tab}'{CommandUtil.TabHint(section)}"; yield break; }
        yield return CommandUtil.Settle(0.6f);
        var result = Parameters.Payload(section, tab, category);
        // Auto-frame the category so the next screenshot shows the right region. 'view' is always
        // reported (symmetric): the applied framing, or 'keep' when the camera was left alone.
        if (viewArg == "keep") result["view"] = "keep";
        else
        {
            if (viewArg == "reset") { CameraApi.ApplyCamera("reset"); result["view"] = "reset"; }
            else if (viewArg == "auto")
            {
                var cv = CategoryView(section, tab);
                if (cv != null) { CameraApi.ApplyCameraView(cv.Value.region, cv.Value.yaw); result["view"] = cv.Value.name; }
                else result["view"] = "keep";
            }
            else { CameraApi.ApplyCameraView(viewRegion, viewYaw); result["view"] = viewArg; }
            // Let the new framing reach the preview RenderTexture before returning, so a screenshot
            // in the same batch captures it (SetCustomView is direct but only visible next render).
            yield return CameraApi.WaitCameraIdle();
        }
        ctx.Result = result;
    }

    // Default camera framing per category (region + optional yaw relative to front).
    private static (string region, float yaw, string name)? CategoryView(string section, string tab)
    {
        switch (section)
        {
            case "FaceEditor": return ("face", 0f, "face");
            case "HairEditor":
                if (tab == "Back") return ("head", 180f, "head:back");
                if (tab == "Side") return ("head", 90f, "head:side");
                return ("head", 0f, "head");
            case "BodyEditor": return ("body", 0f, "body");
            case "ClothingEditor":
                if (tab == "Shoes" || tab == "Socks") return ("feet", 0f, "feet");
                if (tab == "Tops" || tab == "Neck Accessories" || tab == "Arm Accessories") return ("upper", 0f, "upper");
                return ("body", 0f, "body");
            default: return null;
        }
    }
}
