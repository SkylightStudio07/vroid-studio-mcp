using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VroidStudioMcp;

public static class HairHandlers
{
    private static string Arg(CommandDispatcher.Context ctx, int n) => CommandUtil.Arg(ctx, n);
    public static IEnumerator List(CommandDispatcher.Context ctx) { ctx.Result = HairGeometry.List(); yield break; }
    public static IEnumerator Get(CommandDispatcher.Context ctx) { ctx.Result = HairGeometry.Get(Arg(ctx,0)); yield break; }
    public static IEnumerator Smooth(CommandDispatcher.Context ctx)
    {
        HairGeometry.CheckRevision(Arg(ctx,1));
        string id = Arg(ctx,0);
        if (HairGeometry.Resolve(id).Path.TryCast<VRoidCore.Common.SpecificTypes.BrushHairStrandPath>() == null)
            throw new ArgumentException("HAIR_NOT_SUPPORTED: brush strand required");
        var before = HairGeometry.Read(id);
        long revision = HairGeometry.Revision;
        string file = System.IO.Path.ChangeExtension(TextureEditor.AllowedPath("hair-checkpoints/" + Guid.NewGuid().ToString("N") + ".png"), ".vroid");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
        var save = new CommandDispatcher.Context { Args = new[] { file } };
        yield return SessionHandlers.Save(save);
        if (save.Error != null) { ctx.Error = save.Error; yield break; }
        if (HairGeometry.Revision != revision) {ctx.Error = "Concurrent edit during checkpoint; smooth refused"; yield break;}
        string error = null;
        int signal = Signals.ModelVersion;
        try { revision = HairGeometry.Smooth(id); }
        catch (Exception e) { error = e.Message; }
        if (error != null) {ctx.Error = error + "; recover checkpoint: " + file; yield break;}
        yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating,30f);
        VRoidCore.Common.SpecificTypes.ControlPoint[] after = null;
        try { after = HairGeometry.Read(id); }
        catch (Exception e) { error = e.Message; }
        if (error != null) {ctx.Error = error + "; recover checkpoint: " + file; yield break;}
        string hash = HairGeometry.Hash(after), render = null;
        if (Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating)
            yield return CommandUtil.StablePreview(value => render = value);
        if (render == null || HairGeometry.Revision != revision || HairGeometry.Hash(HairGeometry.Read(id)) != hash)
        {ctx.Error = "Hair smooth verification failed; recover checkpoint: " + file; yield break;}
        ctx.Result = new Dictionary<string,object> { ["revision"] = revision, ["checkpoint_path"] = file,
            ["before_geometry_sha256"] = HairGeometry.Hash(before), ["geometry_sha256"] = hash,
            ["before_point_count"] = before.Length, ["point_count"] = after.Length,
            ["stable_preview_hash"] = render };
    }
    public static IEnumerator Profile(CommandDispatcher.Context ctx)
    {
        HairGeometry.CheckRevision(Arg(ctx,2));
        string id = Arg(ctx,0);
        if (HairGeometry.Resolve(id).Path.TryCast<VRoidCore.Common.SpecificTypes.BrushHairStrandPath>() == null)
            throw new ArgumentException("HAIR_NOT_SUPPORTED: brush strand required");
        var tokens = (Arg(ctx,1) ?? "").Split(',');
        if (tokens.Length < 2 || tokens.Length > 16) throw new ArgumentException("INVALID_HAIR_PROFILE: expected 2..16 x:y pairs");
        var points = new Vector2[tokens.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var pair = tokens[i].Split(':');
            if (pair.Length != 2) throw new ArgumentException("INVALID_HAIR_PROFILE: expected x:y pairs");
            points[i] = new Vector2(Number(pair[0]), Number(pair[1]));
            if (points[i].x < 0 || points[i].x > 1 || points[i].y < 0 || points[i].y > 1.5f ||
                i > 0 && points[i].x <= points[i-1].x)
                throw new ArgumentException("INVALID_HAIR_PROFILE: increasing x in 0..1 and y in 0..1.5 required");
        }
        if (!points[0].Equals(Vector2.zero) || !points[^1].Equals(new Vector2(1,0)))
            throw new ArgumentException("INVALID_HAIR_PROFILE: endpoints must be 0:0 and 1:0");
        var before = HairGeometry.ReadProfile(id);
        string file = System.IO.Path.ChangeExtension(TextureEditor.AllowedPath("hair-checkpoints/" + Guid.NewGuid().ToString("N") + ".png"), ".vroid");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
        long revision = HairGeometry.Revision;
        var save = new CommandDispatcher.Context { Args = new[] { file } };
        yield return SessionHandlers.Save(save);
        if (save.Error != null) { ctx.Error = save.Error; yield break; }
        if (HairGeometry.Revision != revision) {ctx.Error = "Concurrent edit during checkpoint; profile refused"; yield break;}
        int signal = Signals.ModelVersion;
        string error = null;
        try { revision = HairGeometry.SetProfile(id,points); }
        catch (Exception e) { error = e.Message; }
        if (error != null) {ctx.Error = error + "; recover checkpoint: " + file; yield break;}
        yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating,30f);
        Vector2[] after = null;
        try { after = HairGeometry.ReadProfile(id); }
        catch (Exception e) { error = e.Message; }
        if (error != null) {ctx.Error = error + "; recover checkpoint: " + file; yield break;}
        bool match = after.Length == points.Length;
        for (int i = 0; match && i < after.Length; i++) match &= after[i].Equals(points[i]);
        string render = null;
        if (match && Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating)
            yield return CommandUtil.StablePreview(hash => render = hash);
        if (render == null || HairGeometry.Revision != revision)
        {ctx.Error = "Hair profile verification failed; recover checkpoint: " + file; yield break;}
        ctx.Result = new Dictionary<string,object> { ["revision"] = revision, ["checkpoint_path"] = file,
            ["previous_width_curve"] = Array.ConvertAll(before,p => new[] {p.x,p.y}),
            ["width_curve"] = Array.ConvertAll(after,p => new[] {p.x,p.y}), ["stable_preview_hash"] = render };
    }
    public static IEnumerator Visible(CommandDispatcher.Context ctx)
    {
        HairGeometry.CheckRevision(Arg(ctx,2));
        if (!bool.TryParse(Arg(ctx,1),out bool visible)) throw new ArgumentException("Visibility must be true or false");
        bool before = HairGeometry.IsVisible(Arg(ctx,0));
        if (before == visible) { ctx.Result = new Dictionary<string,object> { ["changed"] = false, ["revision"] = HairGeometry.Revision }; yield break; }
        int signal = Signals.ModelVersion;
        long revision = HairGeometry.SetVisible(Arg(ctx,0),visible);
        yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating,30f);
        if (Signals.ModelVersion == signal || !VroidState._modelReady || VroidState._modelUpdating || HairGeometry.Revision != revision || HairGeometry.IsVisible(Arg(ctx,0)) != visible)
        { ctx.Error = "Visibility verification failed; inspect checkpoint/current state"; yield break; }
        string render = null;
        yield return CommandUtil.StablePreview(hash => render = hash);
        if (render == null || HairGeometry.Revision != revision)
        { ctx.Error = "Visibility render verification failed; inspect checkpoint/current state"; yield break; }
        ctx.Result = new Dictionary<string,object> { ["changed"] = true, ["before"] = before, ["visible"] = visible, ["revision"] = revision,
            ["stable_preview_hash"] = render };
    }
    public static IEnumerator EditGroups(CommandDispatcher.Context ctx)
    {
        bool offset = ctx.Method == "hair_offset_front";
        bool scale = ctx.Method == "hair_scale_back";
        bool groupOffset = ctx.Method == "hair_offset_group";
        bool width = ctx.Method == "hair_group_width";
        bool single = groupOffset || width;
        offset |= groupOffset;
        float dx = offset || scale || width ? Number(Arg(ctx,single ? 1 : 0)) : 0;
        if (offset && (Math.Abs(dx) > 0.15f || Math.Abs(dx) < 0.00001f)) throw new ArgumentException("Front offset must be nonzero and <= 0.15 in native guide units");
        if (scale && (dx < 0.5f || dx > 2.5f)) throw new ArgumentException("Guide scale must be 0.5..2.5");
        if (width && (dx < 0.05f || dx > 4f)) throw new ArgumentException("Width scale must be 0.05..4");
        HairGeometry.CheckRevision(Arg(ctx,single ? 2 : offset || scale ? 1 : 0));
        long revision = HairGeometry.Revision;
        var groups = HairGeometry.FrontGroups(width ? null : scale ? ".HairBack" : ".HairFront");
        if (scale)
        {
            groups.RemoveAll(p => HairGeometry.GuideHeight(p) < 0.6f);
            if (groups.Count == 0) throw new ArgumentException("No long back-hair guides (height >= 0.6) found");
        }
        if (single)
        {
            groups.RemoveAll(p => p.HairGroupId != Arg(ctx,0));
            if (groups.Count != 1) throw new ArgumentException("Expected one current brush group in the selected scope");
        }
        string file = System.IO.Path.ChangeExtension(TextureEditor.AllowedPath("hair-checkpoints/" + Guid.NewGuid().ToString("N") + ".png"), ".vroid");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
        var save = new CommandDispatcher.Context { Args = new[] { file } };
        yield return SessionHandlers.Save(save);
        if (save.Error != null) { ctx.Error = save.Error; yield break; }
        if (HairGeometry.Revision != revision) { ctx.Error = "Concurrent edit during checkpoint save; operation refused"; yield break; }
        string error = null;
        int count = 0;
        foreach (var group in groups)
        {
            float before = scale ? HairGeometry.GuideHeight(group) : offset ? HairGeometry.GroupOffsetX(group) : width ? HairGeometry.GroupWidth(group) : 0;
            int signal = Signals.ModelVersion;
            try
            {
                if (HairGeometry.Revision != revision) throw new InvalidOperationException("Concurrent edit: group operation stopped");
                revision = scale ? HairGeometry.ScaleGuide(group,dx) : offset ? HairGeometry.Offset(group,dx) : width ? HairGeometry.SetGroupWidth(group,dx) : HairGeometry.Flip(group);
            }
            catch (Exception e) { error = e.Message; }
            if (error != null) break;
            yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating, 30f);
            if (Signals.ModelVersion == signal || !VroidState._modelReady || VroidState._modelUpdating) { error = "TIMEOUT: group rebuild"; break; }
            count++;
            if (HairGeometry.Revision != revision) { error = "Concurrent edit during group verification"; break; }
            if (scale && Math.Abs(HairGeometry.GuideHeight(group) - before * dx) > 0.0001f ||
                offset && Math.Abs(HairGeometry.GroupOffsetX(group) - before - dx) > 0.0001f ||
                width && Math.Abs(HairGeometry.GroupWidth(group) - dx) > 0.0001f)
            { error = "Group parameter read-back mismatch"; break; }
        }
        string render = null;
        if (error == null)
        {
            yield return CommandUtil.StablePreview(hash => render = hash);
            if (render == null || HairGeometry.Revision != revision) error = "Group render verification failed";
        }
        HairGeometry.Invalidate();
        var result = new Dictionary<string,object> { ["ok"] = error == null, ["groups_modified"] = count,
            ["operation"] = scale ? "guide_height" : offset ? "guide_offset_x" : width ? "group_width" : "mirror",
            ["checkpoint_path"] = file, ["revision"] = revision, ["stable_preview_hash"] = render, ["error"] = error };
        if (error == null) ctx.Result = result;
        else ctx.Error = MiniJson.Serialize(result, false);
    }
    private static float Number(string s)
    {
        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException("INVALID_HAIR_DELTA: finite number required");
        return value;
    }
    public static IEnumerator Move(CommandDispatcher.Context ctx)
    {
        HairGeometry.CheckRevision(Arg(ctx,5));
        if (!int.TryParse(Arg(ctx,1), out int index)) throw new ArgumentException("INVALID_HAIR_POINT: integer index required");
        var delta = new Vector3(Number(Arg(ctx,2)), Number(Arg(ctx,3)), Number(Arg(ctx,4)));
        yield return Mutate(ctx, HairGeometry.Prepare(Arg(ctx,0), index, delta));
    }
    public static IEnumerator Restore(CommandDispatcher.Context ctx)
    {
        HairGeometry.CheckRevision(Arg(ctx,1));
        yield return Mutate(ctx, HairGeometry.PrepareRestore(Arg(ctx,0)));
    }

    private static IEnumerator Mutate(CommandDispatcher.Context ctx, HairGeometry.Backup b)
    {
        string error = null, render = null;
        long revision = HairGeometry.Revision;
        int signal = Signals.ModelVersion;
        try { revision = HairGeometry.Apply(b); }
        catch (Exception e) { error = e.Message; }
        if (error == null)
        {
            yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating, 30f);
            if (Signals.ModelVersion == signal || !VroidState._modelReady || VroidState._modelUpdating)
                error = "TIMEOUT: no completed model rebuild after hair command";
        }
        if (error == null)
        {
            try { if (HairGeometry.Hash(HairGeometry.Read(b.StrandId)) != b.AfterHash) error = "HAIR_VERIFY_FAILED: full strand read-back differs"; }
            catch (Exception e) { error = e.Message; }
        }
        int stable = 0;
        float elapsed = 0;
        while (error == null && stable < 3 && elapsed < 15f)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
            if (VroidState._modelUpdating) { stable = 0; continue; }
            string hash = Screenshot.ShotHash();
            stable = !Screenshot.LastHasContent || hash == "null" || hash == "no-rt" ? 0 : hash == render ? stable + 1 : 1;
            render = hash;
        }
        if (error == null && stable < 3) error = "TIMEOUT: preview did not stabilize";
        if (error == null)
        {
            try { if (HairGeometry.Revision != revision || HairGeometry.Hash(HairGeometry.Read(b.StrandId)) != b.AfterHash)
                    error = "STALE_HAIR_REVISION: concurrent edit during verification"; }
            catch (Exception e) { error = e.Message; }
        }
        if (error != null)
        {
            bool rollback = false;
            string rollbackError = null;
            signal = Signals.ModelVersion;
            try
            {
                if (HairGeometry.Revision != revision) throw new InvalidOperationException("Concurrent edit: rollback refused");
                if (HairGeometry.Hash(HairGeometry.Read(b.StrandId)) == b.BeforeHash) rollback = true;
                else revision = HairGeometry.Apply(b, true);
            }
            catch (Exception e) { rollbackError = e.Message; }
            if (!rollback && rollbackError == null)
            {
                yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating, 30f);
                try { rollback = Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating &&
                        HairGeometry.Revision == revision && HairGeometry.Hash(HairGeometry.Read(b.StrandId)) == b.BeforeHash; }
                catch (Exception e) { rollbackError = e.Message; }
            }
            ctx.Error = MiniJson.Serialize(new Dictionary<string, object> { ["error"] = error,
                ["rollback_applied"] = rollback, ["rollback_error"] = rollbackError,
                ["backup_id"] = b.Id, ["backup_path"] = b.File }, false);
            yield break;
        }
        ctx.Result = new Dictionary<string, object> { ["ok"] = true, ["strand_id"] = b.StrandId, ["point_index"] = b.Index,
            ["revision"] = revision, ["geometry_sha256"] = b.AfterHash, ["backup_id"] = b.Id, ["backup_path"] = b.File,
            ["model_ready"] = true, ["stable_preview_hash"] = render };
    }
}
