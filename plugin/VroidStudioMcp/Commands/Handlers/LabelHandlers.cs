using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Preset label overlay: reload/list and set a label (dev/TCP only).</summary>
public static class LabelHandlers
{
    // Labels are our own overlay data (VRoid exposes no names): flat {resourceId: label} maps merged
    // from <GameRoot>/labels/*.json. Read here, written by label_set into 99-user.json.
    public static IEnumerator Labels(CommandDispatcher.Context ctx)
    {
        PresetLabels.Reload();
        ctx.Result = new Dictionary<string, object>
        {
            ["dir"] = PresetLabels.Dir,
            ["count"] = PresetLabels.Count,
            ["files"] = PresetLabels.Files.ToList(),
        };
        yield break;
    }

    public static IEnumerator LabelSet(CommandDispatcher.Context ctx)
    {
        var section = CommandUtil.ResolveSectionArg(ctx, 0, "usage: label_set <section> <category> <index|resourceName> <text>"); var tab = CommandUtil.Arg(ctx, 1);
        var sel = CommandUtil.Arg(ctx, 2); var text = CommandUtil.Arg(ctx, 3);
        if (section == null) yield break;
        if (tab == null || sel == null || text == null)
        { ctx.Error = "usage: label_set <section> <category> <index|resourceName> <text>"; yield break; }

        string resourceId;
        if (sel.StartsWith("pixiv/", StringComparison.OrdinalIgnoreCase))
        {
            resourceId = sel;
        }
        else
        {
            yield return CommandUtil.EnterSection(section);
            var selector = Categories.FindSelectorByShort(section + ".PresetsSelectorViewModel");
            var category = Categories.FindTab(selector, tab);
            if (category == null) { ctx.Error = $"no tab '{tab}'{CommandUtil.TabHint(section)}"; yield break; }
            var presetNode = Get(category, "PresetCategoryViewModel");
            var src = (object)presetNode ?? category;
            var options = Enumerate(Get(src, "PresetOptions")).ToList();
            if (!int.TryParse(sel, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) || i < 0 || i >= options.Count)
            { ctx.Error = "index out of range"; yield break; }
            var model = Get(options[i], "Model");
            resourceId = (Get(model, "resourceName") as string) ?? (Get(model, "itemId") as string);
        }
        if (string.IsNullOrEmpty(resourceId)) { ctx.Error = "preset has no resource id"; yield break; }

        PresetLabels.Set(resourceId, text);
        ctx.Result = new Dictionary<string, object> { ["resourceName"] = resourceId, ["label"] = text };
    }
}
