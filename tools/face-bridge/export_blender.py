"""Run with Blender --background --python export_blender.py -- [arguments]."""
import argparse,hashlib,json,struct,sys
from pathlib import Path
import bpy
import numpy as np

parser=argparse.ArgumentParser()
parser.add_argument('--blend',required=True);parser.add_argument('--source',required=True)
parser.add_argument('--native-probe',required=True);parser.add_argument('--output',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
source=Path(args.source)
captures={state:next(r for r in json.loads((source/(state+'.json')).read_text())['renderers'] if r['name']=='Face') for state in ['neutral','half','closed']}
native=next(r for r in json.loads(Path(args.native_probe).read_text())['renderers'] if r['name']=='Face')
if native['mesh'].startswith('MCPFaceBridge.'):raise ValueError('Capture the native probe from the unmodified source model, without an active face bridge')
if any(abs(s['weight'])>1e-5 for s in native['shapes']):raise ValueError('Capture face_probe after expression_preview reset; native probe must use zero blend-shape weights')
if not np.array_equal(np.array(native['uv'],dtype=np.float32),np.array(captures['neutral']['uv'],dtype=np.float32)):raise ValueError('Native probe UV differs from the source snapshot')
bpy.ops.wm.open_mainfile(filepath=str(Path(args.blend).resolve()));scene=bpy.context.scene;scene.frame_set(1);bpy.context.view_layer.update()

def unity(obj,state):
    points=np.array([tuple(obj.matrix_world@v.co) for v in obj.data.shape_keys.key_blocks[state].data])
    return points[:,[0,2,1]]*np.array([1,1,-1])

face=captures['neutral'];vertex_count=len(face['vertices'])
topology=bytearray(struct.pack('<i',vertex_count));topology.extend(np.array(face['uv'],dtype='<f4').tobytes())
topology.extend(struct.pack('<i',len(face['submeshes'])))
for sub in face['submeshes']:
    topology.extend(struct.pack('<i',len(sub['triangles'])));topology.extend(np.array(sub['triangles'],dtype='<i4').tobytes())
package={'schema':'vroid-face-bridge/1','vertex_count':vertex_count,'topology_sha256':hashlib.sha256(topology).hexdigest(),'additions':[]}
deltas={state:np.zeros((vertex_count,3)) for state in captures}
skin=None
for sub in face['submeshes']:
    ids=sorted(set(sub['triangles']))
    candidates=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith(sub['material']) and len(o.data.vertices)==len(ids)]
    if len(candidates)!=1:raise ValueError('Cannot identify native submesh '+sub['material'])
    obj=candidates[0]
    for state,key in [('neutral','Basis'),('half','half'),('closed','closed')]:
        deltas[state][ids]=unity(obj,key)-np.array(captures[state]['vertices'])[ids]
    if sub['material']=='N00_000_00_Face_00_SKIN':skin=obj
for state,name in [('neutral','delta_open'),('half','delta_half'),('closed','delta_closed')]:package[name]=np.round(deltas[state],9).tolist()

scene.render.engine='CYCLES';scene.cycles.samples=1;scene.cycles.device='CPU'
scene.render.bake.use_selected_to_active=False;scene.render.bake.margin=2
textures=[]
def bake(obj,stem,skin_uv=False):
    mat=obj.data.materials[0];nodes=mat.node_tree.nodes;links=mat.node_tree.links
    uvname=obj.data.uv_layers[0].name if skin_uv else 'BridgeUV'
    if not skin_uv:
        layer=obj.data.uv_layers.get(uvname) or obj.data.uv_layers.new(name=uvname)
        points=unity(obj,'Basis');x=abs(points[:,0]);y=points[:,1];lo=np.array([x.min(),y.min()]);span=np.maximum(np.array([x.max(),y.max()])-lo,1e-6)
        uvs=(np.column_stack([x,y])-lo)/span
        for loop in obj.data.loops:layer.data[loop.index].uv=uvs[loop.vertex_index]
    else:
        layer=obj.data.uv_layers[0];uvs=None
    obj.data.uv_layers.active_index=0;obj.data.uv_layers[0].active_render=True
    for other in scene.objects:other.select_set(False)
    obj.select_set(True);bpy.context.view_layer.objects.active=obj
    emission=next(n for n in nodes if n.type=='EMISSION')
    color=emission.inputs['Color'].links[0].from_socket if emission.inputs['Color'].is_linked else tuple(emission.inputs['Color'].default_value)
    mixes=[n for n in nodes if n.type=='MIX_SHADER']
    alpha=(mixes[0].inputs[0].links[0].from_socket if mixes[0].inputs[0].is_linked else float(mixes[0].inputs[0].default_value)) if mixes else 1.0
    output=next(n for n in nodes if n.type=='OUTPUT_MATERIAL');original=output.inputs['Surface'].links[0].from_socket
    em=nodes.new('ShaderNodeEmission');links.new(em.outputs[0],output.inputs['Surface']);target=nodes.new('ShaderNodeTexImage')
    size=(1024,1024) if skin_uv else (512,128)
    for channel,value,space in [('color',color,'sRGB'),('alpha',alpha,'Non-Color')]:
        image=bpy.data.images.new(stem+'-'+channel,width=size[0],height=size[1],alpha=False);image.colorspace_settings.name=space
        target.image=image
        for n in nodes:n.select=False
        target.select=True;nodes.active=target
        for link in list(em.inputs['Color'].links):links.remove(link)
        if isinstance(value,tuple):em.inputs['Color'].default_value=value
        elif isinstance(value,(int,float)):em.inputs['Color'].default_value=(value,value,value,1)
        else:links.new(value,em.inputs['Color'])
        bpy.ops.object.bake(type='EMIT',uv_layer=uvname)
        image.filepath_raw=str(out/(stem+'-'+channel+'.png'));image.file_format='PNG';image.save()
    links.new(original,output.inputs['Surface']);nodes.remove(em);nodes.remove(target)
    textures.append({'stem':stem,'width':size[0],'height':size[1]})
    return uvs

bake(skin,'skin',True)
native_world=np.array(captures['neutral']['vertices']);rest_world=np.array(native['world_vertices']);neutral_offset=native_world-rest_world
package['reference_neutral_offset']=np.round(neutral_offset,9).tolist()
package['reference_rest_world']=np.round(rest_world,9).tolist()
package['closure_mask']=(np.linalg.norm(np.array(captures['closed']['vertices'])-native_world,axis=1)>1e-6).tolist()
bind_ids=np.array(sorted(set(face['submeshes'][3]['triangles'])|set(face['submeshes'][7]['triangles'])))
bind_points=native_world[bind_ids]
for obj in sorted(bpy.data.objects,key=lambda o:o.name):
    if obj.type!='MESH' or obj.hide_render or obj.name.startswith('N00_'):continue
    if not obj.data.shape_keys:continue
    index=len(package['additions']);stem='detail-'+str(index)
    uvs=bake(obj,stem);poses={state:unity(obj,key) for state,key in [('open','Basis'),('half','half'),('closed','closed')]}
    points=poses['open'];nearest=[]
    for start in range(0,len(points),128):
        d=((points[start:start+128,None,:]-bind_points[None,:,:])**2).sum(2)
        nearest.extend(bind_ids[d.argmin(1)].tolist())
    obj.data.calc_loop_triangles();triangles=[int(i) for tri in obj.data.loop_triangles for i in tri.vertices]
    addition={'name':obj.name,'base_world':np.round(points,9).tolist(),
              'bind_vertices':nearest,'uv':np.round(uvs,8).tolist(),'triangles':triangles,'texture_stem':stem}
    binding=obj.get('bridge_bone')
    if binding and binding!='J_Bip_C_Head':
        if binding not in ('J_Bip_C_UpperChest','J_Bip_C_Neck','J_Bip_L_LowerArm','J_Bip_R_LowerArm'):raise ValueError('Unsupported bridge binding bone: '+str(binding))
        addition['bone_name']=binding
    for state,values in poses.items():addition[state+'_world']=np.round(values,9).tolist()
    package['additions'].append(addition)
manifest={'package':package,'textures':textures,'source_blend':str(Path(args.blend).resolve()),'source_snapshots':str(source.resolve())}
(out/'export.json').write_text(json.dumps(manifest,separators=(',',':')))
print(json.dumps({'export':str(out/'export.json'),'additions':len(package['additions']),'vertices':sum(len(a['open_world']) for a in package['additions'])}),flush=True)
