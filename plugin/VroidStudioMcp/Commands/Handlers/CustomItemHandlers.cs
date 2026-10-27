using System.Collections;

namespace VroidStudioMcp;

public static class CustomItemHandlers
{
    public static IEnumerator List(CommandDispatcher.Context ctx)
    {
        var section = CommandUtil.ResolveSectionArg(ctx,0,"custom_item_list requires section and category");
        var category = CommandUtil.Arg(ctx,1);
        if (section == null) yield break;
        if (category == null) { ctx.Error = "category is required"; yield break; }
        yield return CommandUtil.EnterSection(section);
        ctx.Result = CustomItems.List(section,category);
    }
}
