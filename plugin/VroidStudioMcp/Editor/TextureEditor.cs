using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using VRoidCore.Common.SpecificTypes;
using VRoidCore.Draw.TiledBitmap;
using VRoidCore.Editing;
using VRoidCore.Editing.Query;
using VRoidCore.Editing.History.Command;
using BeginEdit = VRoidCore.Editing.History.Command.AdvancedEdit.BeginAdvancedEditCommand;

namespace VroidStudioMcp;

// The editable layer content and the history command are the app's own PNG-editing path.
public static class TextureEditor
{
    internal sealed class Target
    {
        internal string Id, Section, GroupType;
        internal EditableImagePath Path;
    }
    internal sealed class Backup
    {
        internal string Id, TargetId, LayerId, Path, PixelHash;
    }
    private static readonly Dictionary<string, Target> Targets = new();
    private static readonly Dictionary<string, Backup> Backups = new();
    private static string _session = Guid.NewGuid().ToString("N");
    private static IntPtr _contextPointer;
    internal const int MaxBytes = 64 * 1024 * 1024;
    internal const long MaxPixels = 16 * 1024 * 1024;
    internal static string Workspace => Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath, ServerSettings.TextureWorkspace));

    internal static void Invalidate()
    {
        _session = Guid.NewGuid().ToString("N");
        Targets.Clear();
        Backups.Clear(); // Backup PNGs remain available for manual recovery.
    }

    internal static Context Current()
    {
        if (!VroidState._inEdit) throw new InvalidOperationException("NOT_IN_EDIT_MODE: open or create a model first");
        if (!VroidState._modelReady || VroidState._modelUpdating)
            throw new InvalidOperationException("MODEL_NOT_READY: wait for the current operation");
        var context = (Interop.Get(VroidState._engineModel, "Context") as Il2CppObjectBase)?.TryCast<Context>();
        if (context == null || !context.IsQueryExecutable || !context.IsCommandExecutable)
            throw new InvalidOperationException("MODEL_NOT_READY: editing context is unavailable or busy");
        if (context.Pointer != _contextPointer) { Invalidate(); _contextPointer = context.Pointer; }
        return context;
    }

    internal static long Revision => Current()._revision;

    internal static Il2CppSystem.Collections.Generic.List<BeginEdit.Input> PrepareGroup(string groupType)
    {
        var ctx = Current();
        var entries = ctx.editingModel.GetActiveLeafTransferables();
        var values = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VRoidCore.Editing.TransferableInfo>(entries.Count);
        entries.Values.CopyTo(values, 0);
        var inputs = new Il2CppSystem.Collections.Generic.List<BeginEdit.Input>();
        bool found = false;
        for (int i = 0; i < values.Length; i++)
            if (values[i].GroupType == groupType)
            {
                found = true;
                var elements = ctx.editingModel._transferableGroups[values[i].GroupId].EditableElements;
                var makers = new Il2CppSystem.Collections.Generic.List<BeginEdit.IMakeImageEditable>();
                for (int j = 0; j < elements.Count; j++)
                {
                    var element = elements.GetNthValue(j);
                    if (element.TransferableId != values[i].Id || element.Type != VRoidCore.Common.TransferableTypes.TransferableGroupEditableElementType.Image) continue;
                    var image = ctx.editingModel._transferables[values[i].Id].Collection.Common.images[element.ImageElement.ImageId];
                    if (image.Type == ImageType.EditableImage) continue;
                    var key = new VRoidCore.Common.TransferableTypes.Path.ImagePath(element.Key);
                    makers.Add(new BeginEdit.SimpleTextureKey(key.Cast<VRoidCore.Common.TransferableTypes.Path.IImagePath>()).Cast<BeginEdit.IMakeImageEditable>());
                }
                if (makers.Count > 0) inputs.Add(new BeginEdit.OverlayInput(values[i].Id, values[i].GroupId,
                    makers.Cast<Il2CppSystem.Collections.Generic.IEnumerable<BeginEdit.IMakeImageEditable>>()).Cast<BeginEdit.Input>());
            }
        if (!found) throw new ArgumentException("UNKNOWN_TEXTURE_TARGET: select a non-empty preset for this group first");
        return inputs;
    }

    internal static long ApplyPreparation(Il2CppSystem.Collections.Generic.List<BeginEdit.Input> inputs)
    {
        var ctx = Current();
        var command = new BeginEdit(inputs.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<BeginEdit.Input>>(), "Layer 1");
        Signals.Arm();
        ctx.ExecuteSyncCommand(command.Cast<ISyncCommand<Context.HistoryManagerContext, Model>>());
        return ctx._revision;
    }

    internal static Il2CppSystem.Collections.Generic.List<string> ClothingPreparation(string groupType, out string groupId)
    {
        var ctx = Current();
        var entries = ctx.editingModel.GetActiveLeafTransferables();
        var values = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VRoidCore.Editing.TransferableInfo>(entries.Count);
        entries.Values.CopyTo(values,0);
        var ids = new Il2CppSystem.Collections.Generic.List<string>();
        groupId = null;
        for(int i=0;i<values.Length;i++)
        {
            if(values[i].GroupType!=groupType)continue;
            if(groupId!=null&&groupId!=values[i].GroupId)throw new ArgumentException("PREPARATION_NOT_SUPPORTED: ambiguous clothing groups");
            groupId=values[i].GroupId;
            if(new GetEditableImagePathsForTransferableQuery(values[i].Id).Execute(ctx.editingModel).Length==0)ids.Add(values[i].Id);
        }
        if(groupId==null)throw new ArgumentException("UNKNOWN_TEXTURE_TARGET: select a non-empty preset for this group first");
        return ids;
    }

    internal static long ApplyClothingPreparation(Il2CppSystem.Collections.Generic.List<string> ids,string groupId)
    {
        var ctx=Current();
        var command=new VRoidCore.Editing.History.Command.AdvancedEdit.BeginClothingAdvancedEditCommand(
            ids.Cast<Il2CppSystem.Collections.Generic.IEnumerable<string>>(),groupId,"Layer 1");
        Signals.Arm();
        ctx.ExecuteSyncCommand(command.Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        return ctx._revision;
    }

    internal static object ListTargets(string section)
    {
        if (section != null && section != "Face" && section != "Body" && section != "Outfit" && section != "Hairstyle")
            throw new ArgumentException("UNKNOWN_TEXTURE_TARGET: section must be Face, Body, Outfit or Hairstyle");
        var context = Current();
        var model = context.editingModel;
        var groups = model.GetActiveLeafTransferables();
        // Copy dictionary values into an indexed list; never use a proxy IEnumerator.Current.
        var entries = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VRoidCore.Editing.TransferableInfo>(groups.Count);
        groups.Values.CopyTo(entries, 0);
        var result = new List<object>();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var paths = new GetEditableImagePathsForTransferableQuery(entry.Id).Execute(model);
            for (int j = 0; j < paths.Length; j++)
            {
                var path = paths[j];
                string type = entry.GroupType ?? entry.Type ?? "";
                string inferred = type.Contains("Hair") ? "Hairstyle" :
                    type.Contains("Body") ? "Body" :
                    new[] { "Tops", "Bottoms", "Onepiece", "Dress", "Shoes", "Socks", "Neck", "Arm", "Underwear" }.Any(type.Contains) ? "Outfit" : "Face";
                if (section != null && section != inferred) continue;
                string id = _session + "/" + path.TransferableId + "/" + path.ImageId;
                Targets[id] = new Target { Id = id, Section = inferred, GroupType = type, Path = path };
                var size = model.GetEditableImage(path).TiledBitmapProperty.bitmapSize;
                result.Add(new Dictionary<string, object> {
                    ["target_id"] = id, ["section"] = inferred, ["group_type"] = type,
                    ["image_id"] = path.ImageId, ["width"] = size.x, ["height"] = size.y,
                    ["revision"] = context._revision, ["uv_guide_path"] = null });
            }
        }
        return new Dictionary<string, object> { ["targets"] = result, ["workspace_root"] = Workspace, ["revision"] = context._revision };
    }

    internal static Target Resolve(string id)
    {
        var context = Current();
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("UNKNOWN_TEXTURE_TARGET: target_id is required");
        if (!id.StartsWith(_session + "/", StringComparison.Ordinal)) throw new ArgumentException("STALE_TEXTURE_TARGET: enumerate texture_targets again");
        if (!Targets.TryGetValue(id, out var target) || !context.editingModel.GetIsPathValid(target.Path))
            throw new ArgumentException("UNKNOWN_TEXTURE_TARGET: target no longer exists");
        return target;
    }

    internal static EditableImageRasterLayerPath Layer(string id, string layerId)
    {
        var target = Resolve(id);
        if (string.IsNullOrEmpty(layerId)) throw new ArgumentException("UNKNOWN_TEXTURE_TARGET: layer_id is required");
        var path = new EditableImageRasterLayerPath(target.Path, layerId);
        if (!Current().editingModel.GetIsPathValid(path)) throw new ArgumentException("UNKNOWN_TEXTURE_TARGET: layer does not exist");
        return path;
    }

    internal static object ListLayers(string id)
    {
        var target = Resolve(id);
        var model = Current().editingModel;
        var paths = new GetEditableImageRasterLayersQuery(target.Path).Execute(model);
        var result = new List<object>();
        for (int i = 0; i < paths.Length; i++)
        {
            var p = new GetEditableImageLayerTreeParameterSetQuery(paths[i]).Execute(model);
            var size = model.GetEditableImageRasterLayer(paths[i]).TiledBitmapProperty.bitmapSize;
            result.Add(new Dictionary<string, object> { ["layer_id"] = paths[i].NodeId, ["order"] = i,
                ["name"] = p.Name, ["visible"] = p.Visibility, ["opacity"] = p.Opacity,
                ["width"] = size.x, ["height"] = size.y, ["editable"] = true });
        }
        return new Dictionary<string, object> { ["target_id"] = id, ["layers"] = result, ["revision"] = Revision };
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static (byte[] Png, string PixelHash, int Width, int Height) Read(string id, string layerId)
    {
        var path = Layer(id, layerId);
        var content = new GetRasterLayerContentQuery(path).Execute(Current().editingModel);
        CheckSize(content.size.x, content.size.y);
        var texture = new Texture2D(content.size.x, content.size.y, TextureFormat.RGBA32, false);
        try
        {
            texture.LoadRawTextureData(content.rgbaBytes);
            texture.Apply();
            return (ImageConversion.EncodeToPNG(texture), Hash(content.rgbaBytes), content.size.x, content.size.y);
        }
        finally { UnityEngine.Object.Destroy(texture); }
    }

    internal static object Info(string id, string layerId, string output = null)
    {
        var data = Read(id, layerId);
        string file = null;
        if (output != null)
        {
            file = AllowedPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
            stream.Write(data.Png);
        }
        return new Dictionary<string, object> { ["target_id"] = id, ["layer_id"] = layerId,
            ["texture_path"] = file, ["width"] = data.Width, ["height"] = data.Height,
            ["format"] = "PNG", ["sha256"] = Hash(data.Png), ["pixel_sha256"] = data.PixelHash,
            ["revision"] = Revision, ["uv_guide_path"] = null };
    }

    internal static string AllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("PATH_NOT_ALLOWED: path is required");
        string root = Workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(full).ToLowerInvariant() != ".png" || full.Substring(Path.GetPathRoot(full).Length).Contains(':'))
            throw new ArgumentException("PATH_NOT_ALLOWED: use a PNG inside the configured texture workspace");
        // Lexical containment alone permits escaping through directory junctions/symlinks.
        for (string p = full; p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("PATH_NOT_ALLOWED: reparse points are not supported");
        return full;
    }

    private static void CheckSize(int w, int h)
    {
        if (w <= 0 || h <= 0 || (long)w * h > MaxPixels)
            throw new ArgumentException("INVALID_PNG_DIMENSIONS: maximum is 16 megapixels");
    }

    internal static Texture2D Decode(byte[] png)
    {
        if (png.Length < 33 || png.Length > MaxBytes || !png.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
            || System.Text.Encoding.ASCII.GetString(png, 12, 4) != "IHDR" || png[24] != 8 || png[25] != 6)
            throw new ArgumentException("INVALID_PNG: an 8-bit RGBA PNG is required (maximum 64 MiB)");
        int w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4));
        int h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4));
        CheckSize(w,h);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, png, false) || texture.width != w || texture.height != h)
                throw new ArgumentException("INVALID_PNG: PNG decoding failed");
            return texture;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }

    internal static byte[] Input(string path)
    {
        var full = AllowedPath(path);
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new ArgumentException("INVALID_PNG: file exceeds 64 MiB");
        var data = new byte[checked((int)stream.Length)];
        stream.ReadExactlyCompat(data);
        return data;
    }

    internal static string Validate(string id, string layerId, byte[] png, bool preserveAlpha = true)
    {
        var original = new GetRasterLayerContentQuery(Layer(id, layerId)).Execute(Current().editingModel);
        var texture = Decode(png);
        try
        {
            if (texture.width != original.size.x || texture.height != original.size.y)
                throw new ArgumentException($"INVALID_PNG_DIMENSIONS: expected {original.size.x}x{original.size.y}");
            var pixels = texture.GetPixels32();
            var raw = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (preserveAlpha && p.a != original.rgbaBytes[i * 4 + 3])
                    throw new ArgumentException("INVALID_PNG: alpha must match the source layer");
                raw[i*4] = p.r; raw[i*4+1] = p.g; raw[i*4+2] = p.b; raw[i*4+3] = p.a;
            }
            return Hash(raw);
        }
        finally { UnityEngine.Object.Destroy(texture); }
    }

    internal static Backup CreateBackup(string id, string layerId)
    {
        var data = Read(id, layerId);
        var backup = new Backup { Id = Guid.NewGuid().ToString("N"), TargetId = id, LayerId = layerId, PixelHash = data.PixelHash };
        backup.Path = AllowedPath("backups/" + backup.Id + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(backup.Path));
        using (var stream = new FileStream(backup.Path, FileMode.CreateNew, FileAccess.Write)) stream.Write(data.Png);
        Backups.Add(backup.Id, backup);
        return backup;
    }

    internal static Backup FindBackup(string id)
    {
        Current();
        if (id == null || !Backups.TryGetValue(id, out var backup)) throw new ArgumentException("STALE_TEXTURE_TARGET: backup not in this session");
        Layer(backup.TargetId, backup.LayerId);
        return backup;
    }

    internal static long Apply(string id, string layerId, byte[] png)
    {
        var path = Layer(id, layerId);
        var context = Current();
        var texture = Decode(png);
        try
        {
            var command = new LoadImageToEditableImageRasterLayerCommand(path, new BitmapSize(texture.width, texture.height), texture.GetPixels());
            Signals.Arm();
            context.ExecuteSyncCommand(command.Cast<ISyncCommand<Context.HistoryManagerContext, VRoidCore.Common.SpecificTypes.Model>>());
            return context._revision;
        }
        finally { UnityEngine.Object.Destroy(texture); }
    }

    private static void ReadExactlyCompat(this Stream stream, byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length) { int n = stream.Read(bytes, offset, bytes.Length-offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
    }
}
