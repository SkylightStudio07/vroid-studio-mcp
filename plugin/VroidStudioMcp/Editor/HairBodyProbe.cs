using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace VroidStudioMcp;

public static class HairBodyProbe
{
    private static float[][] Matrix(Matrix4x4 matrix) => Enumerable.Range(0,4)
        .Select(i => new[] {matrix[i,0],matrix[i,1],matrix[i,2],matrix[i,3]}).ToArray();
    private static float[] V(Vector3 value) => new[] {value.x,value.y,value.z};
    private static string Hierarchy(Transform transform)
    {
        var parts = new List<string>();
        while (transform != null) { parts.Add(transform.name); transform = transform.parent; }
        parts.Reverse(); return string.Join("/",parts);
    }
    internal static object Probe()
    {
        var context = TextureEditor.Current();
        var face = FaceBridgeRuntime.FindFace() ?? throw new InvalidOperationException("Active avatar face is unavailable");
        var root = face.transform.root;
        var items = new List<object>();
        int totalVertices = 0, totalIndices = 0;
        void Add(Renderer renderer, Mesh mesh, string kind)
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            totalVertices += mesh.vertexCount;
            if (totalVertices > 350000) throw new ArgumentException("HAIR_PROBE_LIMIT: at most 350000 vertices");
            var positions = mesh.vertices.Select(v => V(renderer.transform.TransformPoint(v))).ToArray();
            var parts = new List<object>();
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var indices = mesh.GetTriangles(i);
                totalIndices += indices.Length;
                if (totalIndices > 1500000) throw new ArgumentException("HAIR_PROBE_LIMIT: at most 1500000 triangle indices");
                var material = i < materials.Length ? materials[i] : null;
                parts.Add(new { triangles=indices.ToArray(), material=material?.name, shader=material?.shader?.name });
            }
            items.Add(new {name=renderer.name,kind,hierarchy=Hierarchy(renderer.transform),mesh=mesh.name,
                world_vertices=positions,uv=mesh.uv.Select(v => new[] {v.x,v.y}).ToArray(),
                submeshes=parts,bounds_center=V(renderer.bounds.center),bounds_size=V(renderer.bounds.size)});
        }
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.transform.IsChildOf(root) || renderer.sharedMesh == null) continue;
            var baked = new Mesh();
            try { renderer.BakeMesh(baked); Add(renderer,baked,"skinned"); }
            finally { UnityEngine.Object.Destroy(baked); }
        }
        foreach (var filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.transform.IsChildOf(root)) continue;
            Add(renderer,filter.sharedMesh,"static");
        }
        var head = face.bones.FirstOrDefault(b => b != null && b.name == "J_Bip_C_Head");
        string path = Path.Combine(TextureEditor.Workspace,"hair-probes","body-"+Guid.NewGuid().ToString("N")+".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var result = new {revision=context._revision,model_path=FileOps.CurrentModelPath(),root=Hierarchy(root),
            head_local_to_world=head == null ? null : Matrix(head.localToWorldMatrix),
            face_local_to_world=Matrix(face.transform.localToWorldMatrix),
            rig_bones=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("J_Bip_",StringComparison.Ordinal))
                .Select(t=>new {name=t.name,local_to_world=Matrix(t.localToWorldMatrix)}).ToArray(),renderers=items};
        File.WriteAllText(path,JsonSerializer.Serialize(result));
        return new Dictionary<string,object> { ["probe_path"]=path,["revision"]=context._revision,
            ["renderers"]=items.Count,["vertices"]=totalVertices,["read_only"]=true };
    }
}
