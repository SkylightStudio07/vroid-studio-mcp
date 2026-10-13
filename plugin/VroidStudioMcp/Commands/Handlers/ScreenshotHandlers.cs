using System;
using System.Collections;
using System.Collections.Generic;

namespace VroidStudioMcp;

/// <summary>Preview and full-window screenshots.</summary>
public static class ScreenshotHandlers
{
    public static IEnumerator Screenshot(CommandDispatcher.Context ctx)
    {
        var mode = (CommandUtil.Arg(ctx, 0) ?? "file").ToLowerInvariant();
        if (mode != "file" && mode != "inline" && mode != "both") { ctx.Error = $"screenshot: mode must be file|inline|both (got '{mode}')"; yield break; }
        bool write = mode == "file" || mode == "both";
        VroidStudioMcp.Screenshot.CaptureScreenshot(write);
        ctx.Result = write ? "screenshot.png" : "inline";
    }

    public static IEnumerator UiScreenshot(CommandDispatcher.Context ctx)
    {
        var mode = CommandUtil.Arg(ctx, 0) ?? "inline";
        var png = VroidStudioMcp.Screenshot.FullWindowPng(mode == "file" || mode == "both");
        if (png == null) { ctx.Error = "ui screenshot failed"; yield break; }
        if (mode == "file") { ctx.Result = "ui_screenshot.png"; yield break; }
        ctx.Result = new Dictionary<string, object> { ["bytes"] = png.Length, ["data"] = Convert.ToBase64String(png) };
    }
}
