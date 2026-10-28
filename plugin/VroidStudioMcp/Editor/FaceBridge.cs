using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using VRoidCore.Common.TransferableTypes;
using VRoidCore.Common.SpecificTypes;
using VRoidCore.Editing;
using VRoidCore.Editing.History.Command;

namespace VroidStudioMcp;

public static class FaceBridge
{
    internal const string BlobKey="com.kokokoshka.face-bridge.v1";
    internal static Transferable Carrier()
    {
        var context=TextureEditor.Current();
        var entries=context.editingModel.GetActiveLeafTransferables();
        var values=new Il2CppReferenceArray<TransferableInfo>(entries.Count);entries.Values.CopyTo(values,0);
        for(int i=0;i<values.Length;i++)
            if(values[i].GroupType=="TransferableGroupType.N00.Level1.FacePaint")
                return context.editingModel._transferables[values[i].Id];
        return null;
    }
    internal static byte[] Stored()
    {
        var carrier=Carrier();if(carrier==null)return null;
        var blobs=carrier.Collection.Blob.Blobs;
        if(!blobs.ContainsKey(BlobKey)) return null;
        var native=blobs[BlobKey];
        if(native.Length>32*1024*1024) throw new ArgumentException("FACE_PACKAGE_LIMIT: maximum 32 MiB");
        var bytes=new byte[native.Length];for(int i=0;i<bytes.Length;i++) bytes[i]=native[i];
        return bytes;
    }
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static object Status()
    {
        var bytes=Stored();
        return new Dictionary<string,object>{["stored"]=bytes!=null,["sha256"]=bytes==null?null:Hash(bytes),
            ["bytes"]=bytes?.Length??0,["revision"]=TextureEditor.Revision,["plugin_required"]=true,
            ["active"]=FaceBridgeRuntime.Active,["active_sha256"]=FaceBridgeRuntime.ActiveHash,["runtime_error"]=FaceBridgeRuntime.Error,["added_vertices"]=FaceBridgeRuntime.AddedVertices};
    }
    internal static byte[] ReadInput(string path,long expected)
    {
        if(expected!=TextureEditor.Revision) throw new ArgumentException("STALE_FACE_TARGET: expected_revision must match current revision");
        if(Path.GetExtension(path)!=".json") throw new ArgumentException("FACE_PACKAGE_PATH: JSON required");
        string checkedPath=TextureEditor.AllowedPath(path+".png");checkedPath=checkedPath.Substring(0,checkedPath.Length-4);
        var file=new FileInfo(checkedPath);
        if(!FileOps.IsRegularFile(checkedPath)||(File.GetAttributes(checkedPath)&FileAttributes.ReparsePoint)!=0||file.Length>32*1024*1024) throw new ArgumentException("FACE_PACKAGE_LIMIT: regular JSON file up to 32 MiB required");
        var bytes=File.ReadAllBytes(checkedPath);
        if(bytes.Length>32*1024*1024)throw new ArgumentException("FACE_PACKAGE_LIMIT: maximum 32 MiB");
        FaceBridgeRuntime.ValidateTarget(FaceBridgeRuntime.Parse(bytes));
        if(Carrier()==null)throw new InvalidOperationException("FACE_TARGET_MISSING: select a FacePaint preset first");
        return bytes;
    }
    internal static long Store(byte[] bytes,long expected)
    {
        if(expected!=TextureEditor.Revision)throw new ArgumentException("STALE_FACE_TARGET: expected_revision must match current revision");
        var ctx=TextureEditor.Current();var carrier=Carrier()??throw new InvalidOperationException("FACE_TARGET_MISSING: select a FacePaint preset first");var transfer=carrier.CloneSpecificDeep();
        transfer.Collection.Blob.Blobs[BlobKey]=new Il2CppStructArray<byte>(bytes);
        Signals.Arm();
        ctx.ExecuteSyncCommand(new ReplaceTransferableCommand(transfer).Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        ctx.FlushPendingHistory(false);
        return ctx._revision;
    }
    internal static long Remove(long expected)
    {
        if(expected!=TextureEditor.Revision)throw new ArgumentException("STALE_FACE_TARGET: expected_revision must match current revision");
        var ctx=TextureEditor.Current();var carrier=Carrier()??throw new InvalidOperationException("FACE_TARGET_MISSING");
        var transfer=carrier.CloneSpecificDeep();transfer.Collection.Blob.Blobs.Remove(BlobKey);
        Signals.Arm();ctx.ExecuteSyncCommand(new ReplaceTransferableCommand(transfer).Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());ctx.FlushPendingHistory(false);return ctx._revision;
    }
    internal static object Probe()
    {
        var ctx = TextureEditor.Current();
        var items = new List<object>();
        foreach (var r in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r.sharedMesh == null || (r.name != "Face" && r.name != "Head" && r.name!="MCPFaceBridge.Details")) continue;
            var mesh = r.sharedMesh;
            var baked = new Mesh();
            try
            {
                r.BakeMesh(baked);
                var vertices = new List<object>(); var world = new List<object>(); var uv = new List<object>();
                var weights = new List<object>(); var bones = new List<string>(); var shapes = new List<object>();
                foreach (var v in mesh.vertices) vertices.Add(new[] {v.x,v.y,v.z});
                foreach (var v in baked.vertices) { var p = r.transform.TransformPoint(v); world.Add(new[] {p.x,p.y,p.z}); }
                foreach (var v in mesh.uv) uv.Add(new[] {v.x,v.y});
                foreach (var w in mesh.boneWeights) weights.Add(new object[] {new[] {w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3},new[] {w.weight0,w.weight1,w.weight2,w.weight3}});
                foreach (var b in r.bones) bones.Add(b == null ? null : b.name);
                for (int i=0;i<mesh.blendShapeCount;i++) shapes.Add(new {name=mesh.GetBlendShapeName(i),weight=r.GetBlendShapeWeight(i),frames=mesh.GetBlendShapeFrameCount(i)});
                var materials = new List<string>(); foreach(var m in r.sharedMaterials) materials.Add(m?.name);
                items.Add(new {name=r.name,mesh=mesh.name,vertices,world_vertices=world,uv,weights,bones,shapes,materials});
            }
            finally { UnityEngine.Object.Destroy(baked); }
        }
        string path = Path.Combine(TextureEditor.Workspace,"face-bridge","probe-"+Guid.NewGuid().ToString("N")+".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,JsonSerializer.Serialize(new Dictionary<string,object> { ["renderers"]=items, ["model_path"]=FileOps.CurrentModelPath(),["revision"]=ctx._revision }));
        return new Dictionary<string,object>{["probe_path"]=path,["renderers"]=items.Count};
    }
}
