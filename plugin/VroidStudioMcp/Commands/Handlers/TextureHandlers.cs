using System;
using System.Collections;
using System.Collections.Generic;

namespace VroidStudioMcp;

public static class TextureHandlers
{
    private static string Arg(CommandDispatcher.Context ctx, int n) => CommandUtil.Arg(ctx,n);
    public static IEnumerator Targets(CommandDispatcher.Context ctx) { ctx.Result = TextureEditor.ListTargets(Arg(ctx,0)); yield break; }
    public static IEnumerator Prepare(CommandDispatcher.Context ctx)
    {
        string group = Arg(ctx,0);
        string section = group switch {
            "TransferableGroupType.N00.Level1.FacePaint" => "Face",
            "TransferableGroupType.N00.Level1.Tops" or "TransferableGroupType.N00.Level1.Bottoms"
                or "TransferableGroupType.N00.Level1.Socks" => "Outfit",
            _ => null
        };
        if (section == null)
        { ctx.Error = "PREPARATION_NOT_SUPPORTED: supported groups are FacePaint, Tops, Bottoms and Socks"; yield break; }
        if (!long.TryParse(Arg(ctx,1), out long revision) || revision != TextureEditor.Revision)
        { ctx.Error = "STALE_TEXTURE_TARGET: expected_revision must equal the current revision"; yield break; }
        string clothingGroupId=null;
        var clothingIds=section=="Outfit"?TextureEditor.ClothingPreparation(group,out clothingGroupId):null;
        var inputs = section=="Face"?TextureEditor.PrepareGroup(group):null;
        if ((inputs?.Count ?? clothingIds.Count) == 0)
        {
            var listing = (Dictionary<string,object>)TextureEditor.ListTargets(section);
            bool found = false;
            foreach (Dictionary<string,object> t in (List<object>)listing["targets"])
                if ((string)t["group_type"] == group) found = true;
            if (!found) { ctx.Error = "PREPARATION_NOT_SUPPORTED: no convertible image keys"; yield break; }
            ctx.Result = new Dictionary<string,object> { ["ok"] = true, ["already_prepared"] = true,
                ["revision"] = revision, ["checkpoint_path"] = null, ["targets"] = listing["targets"] };
            yield break;
        }
        string path = System.IO.Path.ChangeExtension(TextureEditor.AllowedPath("preparation/" + Guid.NewGuid().ToString("N") + ".png"), ".vroid");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        var save = new CommandDispatcher.Context { Args = new[] { path } };
        yield return SessionHandlers.Save(save);
        if (save.Error != null) { ctx.Error = save.Error; yield break; }
        string error = null;
        int signal = Signals.ModelVersion;
        long applied = revision;
        try
        {
            if (TextureEditor.Revision != revision) throw new InvalidOperationException("STALE_TEXTURE_TARGET: model changed during checkpoint save");
            applied = section=="Face"?TextureEditor.ApplyPreparation(inputs):TextureEditor.ApplyClothingPreparation(clothingIds,clothingGroupId);
        }
        catch (Exception e) { error = e.Message; }
        if (error == null)
        {
            yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating, 30f);
            if (Signals.ModelVersion == signal || !VroidState._modelReady || VroidState._modelUpdating) error = "TIMEOUT: preparation did not complete a model rebuild";
        }
        TextureEditor.Invalidate();
        if (error == null)
        {
            try
            {
                if (TextureEditor.Revision != applied) throw new InvalidOperationException("STALE_TEXTURE_TARGET: concurrent edit during preparation");
                var listing = (Dictionary<string, object>)TextureEditor.ListTargets(section);
                bool found = false;
                foreach (Dictionary<string,object> t in (List<object>)listing["targets"])
                    if ((string)t["group_type"] == group) found = true;
                if (!found) throw new InvalidOperationException("PREPARATION_NOT_SUPPORTED: no editable image created");
                ctx.Result = new Dictionary<string, object> { ["ok"] = true, ["group_type"] = group,
                    ["revision"] = applied, ["checkpoint_path"] = path, ["targets"] = listing["targets"] };
            }
            catch (Exception e) { error = e.Message; }
        }
        if (error != null) ctx.Error = MiniJson.Serialize(new Dictionary<string, object> {
            ["error"] = error, ["checkpoint_path"] = path, ["recovery"] = "Inspect state; reopen checkpoint to recover. Preparation is not automatically reversed." }, false);
    }
    public static IEnumerator Layers(CommandDispatcher.Context ctx) { ctx.Result = TextureEditor.ListLayers(Arg(ctx,0)); yield break; }
    public static IEnumerator Info(CommandDispatcher.Context ctx) { ctx.Result = TextureEditor.Info(Arg(ctx,0),Arg(ctx,1)); yield break; }
    public static IEnumerator Export(CommandDispatcher.Context ctx)
    {
        if (Arg(ctx,2) == null) { ctx.Error = "PATH_NOT_ALLOWED: output_path is required"; yield break; }
        ctx.Result = TextureEditor.Info(Arg(ctx,0),Arg(ctx,1),Arg(ctx,2));
    }
    public static IEnumerator Import(CommandDispatcher.Context ctx)
    {
        string alphaMode = Arg(ctx,4) ?? "preserve";
        if (alphaMode != "preserve" && alphaMode != "replace")
        { ctx.Error = "INVALID_ALPHA_MODE: use preserve (default) or replace"; yield break; }
        yield return Mutate(ctx, Arg(ctx,0), Arg(ctx,1), Arg(ctx,2), Arg(ctx,3), false, alphaMode);
    }
    public static IEnumerator Restore(CommandDispatcher.Context ctx)
    {
        var backup = TextureEditor.FindBackup(Arg(ctx,0));
        if (TextureEditor.Validate(backup.TargetId, backup.LayerId, TextureEditor.Input(backup.Path), false) != backup.PixelHash)
        { ctx.Error = "INVALID_PNG: backup pixels no longer match the recorded original"; yield break; }
        yield return Mutate(ctx, backup.TargetId, backup.LayerId, backup.Path, Arg(ctx,1), true);
    }

    private static IEnumerator Mutate(CommandDispatcher.Context ctx, string id, string layer, string input, string expected, bool restoring, string alphaMode = "preserve")
    {
        if (!long.TryParse(expected, out var revision) || revision != TextureEditor.Revision)
        { ctx.Error = "STALE_TEXTURE_TARGET: expected_revision must equal the current revision"; yield break; }
        byte[] png = TextureEditor.Input(input);
        string intended = TextureEditor.Validate(id, layer, png, !restoring && alphaMode == "preserve");
        var backup = TextureEditor.CreateBackup(id,layer);
        string error = null;
        int signal = Signals.ModelVersion;
        long appliedRevision = revision;
        try { appliedRevision = TextureEditor.Apply(id,layer,png); }
        catch (Exception e) { error = e.Message; }
        if (error == null)
        {
            yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating, 30f);
            if (Signals.ModelVersion == signal || VroidState._modelUpdating || !VroidState._modelReady)
                error = "TIMEOUT: no completed model update after texture command";
        }
        if (error == null)
        {
            try { if (TextureEditor.Read(id,layer).PixelHash != intended) error = "IMPORT_NOT_SUPPORTED: layer read-back differs from input"; }
            catch (Exception e) { error = e.Message; }
        }
        string renderHash = null;
        int sameFrames = 0;
        float elapsed = 0;
        while (error == null && sameFrames < 3 && elapsed < 15f)
        {
            yield return null;
            elapsed += UnityEngine.Time.unscaledDeltaTime;
            if (VroidState._modelUpdating) { sameFrames = 0; continue; }
            string hash = Screenshot.ShotHash();
            sameFrames = !Screenshot.LastHasContent ? 0 : hash == renderHash && hash != "no-rt" && hash != "null" ? sameFrames + 1 : 1;
            renderHash = hash;
        }
        if (error == null && (sameFrames < 3 || renderHash == "no-rt" || renderHash == "null")) error = "TIMEOUT: preview did not stabilize";
        if (error == null)
        {
            try { if (TextureEditor.Revision != appliedRevision || TextureEditor.Read(id,layer).PixelHash != intended) error = "STALE_TEXTURE_TARGET: concurrent edit during texture verification"; }
            catch (Exception e) { error = e.Message; }
        }
        if (error != null)
        {
            bool rollback = false;
            string rollbackError = null;
            signal = Signals.ModelVersion;
            try
            {
                if (TextureEditor.Revision != appliedRevision) throw new InvalidOperationException("Concurrent edit: automatic rollback refused; backup retained");
                TextureEditor.Apply(id,layer,TextureEditor.Input(backup.Path));
            }
            catch (Exception e) { rollbackError = e.Message; }
            if (rollbackError == null)
            {
                yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating, 30f);
                try { rollback = Signals.ModelVersion != signal && TextureEditor.Read(id,layer).PixelHash == backup.PixelHash; }
                catch (Exception e) { rollbackError = e.Message; }
            }
            ctx.Error = MiniJson.Serialize(new Dictionary<string, object> { ["ok"] = false,
                ["error"] = error, ["code"] = rollback ? error.Split(':')[0] : "ROLLBACK_FAILED",
                ["rollback_applied"] = rollback, ["rollback_error"] = rollbackError,
                ["backup_id"] = backup.Id, ["backup_path"] = backup.Path }, false);
            yield break;
        }
        ctx.Result = new Dictionary<string, object> { ["ok"] = true, ["target_id"] = id, ["layer_id"] = layer,
            ["revision"] = TextureEditor.Revision, ["backup_id"] = backup.Id, ["backup_path"] = backup.Path,
            ["pixel_sha256"] = intended, ["alpha_mode"] = restoring ? "restore" : alphaMode,
            ["model_ready"] = true, ["stable_preview_hash"] = renderHash };
    }
}
