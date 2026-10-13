using System.Collections;

namespace VroidStudioMcp;

/// <summary>i18n overlay commands (dev/TCP only).</summary>
public static class I18nHandlers
{
    public static IEnumerator Dump(CommandDispatcher.Context ctx)
    {
        ctx.Result = MessagesI18n.Dump(CommandUtil.Arg(ctx, 0), out var error);
        if (error != null) ctx.Error = error;
        yield break;
    }

    public static IEnumerator Reload(CommandDispatcher.Context ctx)
    {
        MessagesI18n.Reload();
        ctx.Result = MessagesI18n.Count;
        yield break;
    }

    public static IEnumerator Lang(CommandDispatcher.Context ctx)
    {
        ctx.Result = MessagesI18n.Language(CommandUtil.Arg(ctx, 0));
        yield break;
    }
}
