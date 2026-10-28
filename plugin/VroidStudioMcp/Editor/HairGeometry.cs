using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using VRoidCore.Common.SpecificTypes;
using VRoidCore.Editing;
using VRoidCore.Editing.Query;
using VRoidCore.Editing.History.Command;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace VroidStudioMcp;

public static class HairGeometry
{
    internal sealed class Strand
    {
        internal string Id, GroupId, GroupType;
        internal IHairStrandPath Path;
    }
    internal sealed class Backup
    {
        internal string Id, StrandId, File, BeforeHash, AfterHash;
        internal int Index;
        internal ControlPoint[] Before, After;
    }
    private static string _session = Guid.NewGuid().ToString("N");
    private static IntPtr _context;
    private static readonly Dictionary<string, Strand> Strands = new();
    private static readonly Dictionary<string, Backup> Backups = new();
    private static readonly List<Il2CppSystem.Action<BrushHairGroupHairParameterSet>> OffsetCallbacks = new();
    private static readonly List<Il2CppSystem.Action<GuideParameterSet>> GuideCallbacks = new();
    private static readonly List<Il2CppSystem.Action<BrushHairGroupParameterSet>> ParameterCallbacks = new();
    internal const int MaxPoints = 2048;
    internal const float MaxDelta = 0.05f;

    internal static void Invalidate()
    {
        _session = Guid.NewGuid().ToString("N");
        Strands.Clear(); Backups.Clear();
    }
    internal static Context Current()
    {
        var ctx = TextureEditor.Current();
        if (_context != ctx.Pointer) { Invalidate(); _context = ctx.Pointer; }
        return ctx;
    }
    internal static long Revision => Current()._revision;

    internal static List<BrushHairGroupPath> FrontGroups(string suffix = ".HairFront")
    {
        List();
        var result = new List<BrushHairGroupPath>();
        var seen = new HashSet<string>();
        foreach (var strand in Strands.Values.Where(s => suffix == null || (s.GroupType ?? "").EndsWith(suffix)))
        {
            var path = strand.Path.TryCast<BrushHairStrandPath>();
            if (path == null)
            {
                if (suffix == null) continue;
                throw new ArgumentException("HAIR_NOT_SUPPORTED: operation requires brush hair");
            }
            if (seen.Add(path.TransferableId + "/" + path.HairGroupId)) result.Add(path.HairGroupPath);
        }
        if (result.Count == 0 || result.Count > 64) throw new ArgumentException("HAIR_NOT_SUPPORTED: expected 1..64 brush groups");
        return result;
    }
    internal static long Flip(BrushHairGroupPath path)
    {
        var ctx = Current();
        if (!ctx.editingModel.GetIsPathValid(path)) throw new ArgumentException("STALE_HAIR_TARGET: group no longer exists");
        Signals.Arm();
        ctx.ExecuteSyncCommand(new FlipBrushHairGroupCommand(path).Cast<ISyncCommand<Context.HistoryManagerContext, Model>>());
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }
    internal static long Offset(BrushHairGroupPath path, float dx)
    {
        var ctx = Current();
        if (!ctx.editingModel.GetIsPathValid(path)) throw new ArgumentException("STALE_HAIR_TARGET: group no longer exists");
        if (Math.Abs(GroupOffsetX(path) + dx) > 0.3f) throw new ArgumentException("Group offset would exceed native bound 0.3");
        if (OffsetCallbacks.Count >= 256) throw new ArgumentException("Restart the app before more group offset commands");
        var callback = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<BrushHairGroupHairParameterSet>>(
            (Action<BrushHairGroupHairParameterSet>)(p => p.PositionOffsetX += dx));
        OffsetCallbacks.Add(callback);
        var command = new ModifyBrushHairGroupPositionOffsetParameterCommand(Guid.NewGuid().ToString("N"),path,callback);
        Signals.Arm();
        ctx.ExecuteSyncCommand(command.Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }
    internal static long ScaleGuide(BrushHairGroupPath path, float factor)
    {
        var ctx = Current();
        if (!ctx.editingModel.GetIsPathValid(path)) throw new ArgumentException("STALE_HAIR_TARGET: group no longer exists");
        float height = GuideHeight(path) * factor;
        if (!float.IsFinite(height) || height < 0.02f || height > 4f) throw new ArgumentException("Guide height would exceed native bounds 0.02..4");
        if (GuideCallbacks.Count >= 128) throw new ArgumentException("Restart before more guide commands");
        var selector = Categories.FindSelectorByShort("HairEditor.PresetsSelectorViewModel");
        var engine = Interop.Get(selector,"_engineViewModel");
        var reference = (Interop.Invoke(engine,"GetHairGuideReference") as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)
            ?.TryCast<VRoidCore.Common.Algorithm.GuideUtil.Reference>();
        if (reference == null) throw new InvalidOperationException("Hair guide reference unavailable");
        var callback = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<GuideParameterSet>>(
            (Action<GuideParameterSet>)(p => p.Height *= factor));
        GuideCallbacks.Add(callback);
        Signals.Arm();
        ctx.ExecuteSyncCommand(new ModifyBrushHairGroupGuideParameterCommand(Guid.NewGuid().ToString("N"),path,callback,reference)
            .Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }
    internal static float GuideHeight(BrushHairGroupPath path)
        => new GetBrushHairGroupParameterSetQuery(path).Execute(Current().editingModel).GuideParameterSet.Height;
    internal static float GroupOffsetX(BrushHairGroupPath path)
        => new GetBrushHairGroupParameterSetQuery(path).Execute(Current().editingModel).HairParameterSet.PositionOffsetX;
    internal static float GroupWidth(BrushHairGroupPath path)
        => new GetBrushHairGroupParameterSetQuery(path).Execute(Current().editingModel).HairParameterSet.WidthScale;
    internal static long SetGroupWidth(BrushHairGroupPath path, float width)
    {
        var ctx = Current();
        if (!ctx.editingModel.GetIsPathValid(path)) throw new ArgumentException("STALE_HAIR_TARGET: group no longer exists");
        if (!float.IsFinite(width) || width < 0.05f || width > 4f) throw new ArgumentException("Width scale must be 0.05..4");
        if (ParameterCallbacks.Count >= 128) throw new ArgumentException("Restart before more group parameter commands");
        var callback = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<BrushHairGroupParameterSet>>(
            (Action<BrushHairGroupParameterSet>)(p => p.HairParameterSet.WidthScale = width));
        ParameterCallbacks.Add(callback);
        Signals.Arm();
        ctx.ExecuteSyncCommand(new ModifyBrushHairGroupParameterSetCommand(Guid.NewGuid().ToString("N"),path,callback)
            .Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }
    internal static Vector2[] ReadProfile(string id)
    {
        var curve = new GetHairStrandCurveQuery(Resolve(id).Path).Execute(Current().editingModel);
        if (curve?.RawControlPoints == null) throw new ArgumentException("HAIR_NOT_SUPPORTED: no width curve");
        if (curve.RawControlPoints.Length > MaxPoints) throw new ArgumentException("HAIR_LIMIT: curve too large");
        return curve.RawControlPoints.ToArray();
    }
    internal static long SetProfile(string id, Vector2[] points)
    {
        var strand = Resolve(id);
        if (strand.Path.TryCast<BrushHairStrandPath>() == null) throw new ArgumentException("HAIR_NOT_SUPPORTED: brush strand required");
        var ctx = Current();
        var curve = new GetHairStrandCurveQuery(strand.Path).Execute(ctx.editingModel).CloneDeep();
        curve.RawControlPoints = new Il2CppStructArray<Vector2>(points);
        Signals.Arm();
        ctx.ExecuteSyncCommand(new SetHairStrandCurveCommand(strand.Path, curve)
            .Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }
    internal static bool IsVisible(string id)
        => new GetHairStrandIsVisibleQuery(Resolve(id).Path).Execute(Current().editingModel);
    internal static long Smooth(string id)
    {
        var path = Resolve(id).Path.TryCast<BrushHairStrandPath>();
        if (path == null) throw new ArgumentException("HAIR_NOT_SUPPORTED: brush strand required");
        var ctx = Current();
        var paths = new Il2CppSystem.Collections.Generic.List<BrushHairStrandPath>();
        paths.Add(path);
        Signals.Arm();
        ctx.ExecuteSyncCommand(new SmoothBrushHairStrandsCommand(paths.Cast<Il2CppSystem.Collections.Generic.IEnumerable<BrushHairStrandPath>>())
            .Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }
    internal static long SetVisible(string id, bool visible)
    {
        var path = Resolve(id).Path;
        var ctx = Current();
        var paths = new Il2CppSystem.Collections.Generic.List<IHairStrandPath>(); paths.Add(path);
        Signals.Arm();
        ctx.ExecuteSyncCommand(new SetHairStrandIsVisibleCommand(paths.Cast<Il2CppSystem.Collections.Generic.IEnumerable<IHairStrandPath>>(),visible)
            .Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }

    internal static object List()
    {
        var ctx = Current();
        var transfers = ctx.editingModel.GetActiveLeafTransferables();
        var entries = new Il2CppReferenceArray<TransferableInfo>(transfers.Count);
        transfers.Values.CopyTo(entries, 0);
        var result = new List<object>();
        var found = new Dictionary<string, Strand>();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (!(entry.GroupType ?? "").Contains("Hair") && !(entry.Type ?? "").Contains("Hair")) continue;
            var groups = new GetHairGroupListQuery(entry.Id).Execute(ctx.editingModel);
            for (int j = 0; j < groups.Length; j++)
            {
                if (result.Count >= 1024) throw new ArgumentException("HAIR_LIMIT: too many groups");
                var group = groups[j];
                var brush = group.TryCast<BrushHairGroupPath>();
                var procedural = group.TryCast<ProceduralHairGroupPath>();
                if (brush == null && procedural == null) continue;
                string groupId = brush != null ? brush.HairGroupId : procedural.HairGroupId;
                var children = new GetHairGroupChildrenQuery(group).Execute(ctx.editingModel);
                var strands = new List<object>();
                for (int k = 0; k < children.Length; k++)
                {
                    if (found.Count >= 4096) throw new ArgumentException("HAIR_LIMIT: too many strands");
                    var path = children[k];
                    var bp = path.TryCast<BrushHairStrandPath>();
                    string id = _session + "/" + entry.Id + "/" + groupId + "/" + (bp?.HairGroupletId ?? "") + "/" + path.HairStrandId;
                    found.Add(id, new Strand { Id = id, GroupId = groupId, GroupType = entry.GroupType, Path = path });
                    strands.Add(new Dictionary<string, object> { ["strand_id"] = id, ["engine_strand_id"] = path.HairStrandId,
                        ["grouplet_id"] = bp?.HairGroupletId, ["move_supported"] = bp != null,
                        ["visible"] = new GetHairStrandIsVisibleQuery(path).Execute(ctx.editingModel) });
                }
                object nativeParameters = null;
                if (brush != null)
                {
                    var p = new GetBrushHairGroupParameterSetQuery(brush).Execute(ctx.editingModel);
                    nativeParameters = new Dictionary<string,object> { ["guide_height"] = p.GuideParameterSet.Height,
                        ["guide_offset"] = p.GuideParameterSet.Offset, ["offset_x"] = p.HairParameterSet.PositionOffsetX,
                        ["offset_y"] = p.HairParameterSet.PositionOffsetY, ["width_scale"] = p.HairParameterSet.WidthScale,
                        ["thickness_scale"] = p.HairParameterSet.ThicknessScale, ["division_count"] = p.HairParameterSet.DivisionCount,
                        ["cross_section"] = p.HairParameterSet.CrossSection.ToString() };
                }
                result.Add(new Dictionary<string, object> { ["group_id"] = groupId, ["transferable_id"] = entry.Id, ["native_parameters"] = nativeParameters,
                    ["group_type"] = entry.GroupType, ["kind"] = brush != null ? "brush" : "procedural",
                    ["visible"] = new GetHairGroupIsVisibleQuery(group).Execute(ctx.editingModel), ["strands"] = strands });
            }
        }
        Strands.Clear(); foreach (var pair in found) Strands.Add(pair.Key, pair.Value);
        return new Dictionary<string, object> { ["groups"] = result, ["revision"] = ctx._revision,
            ["workspace_root"] = TextureEditor.Workspace, ["max_delta"] = MaxDelta, ["coordinate_space"] = "native_control_point" };
    }

    internal static Strand Resolve(string id)
    {
        var ctx = Current();
        if (id == null || !Strands.TryGetValue(id, out var strand))
            throw new ArgumentException("STALE_HAIR_TARGET: use hair_list in this model/preset session");
        var bp = strand.Path.TryCast<BrushHairStrandPath>();
        var pp = strand.Path.TryCast<ProceduralHairStrandPath>();
        if (!(bp != null ? ctx.editingModel.GetIsPathValid(bp) : pp != null && ctx.editingModel.GetIsPathValid(pp)))
            throw new ArgumentException("STALE_HAIR_TARGET: strand no longer exists");
        return strand;
    }

    internal static ControlPoint[] Read(string id)
    {
        var strand = Resolve(id);
        var model = Current().editingModel;
        int n = new GetHairStrandControlPointCountQuery(strand.Path).Execute(model);
        if (n < 0 || n > MaxPoints) throw new ArgumentException("HAIR_LIMIT: unsupported point count");
        var result = new ControlPoint[n];
        for (int i = 0; i < n; i++) result[i] = new GetHairStrandControlPointQuery(strand.Path, i).Execute(model);
        return result;
    }

    private static object Vector(Vector3 v) => new[] { v.x, v.y, v.z };
    internal static object Points(ControlPoint[] points) => points.Select((p, i) => (object)new Dictionary<string, object> {
        ["index"] = i, ["position"] = Vector(p.Point), ["normal"] = Vector(p.Normal), ["uv"] = new[] { p.Uv.x, p.Uv.y } }).ToList();
    internal static string Hash(ControlPoint[] points)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            foreach (var p in points)
            { writer.Write(p.Point.x); writer.Write(p.Point.y); writer.Write(p.Point.z); writer.Write(p.Normal.x); writer.Write(p.Normal.y); writer.Write(p.Normal.z); writer.Write(p.Uv.x); writer.Write(p.Uv.y); }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    internal static object Get(string id)
    {
        var strand = Resolve(id);
        var points = Read(id);
        var matrix = Current().GetHairGuideOrigin(strand.Path.HairGroupPath);
        object nativeParameters = null;
        var brush = strand.Path.TryCast<BrushHairStrandPath>();
        if (brush != null)
        {
            var p = new GetBrushHairStrandParameterSetQuery(brush).Execute(Current().editingModel).HairParameterSet;
            var curve = new GetHairStrandCurveQuery(strand.Path).Execute(Current().editingModel);
            var profile = new List<object>();
            if (curve != null && curve.RawControlPoints != null)
            {
                if (curve.RawControlPoints.Length > MaxPoints) throw new ArgumentException("HAIR_LIMIT: curve too large");
                foreach (var v in curve.RawControlPoints) profile.Add(new[] {v.x, v.y});
            }
            nativeParameters = new Dictionary<string,object> {
                ["width_scale_override"] = p.WidthScale.HasValue ? (object)p.WidthScale.Value : null,
                ["width_curve_id"] = p.HairWidthCurveId, ["width_curve_points"] = profile };
        }
        var rows = new List<object>();
        for (int row = 0; row < 4; row++) rows.Add(new[] { matrix[row,0], matrix[row,1], matrix[row,2], matrix[row,3] });
        return new Dictionary<string, object> { ["strand_id"] = id, ["engine_strand_id"] = strand.Path.HairStrandId,
            ["group_id"] = strand.GroupId, ["group_type"] = strand.GroupType, ["revision"] = Revision,
            ["move_supported"] = strand.Path.TryCast<BrushHairStrandPath>() != null,
            ["native_parameters"] = nativeParameters,
            ["coordinate_space"] = "native_control_point", ["guide_origin_matrix"] = rows,
            ["points"] = Points(points), ["geometry_sha256"] = Hash(points), ["max_delta"] = MaxDelta, ["root_locked"] = true };
    }

    internal static void CheckRevision(string expected)
    {
        if (!long.TryParse(expected, out long revision) || revision != Revision)
            throw new ArgumentException("STALE_HAIR_REVISION: expected_revision must equal the current revision");
    }

    internal static Backup Prepare(string id, int index, Vector3 delta)
    {
        if (Resolve(id).Path.TryCast<BrushHairStrandPath>() == null)
            throw new ArgumentException("HAIR_NOT_SUPPORTED: only brush strand points can be moved");
        var before = Read(id);
        if (index <= 0 || index >= before.Length) throw new ArgumentException("INVALID_HAIR_POINT: root is locked; use index 1..point_count-1");
        if (!float.IsFinite(delta.x) || !float.IsFinite(delta.y) || !float.IsFinite(delta.z) || delta.magnitude > MaxDelta || delta.sqrMagnitude < 1e-12f)
            throw new ArgumentException("INVALID_HAIR_DELTA: finite nonzero offset with length <= 0.05 required");
        var after = (ControlPoint[])before.Clone();
        var p = after[index]; p.Point += delta; after[index] = p;
        if (!float.IsFinite(p.Point.x) || !float.IsFinite(p.Point.y) || !float.IsFinite(p.Point.z) || p.Point.magnitude > 10)
            throw new ArgumentException("INVALID_HAIR_POINT: position outside conservative native bounds");
        if (Hash(before) == Hash(after)) throw new ArgumentException("INVALID_HAIR_DELTA: offset is below point precision");
        return MakeBackup(id, index, before, after);
    }

    private static Backup MakeBackup(string id, int index, ControlPoint[] before, ControlPoint[] after)
    {
        if (Backups.Count >= 128) throw new ArgumentException("HAIR_LIMIT: 128 backups in this session; save and reopen before more edits");
        var b = new Backup { Id = Guid.NewGuid().ToString("N"), StrandId = id, Index = index,
            Before = before, After = after, BeforeHash = Hash(before), AfterHash = Hash(after) };
        // Use the same containment/reparse checks as texture backups, with a fixed JSON sibling.
        string checkedPath = TextureEditor.AllowedPath("hair-backups/" + b.Id + ".png");
        b.File = Path.ChangeExtension(checkedPath, ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(b.File));
        var s = Resolve(id);
        var data = new Dictionary<string, object> { ["backup_id"] = b.Id, ["strand_id"] = id, ["point_index"] = index,
            ["revision"] = Revision, ["transferable_id"] = s.Path.TransferableId, ["group_id"] = s.GroupId,
            ["engine_strand_id"] = s.Path.HairStrandId, ["coordinate_space"] = "native_control_point",
            ["before_hash"] = b.BeforeHash, ["after_hash"] = b.AfterHash, ["before"] = Points(before), ["intended_after"] = Points(after) };
        using (var stream = new FileStream(b.File, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream)) writer.Write(MiniJson.Serialize(data, true));
        Backups.Add(b.Id, b);
        return b;
    }

    internal static Backup PrepareRestore(string backupId)
    {
        Current();
        if (backupId == null || !Backups.TryGetValue(backupId, out var b)) throw new ArgumentException("STALE_HAIR_TARGET: backup is not in this session");
        if (Hash(Read(b.StrandId)) != b.AfterHash) throw new ArgumentException("STALE_HAIR_TARGET: strand changed since this backup; restore newer changes first");
        return MakeBackup(b.StrandId, b.Index, b.After, b.Before);
    }

    internal static long Apply(Backup b, bool restore = false)
    {
        var strand = Resolve(b.StrandId).Path.TryCast<BrushHairStrandPath>();
        if (strand == null) throw new ArgumentException("HAIR_NOT_SUPPORTED: brush strand required");
        if (Hash(Read(b.StrandId)) != (restore ? b.AfterHash : b.BeforeHash))
            throw new ArgumentException("STALE_HAIR_TARGET: geometry changed before command");
        var ctx = Current();
        var point = (restore ? b.Before : b.After)[b.Index];
        var command = new MoveBrushHairStrandControlPointCommand(strand, b.Index, point);
        Signals.Arm();
        ctx.ExecuteSyncCommand(command.Cast<ISyncCommand<Context.HistoryManagerContext, Model>>());
        // Complete the stock drag-style command before reporting a finished API edit.
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }
}
