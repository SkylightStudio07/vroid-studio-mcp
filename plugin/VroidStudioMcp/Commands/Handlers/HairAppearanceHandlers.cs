using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace VroidStudioMcp;

public static class HairAppearanceHandlers
{
    public static IEnumerator State(CommandDispatcher.Context ctx)
    {
        ctx.Result = HairAppearance.State();
        yield break;
    }

    public static IEnumerator Outline(CommandDispatcher.Context ctx)
    {
        HairGeometry.CheckRevision(CommandUtil.Arg(ctx,1));
        if (!ParamCodec.TryParseFinite(CommandUtil.Arg(ctx,0),out float width) || width < 0 || width > 1)
            throw new ArgumentException("Outline width must be finite and in 0..1 native units");
        float before = HairAppearance.Width();
        if (before == width)
        { ctx.Result = new Dictionary<string,object> { ["changed"] = false, ["revision"] = HairGeometry.Revision }; yield break; }
        long revision = HairGeometry.Revision;
        string file = Path.ChangeExtension(TextureEditor.AllowedPath("hair-checkpoints/" + Guid.NewGuid().ToString("N") + ".png"),".vroid");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        var save = new CommandDispatcher.Context { Args = new[] {file} };
        yield return SessionHandlers.Save(save);
        if (save.Error != null) { ctx.Error = save.Error; yield break; }
        if (HairGeometry.Revision != revision) { ctx.Error = "Concurrent edit during checkpoint; outline refused"; yield break; }
        int signal = Signals.ModelVersion;
        revision = HairAppearance.SetWidth(width);
        yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating,30f);
        if (Signals.ModelVersion == signal || !VroidState._modelReady || VroidState._modelUpdating ||
            HairGeometry.Revision != revision || Math.Abs(HairAppearance.Width()-width) > 0.00001f)
        { ctx.Error = "Outline verification failed; recover checkpoint: " + file; yield break; }
        string render = null;
        yield return CommandUtil.StablePreview(hash => render = hash);
        if (render == null || HairGeometry.Revision != revision)
        { ctx.Error = "Outline render verification failed; recover checkpoint: " + file; yield break; }
        ctx.Result = new Dictionary<string,object> { ["changed"] = true, ["before"] = before, ["outline_width"] = width,
            ["revision"] = revision, ["checkpoint_path"] = file, ["stable_preview_hash"] = render };
    }

    public static IEnumerator Color(CommandDispatcher.Context ctx)
    {
        HairGeometry.CheckRevision(CommandUtil.Arg(ctx,2));
        string id = CommandUtil.Arg(ctx,0);
        if (!ParamCodec.TryParseColor(CommandUtil.Arg(ctx,1),out var color))
            throw new ArgumentException("Outline color must be #RRGGBB or r,g,b in 0..1");
        var before = HairAppearance.OutlineColor(id);
        if (before.Equals(color))
        { ctx.Result = new Dictionary<string,object> { ["changed"] = false, ["revision"] = HairGeometry.Revision }; yield break; }
        long revision = HairGeometry.Revision;
        string file = Path.ChangeExtension(TextureEditor.AllowedPath("hair-checkpoints/" + Guid.NewGuid().ToString("N") + ".png"),".vroid");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        var save = new CommandDispatcher.Context { Args = new[] {file} };
        yield return SessionHandlers.Save(save);
        if (save.Error != null) { ctx.Error = save.Error; yield break; }
        if (HairGeometry.Revision != revision) { ctx.Error = "Concurrent edit during checkpoint; outline color refused"; yield break; }
        int signal = Signals.ModelVersion;
        revision = HairAppearance.SetOutlineColor(id,color);
        yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating,30f);
        if (Signals.ModelVersion == signal || !VroidState._modelReady || VroidState._modelUpdating || HairGeometry.Revision != revision ||
            !HairAppearance.OutlineColor(id).Equals(color))
        { ctx.Error = "Outline color verification failed; recover checkpoint: " + file; yield break; }
        string render = null;
        yield return CommandUtil.StablePreview(hash => render = hash);
        if (render == null || HairGeometry.Revision != revision)
        { ctx.Error = "Outline color render verification failed; recover checkpoint: " + file; yield break; }
        ctx.Result = new Dictionary<string,object> { ["changed"] = true, ["before"] = ParamCodec.Hex(before), ["color"] = ParamCodec.Hex(color),
            ["material_id"] = id, ["revision"] = revision, ["checkpoint_path"] = file, ["stable_preview_hash"] = render };
    }
}
