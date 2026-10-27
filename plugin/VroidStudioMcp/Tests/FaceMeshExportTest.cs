using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VroidStudioMcp;

public sealed class FaceMeshExportTest : VroidTest
{
    const string Neutral = "BlendShape.Synthesized.N00_Face.N00.All.Neutral";
    static CommandDispatcher.Context C(params string[] args) => new() { Args = args };
    static object V(Vector3 v) => new[] { v.x, v.y, v.z };

    protected override IEnumerator Run()
    {
        var open = C(CharacterPath);
        yield return SessionHandlers.Open(open);
        Check(open);
        var list = C();
        yield return ExpressionHandlers.List(list);
        Check(list);
        var initial = new Dictionary<string, float>();
        foreach (string key in new[] { "Fcl_EYE_Close_L", "Fcl_EYE_Close_R" })
            initial[key] = Convert.ToSingle(Interop.Get(Expressions.Parameter(Neutral, 4, key), "Value"));
        string folder = Path.Combine(BepInEx.Paths.GameRootPath, "mesh-study");
        Directory.CreateDirectory(folder);
        foreach (var state in new[] { ("neutral", -1), ("half", 60), ("closed", 100) })
        {
            foreach (string key in new[] { "Fcl_EYE_Close_L", "Fcl_EYE_Close_R" })
            {
                float value = state.Item2 < 0 ? initial[key] : state.Item2;
                var set = C(Neutral, "4", key, value.ToString(System.Globalization.CultureInfo.InvariantCulture), TextureEditor.Revision.ToString());
                yield return ExpressionHandlers.Set(set);
                Check(set);
            }
            var preview = C(Neutral);
            yield return ExpressionHandlers.Preview(preview);
            Check(preview);
            var camera = C("frame", "face", "front");
            yield return CameraHandlers.Camera(camera);
            Check(camera);
            yield return StableShot();
            if (!Screenshot.LastHasContent) throw new InvalidOperationException("Empty mesh-study preview");
            File.WriteAllBytes(Path.Combine(folder, state.Item1 + ".png"), Screenshot.LastCanonicalPng);
            Export(folder, state.Item1, state.Item1 == "neutral");
        }
        foreach (string key in new[] { "Fcl_EYE_Close_L", "Fcl_EYE_Close_R" })
        {
            var set = C(Neutral, "4", key, initial[key].ToString(System.Globalization.CultureInfo.InvariantCulture), TextureEditor.Revision.ToString());
            yield return ExpressionHandlers.Set(set);
            Check(set);
        }
        WriteResult("face_mesh_export_result.json", new Dictionary<string, object> { ["ok"] = true, ["folder"] = folder });
        FileOps.QuitClean();
    }

    static void Check(CommandDispatcher.Context ctx)
    {
        if (ctx.Error != null) throw new InvalidOperationException(ctx.Error);
    }

    static void Export(string folder, string state, bool textures)
    {
        var result = new List<object>();
        var renderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
        int ordinal = 0;
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null) continue;
            var mesh = new Mesh();
            try
            {
                renderer.BakeMesh(mesh);
                var vertices = new List<object>();
                foreach (var vertex in mesh.vertices) vertices.Add(V(renderer.transform.TransformPoint(vertex)));
                var uv = new List<object>();
                foreach (var p in mesh.uv) uv.Add(new[] { p.x, p.y });
                var submeshes = new List<object>();
                var materials = renderer.sharedMaterials;
                for (int n = 0; n < mesh.subMeshCount; n++)
                {
                    var triangles = new List<int>();
                    foreach (int index in mesh.GetTriangles(n)) triangles.Add(index);
                    var material = n < materials.Length ? materials[n] : null;
                    string filename = "material-" + ordinal + "-" + n + ".png";
                    var properties = new List<string>();
                    if (material != null)
                    {
                        foreach (string name in material.GetTexturePropertyNames()) properties.Add(name);
                        if (textures && material.mainTexture != null) WriteTexture(material.mainTexture, Path.Combine(folder, filename));
                    }
                    submeshes.Add(new Dictionary<string, object> { ["triangles"] = triangles, ["material"] = material?.name,
                        ["shader"] = material?.shader?.name, ["texture"] = filename, ["texture_properties"] = properties });
                }
                result.Add(new Dictionary<string, object> { ["id"] = ordinal, ["name"] = renderer.name,
                    ["mesh_name"] = renderer.sharedMesh.name, ["vertices"] = vertices, ["uv"] = uv, ["submeshes"] = submeshes });
                ordinal++;
            }
            finally { UnityEngine.Object.Destroy(mesh); }
        }
        File.WriteAllText(Path.Combine(folder, state + ".json"), MiniJson.Serialize(new Dictionary<string, object> { ["renderers"] = result }, true));
    }

    static void WriteTexture(Texture source, string path)
    {
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D readable = null;
        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(readable));
        }
        finally
        {
            RenderTexture.active = previous;
            if (readable != null) UnityEngine.Object.Destroy(readable);
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
