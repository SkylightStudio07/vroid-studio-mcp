using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace VroidStudioMcp;

public static class FaceBridgeRuntime
{
    public sealed class Package
    {
        public string schema {get;set;}
        public int vertex_count {get;set;}
        public string topology_sha256 {get;set;}
        public float[][] delta_open {get;set;}
        public float[][] delta_half {get;set;}
        public float[][] delta_closed {get;set;}
        public float[][] reference_neutral_offset {get;set;}
        public float[][] reference_rest_world {get;set;}
        public bool[] closure_mask {get;set;}
        public string skin_png {get;set;}
        public Addition[] additions {get;set;}
    }
    public sealed class Addition
    {
        public string name {get;set;}
        public string bone_name {get;set;}
        public float[][] base_world {get;set;}
        public float[][] open_world {get;set;}
        public float[][] half_world {get;set;}
        public float[][] closed_world {get;set;}
        public float[][] uv {get;set;}
        public int[] triangles {get;set;}
        public int[] bind_vertices {get;set;}
        public string rgba_png {get;set;}
    }
    static SkinnedMeshRenderer Face,Extra;
    static Mesh Original,Applied;
    static Il2CppReferenceArray<Material> OriginalMaterials;
    static readonly List<UnityEngine.Object> Owned=new();
    static int SeenVersion=-1;
    static string LastError,AppliedHash;
    internal static bool Active=>Applied!=null&&Face!=null&&Face.sharedMesh==Applied;
    internal static string Error=>LastError;
    internal static string ActiveHash=>AppliedHash;
    internal static int AddedVertices=>Extra?.sharedMesh?.vertexCount??0;
    static Vector3 V(float[] p)=>new(p[0],p[1],p[2]);
    internal static bool BindingSupported(string name)=>name==null||name=="J_Bip_C_UpperChest"||name=="J_Bip_C_Neck"
        ||name=="J_Bip_L_LowerArm"||name=="J_Bip_R_LowerArm";

    internal static Package Parse(byte[] bytes)
    {
        var p=JsonSerializer.Deserialize<Package>(bytes,new JsonSerializerOptions{MaxDepth=32});
        if(p==null||p.schema!="vroid-face-bridge/1"||p.vertex_count<1||p.vertex_count>20000||p.additions==null||p.additions.Length>64)
            throw new ArgumentException("FACE_PACKAGE_SCHEMA: unsupported or incomplete face package");
        void Vectors(float[][] values,int count,int dimension,float bound)
        {
            if(values==null||values.Length!=count) throw new ArgumentException("FACE_PACKAGE_GEOMETRY: array length mismatch");
            foreach(var v in values)
                if(v==null||v.Length!=dimension||v.Any(x=>!float.IsFinite(x)||Math.Abs(x)>bound))
                    throw new ArgumentException("FACE_PACKAGE_GEOMETRY: invalid finite vector");
        }
        Vectors(p.delta_open,p.vertex_count,3,.03f);Vectors(p.delta_half,p.vertex_count,3,.03f);Vectors(p.delta_closed,p.vertex_count,3,.03f);
        Vectors(p.reference_neutral_offset,p.vertex_count,3,.1f);
        Vectors(p.reference_rest_world,p.vertex_count,3,3);
        if(p.closure_mask==null||p.closure_mask.Length!=p.vertex_count)throw new ArgumentException("FACE_PACKAGE_GEOMETRY: closure mask mismatch");
        int total=0;
        foreach(var a in p.additions)
        {
            if(!BindingSupported(a.bone_name))
                throw new ArgumentException("FACE_PACKAGE_GEOMETRY: unsupported additional binding bone");
            int n=a.open_world?.Length??0;total+=n;
            if(n<3||total>60000||a.triangles==null||a.triangles.Length%3!=0||a.triangles.Length>360000
                ||a.triangles.Any(i=>i<0||i>=n)||a.bind_vertices==null||a.bind_vertices.Length!=n||a.bind_vertices.Any(i=>i<0||i>=p.vertex_count))
                throw new ArgumentException("FACE_PACKAGE_GEOMETRY: invalid added mesh");
            Vectors(a.base_world,n,3,3);Vectors(a.open_world,n,3,3);Vectors(a.half_world,n,3,3);Vectors(a.closed_world,n,3,3);Vectors(a.uv,n,2,1.01f);
            CheckPng(a.rgba_png);
        }
        CheckPng(p.skin_png);
        return p;
    }
    static void CheckPng(string value)
    {
        if(value==null||value.Length>8*1024*1024) throw new ArgumentException("FACE_PACKAGE_TEXTURE: bounded embedded RGBA PNG required");
        var bytes=Convert.FromBase64String(value);
        var tex=TextureEditor.Decode(bytes);UnityEngine.Object.Destroy(tex);
    }
    internal static SkinnedMeshRenderer FindFace()
        =>UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="Face"&&r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMesh!=null);
    internal static string Topology(Mesh mesh)
    {
        using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
        {
            w.Write(mesh.vertexCount);foreach(var uv in mesh.uv){w.Write(uv.x);w.Write(uv.y);}
            w.Write(mesh.subMeshCount);
            for(int s=0;s<mesh.subMeshCount;s++){var t=mesh.GetTriangles(s);w.Write(t.Length);foreach(var i in t)w.Write(i);}
        }
        return FaceBridge.Hash(stream.ToArray());
    }
    internal static void ValidateTarget(Package p)
    {
        var face=FindFace();if(face==null)throw new InvalidOperationException("FACE_NOT_READY: native Face renderer unavailable");
        var mesh=Active&&face==Face?Original:face.sharedMesh;
        if(mesh.vertexCount!=p.vertex_count||Topology(mesh)!=p.topology_sha256)
            throw new ArgumentException("FACE_TOPOLOGY_MISMATCH: package belongs to a different source face mesh");
        if(!face.bones.Any(b=>b!=null&&b.name=="J_Bip_C_Head"))throw new ArgumentException("FACE_RIG_MISMATCH: head bone missing");
        var transforms=face.bones;var poses=mesh.bindposes;var weights=mesh.boneWeights;var vertices=mesh.vertices;
        var matrices=new Matrix4x4[transforms.Length];for(int i=0;i<matrices.Length;i++)matrices[i]=transforms[i].localToWorldMatrix*poses[i];
        for(int i=0;i<vertices.Length;i++)
            if((SkinMatrix(weights[i],matrices).MultiplyPoint3x4(vertices[i])-V(p.reference_rest_world[i])).sqrMagnitude>.00000001f)
                throw new ArgumentException("FACE_SOURCE_POSE_MISMATCH: restore source face/body parameters or export a new package");
        foreach(var name in p.additions.Select(a=>a.bone_name).Where(n=>n!=null).Distinct())
            if(!face.transform.root.GetComponentsInChildren<Transform>(true).Any(t=>t.name==name))
                throw new ArgumentException("FACE_RIG_MISMATCH: additional binding bone missing: "+name);
    }
    internal static void Reset()
    {
        if(Face!=null&&Applied!=null&&Face.sharedMesh==Applied)
        {
            var weights=new float[Applied.blendShapeCount];for(int i=0;i<weights.Length;i++)weights[i]=Face.GetBlendShapeWeight(i);
            Face.sharedMesh=Original;if(OriginalMaterials!=null)Face.sharedMaterials=OriginalMaterials;
            for(int i=0;i<weights.Length;i++)Face.SetBlendShapeWeight(i,weights[i]);
        }
        foreach(var o in Owned)if(o!=null)UnityEngine.Object.Destroy(o);
        Owned.Clear();Face=null;Extra=null;Original=null;Applied=null;OriginalMaterials=null;AppliedHash=null;
    }
    internal static void Invalidate(){Reset();SeenVersion=-1;LastError=null;}
    static Matrix4x4 SkinMatrix(BoneWeight weights,Matrix4x4[] bones)
    {
        var ids=new[]{weights.boneIndex0,weights.boneIndex1,weights.boneIndex2,weights.boneIndex3};
        var ws=new[]{weights.weight0,weights.weight1,weights.weight2,weights.weight3};var result=Matrix4x4.zero;
        for(int j=0;j<4;j++)
        {
            if(ws[j]==0)continue;
            var m=bones[ids[j]];
            for(int k=0;k<16;k++)result[k]+=m[k]*ws[j];
        }
        return result;
    }
    static Vector3 Interpolate(float[][] open,float[][] half,float[][] closed,int index,float closure)
        =>closure<=26?V(open[index]):closure<60?Vector3.Lerp(V(open[index]),V(half[index]),(closure-26)/34):Vector3.Lerp(V(half[index]),V(closed[index]),Mathf.Clamp01((closure-60)/40));
    static float Closure(string side)
    {
        try{return Convert.ToSingle(Interop.Get(Expressions.Parameter("BlendShape.Synthesized.N00_Face.N00.All.Neutral",4,"Fcl_EYE_Close_"+side),"Value"));}
        catch{return 26;}
    }
    static Texture2D Texture(string png)
    {
        var t=TextureEditor.Decode(Convert.FromBase64String(png));t.wrapMode=TextureWrapMode.Clamp;Owned.Add(t);return t;
    }
    static Material Material(Material source,string png,bool transparent)
    {
        var m=new Material(source);Owned.Add(m);var t=Texture(png);m.mainTexture=t;
        m.SetColor("_Color",new Color(0,0,0,1));m.SetColor("_ShadeColor",new Color(0,0,0,1));
        m.SetColor("_EmissionColor",Color.white);m.SetTexture("_EmissionMap",t);m.SetFloat("_OutlineWidth",0);
        m.EnableKeyword("_EMISSION");
        m.SetFloat("_CullMode",0);m.SetFloat("_ReceiveShadowRate",0);m.SetFloat("_BlendMode",transparent?2:0);
        m.SetFloat("_SrcBlend",transparent?5:1);m.SetFloat("_DstBlend",transparent?10:0);m.SetFloat("_ZWrite",transparent?0:1);
        m.DisableKeyword("_ALPHATEST_ON");m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        if(transparent)m.EnableKeyword("_ALPHABLEND_ON");else m.DisableKeyword("_ALPHABLEND_ON");
        m.renderQueue=transparent?3000:2000;return m;
    }
    internal static void Refresh()
    {
        Reset();SeenVersion=Signals.ModelVersion;LastError=null;
        var bytes=FaceBridge.Stored();if(bytes==null)return;
        var package=Parse(bytes);ValidateTarget(package);
        Face=FindFace();Original=Face.sharedMesh;OriginalMaterials=Face.sharedMaterials;
        var currentWeights=new float[Original.blendShapeCount];for(int i=0;i<currentWeights.Length;i++)currentWeights[i]=Face.GetBlendShapeWeight(i);
        var boneTransforms=Face.bones;var bindposes=Original.bindposes;var originalVertices=Original.vertices;var boneWeights=Original.boneWeights;
        var boneMatrices=new Matrix4x4[boneTransforms.Length];for(int i=0;i<boneMatrices.Length;i++)boneMatrices[i]=boneTransforms[i].localToWorldMatrix*bindposes[i];
        var inverse=new Matrix4x4[Original.vertexCount];var skinning=new Matrix4x4[Original.vertexCount];
        for(int i=0;i<inverse.Length;i++){skinning[i]=SkinMatrix(boneWeights[i],boneMatrices);inverse[i]=skinning[i].inverse;}
        var closeL=Closure("L");var closeR=Closure("R");
        var localDelta=new Vector3[package.vertex_count];var neutralOffset=new Vector3[package.vertex_count];
        for(int i=0;i<localDelta.Length;i++)localDelta[i]=inverse[i].MultiplyVector(V(package.delta_open[i]));
        for(int i=0;i<neutralOffset.Length;i++)neutralOffset[i]=inverse[i].MultiplyVector(V(package.reference_neutral_offset[i]));
        Applied=UnityEngine.Object.Instantiate(Original);Applied.name="MCPFaceBridge.Face";Owned.Add(Applied);
        var vertices=Applied.vertices;for(int i=0;i<vertices.Length;i++)vertices[i]+=localDelta[i]+neutralOffset[i];Applied.vertices=vertices;
        Applied.ClearBlendShapes();
        for(int s=0;s<Original.blendShapeCount;s++)
        {
            string name=Original.GetBlendShapeName(s);
            for(int f=0;f<Original.GetBlendShapeFrameCount(s);f++)
            {
                var dv=new Il2CppStructArray<Vector3>(package.vertex_count);var dn=new Il2CppStructArray<Vector3>(package.vertex_count);var dt=new Il2CppStructArray<Vector3>(package.vertex_count);
                Original.GetBlendShapeFrameVertices(s,f,dv,dn,dt);
                for(int i=0;i<dv.Length;i++)
                {
                    bool positive=originalVertices[i].x>=0;
                    if(name=="Fcl_ALL_Neutral")dv[i]+=inverse[i].MultiplyVector(Interpolate(package.delta_open,package.delta_half,package.delta_closed,i,positive?closeL:closeR))-localDelta[i]-neutralOffset[i];
                    else if(package.closure_mask[i]&&(name=="Fcl_EYE_Close"||(name=="Fcl_EYE_Close_L"&&positive)||(name=="Fcl_EYE_Close_R"&&!positive)))
                        dv[i]+=inverse[i].MultiplyVector(V(package.delta_closed[i]))-localDelta[i]-neutralOffset[i];
                    else if(name.StartsWith("Fcl_ALL_",StringComparison.Ordinal))dv[i]-=neutralOffset[i];
                }
                Applied.AddBlendShapeFrame(name,Original.GetBlendShapeFrameWeight(s,f),dv,dn,dt);
            }
        }
        Applied.RecalculateNormals();Applied.RecalculateBounds();Face.sharedMesh=Applied;
        for(int i=0;i<currentWeights.Length;i++)Face.SetBlendShapeWeight(i,currentWeights[i]);
        var materials=Face.sharedMaterials;materials[3]=Material(materials[3],package.skin_png,false);Face.sharedMaterials=materials;

        int head=-1;for(int i=0;i<Face.bones.Length;i++)if(Face.bones[i].name=="J_Bip_C_Head")head=i;
        var headInverse=(Face.bones[head].localToWorldMatrix*Original.bindposes[head]).inverse;
        var extraBones=Face.bones.ToList();var extraBindposes=Original.bindposes.ToList();
        var anchoredBones=new Dictionary<string,int>();
        foreach(var name in package.additions.Select(a=>a.bone_name).Where(n=>n!=null).Distinct())
        {
            var bone=Face.transform.root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
            anchoredBones[name]=extraBones.Count;extraBones.Add(bone);
            extraBindposes.Add(bone.worldToLocalMatrix*Face.transform.localToWorldMatrix);
        }
        var addVertices=new List<Vector3>();var addUV=new List<Vector2>();var addWeights=new List<BoneWeight>();var addMaterials=new List<Material>();
        var poses=new[]{new List<Vector3>(),new List<Vector3>(),new List<Vector3>()};var binding=new List<int>();var triangles=new List<int[]>();var followFace=new List<bool>();
        foreach(var a in package.additions)
        {
            int offset=addVertices.Count;
            int bone=a.bone_name==null?head:anchoredBones[a.bone_name];
            var worldInverse=a.bone_name==null?headInverse:(extraBones[bone].localToWorldMatrix*extraBindposes[bone]).inverse;
            for(int i=0;i<a.base_world.Length;i++)
            {
                addVertices.Add(worldInverse.MultiplyPoint3x4(V(a.base_world[i])));addUV.Add(new Vector2(a.uv[i][0],a.uv[i][1]));
                addWeights.Add(new BoneWeight{boneIndex0=bone,weight0=1});binding.Add(a.bind_vertices[i]);followFace.Add(a.bone_name==null);
                poses[0].Add(worldInverse.MultiplyPoint3x4(V(a.open_world[i])));poses[1].Add(worldInverse.MultiplyPoint3x4(V(a.half_world[i])));poses[2].Add(worldInverse.MultiplyPoint3x4(V(a.closed_world[i])));
            }
            triangles.Add(a.triangles.Select(i=>i+offset).ToArray());addMaterials.Add(Material(OriginalMaterials[7],a.rgba_png,true));
        }
        var extraMesh=new Mesh{name="MCPFaceBridge.Details"};Owned.Add(extraMesh);extraMesh.vertices=addVertices.ToArray();extraMesh.uv=addUV.ToArray();
        extraMesh.boneWeights=addWeights.ToArray();extraMesh.bindposes=extraBindposes.ToArray();extraMesh.subMeshCount=triangles.Count;
        for(int s=0;s<triangles.Count;s++)extraMesh.SetTriangles(triangles[s],s);
        for(int s=0;s<Original.blendShapeCount;s++)
        {
            string name=Original.GetBlendShapeName(s);var delta=new Vector3[addVertices.Count];
            var native=new Il2CppStructArray<Vector3>(package.vertex_count);var dn=new Il2CppStructArray<Vector3>(package.vertex_count);var dt=new Il2CppStructArray<Vector3>(package.vertex_count);
            Original.GetBlendShapeFrameVertices(s,0,native,dn,dt);
            for(int i=0;i<delta.Length;i++)
            {
                if(!followFace[i])continue;
                bool positive=addVertices[i].x>=0;
                if(name=="Fcl_ALL_Neutral")
                {
                    float closure=positive?closeL:closeR;
                    var desired=closure<=26?poses[0][i]:closure<60?Vector3.Lerp(poses[0][i],poses[1][i],(closure-26)/34):Vector3.Lerp(poses[1][i],poses[2][i],Mathf.Clamp01((closure-60)/40));
                    delta[i]=desired-addVertices[i];
                }
                else if(name=="Fcl_EYE_Close"||(name=="Fcl_EYE_Close_L"&&positive)||(name=="Fcl_EYE_Close_R"&&!positive))delta[i]=poses[2][i]-addVertices[i];
                else
                {
                    var world=skinning[binding[i]].MultiplyVector(native[binding[i]]);
                    if(name.StartsWith("Fcl_ALL_",StringComparison.Ordinal))world-=V(package.reference_neutral_offset[binding[i]]);
                    delta[i]=headInverse.MultiplyVector(world);
                }
            }
            extraMesh.AddBlendShapeFrame(name,100,delta,new Vector3[delta.Length],new Vector3[delta.Length]);
        }
        extraMesh.RecalculateNormals();extraMesh.RecalculateBounds();
        var go=new GameObject("MCPFaceBridge.Details");Owned.Add(go);go.layer=Face.gameObject.layer;go.transform.SetParent(Face.transform,false);
        Extra=go.AddComponent<SkinnedMeshRenderer>();Extra.bones=extraBones.ToArray();Extra.rootBone=Face.rootBone;Extra.sharedMesh=extraMesh;Extra.sharedMaterials=addMaterials.ToArray();Extra.updateWhenOffscreen=true;
        AppliedHash=FaceBridge.Hash(bytes);CopyWeights();
    }
    static void CopyWeights()
    {
        if(!Active||Extra==null)return;
        for(int i=0;i<Face.sharedMesh.blendShapeCount;i++)Extra.SetBlendShapeWeight(i,Face.GetBlendShapeWeight(i));
    }
    internal static void Tick()
    {
        if(!VroidState._inEdit){if(Face!=null)Invalidate();return;}
        if(!VroidState._modelReady||VroidState._modelUpdating)return;
        try
        {
            if(SeenVersion!=Signals.ModelVersion)Refresh();
            CopyWeights();
        }
        catch(Exception e){Reset();SeenVersion=Signals.ModelVersion;LastError=e.Message;PluginLog.LogError("face bridge: "+e.Message);}
    }
}
