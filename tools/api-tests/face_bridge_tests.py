"""Live checks for a matching, already-open face fixture. Leaves the imported model saved/open."""
import argparse,hashlib,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'comfy-bridge'))
from vroid_client import VroidClient
import socket

NEUTRAL='BlendShape.Synthesized.N00_Face.N00.All.Neutral'
parser=argparse.ArgumentParser();parser.add_argument('--package',required=True);parser.add_argument('--source',required=True);parser.add_argument('--out',required=True)
args=parser.parse_args();out=Path(args.out).resolve();out.mkdir(parents=True,exist_ok=True)
package_path=Path(args.package).resolve();package=json.loads(package_path.read_text());digest=hashlib.sha256(package_path.read_bytes()).hexdigest()
c=VroidClient('http://127.0.0.1:17891/mcp');checks=[]
def raw(command):
    with socket.create_connection(('127.0.0.1',17890),timeout=90) as sock:
        sock.sendall((command+'\n').encode());result=json.loads(sock.makefile('r',encoding='utf-8').readline())
    if not result.get('ok'):raise RuntimeError(result)
    return result.get('result')
def check(name,ok,detail=None):
    checks.append({'name':name,'ok':bool(ok),'detail':detail});print(('PASS ' if ok else 'FAIL ')+name,flush=True)
    if not ok:raise AssertionError((name,detail))
def shot(name):
    c.call('camera',action='frame',region='face',angle='front');data=c.stable_preview();(out/(name+'.png')).write_bytes(data);return hashlib.sha256(data).hexdigest()
def probe():
    data=c.call('face_probe');return json.loads(Path(data['probe_path']).read_text())
def geometry(state):
    actual=probe();face=next(r for r in actual['renderers'] if r['name']=='Face');extra=next(r for r in actual['renderers'] if r['name']=='MCPFaceBridge.Details')
    source=next(r for r in json.loads((Path(args.source)/(state+'.json')).read_text())['renderers'] if r['name']=='Face')
    key={'neutral':'open','half':'half','closed':'closed'}[state]
    target=[[v[k]+d[k] for k in range(3)] for v,d in zip(source['vertices'],package['delta_'+key])]
    expected_extra=[v for mesh in package['additions'] for v in mesh[key+'_world']]
    def error(a,b):
        if len(a)!=len(b):return 1000
        return max(abs(x-y) for p,q in zip(a,b) for x,y in zip(p,q))
    e1=error(face['world_vertices'],target);e2=error(extra['world_vertices'],expected_extra)
    check('world geometry '+state,e1<.00002 and e2<.00002,{'face_max_m':e1,'details_max_m':e2})
def closure(value):
    for key in ['Fcl_EYE_Close_L','Fcl_EYE_Close_R']:
        revision=raw('expression_list')['revision'];raw(f'expression_set {NEUTRAL} 4 {key} {value} {revision}')
    raw('expression_preview '+NEUTRAL)
def active():
    s=c.call('face_bridge_status');check('embedded hash and active renderer',s['active'] and s['sha256']==digest and s['active_sha256']==digest,s);return s

raw('expression_preview '+NEUTRAL);baseline=shot('native-before')
status=c.call('face_bridge_status');initial_revision=status['revision'];checkpoint_root=package_path.parent.parent/'checkpoints'
count=len(list(checkpoint_root.glob('*.vroid'))) if checkpoint_root.exists() else 0
bad=package_path.parent/'invalid-package.json'
for label,change in [('topology',lambda p:p.update(topology_sha256='0'*64)),('triangle',lambda p:p['additions'][0]['triangles'].__setitem__(0,999999)),('source pose',lambda p:p['reference_rest_world'][0].__setitem__(0,p['reference_rest_world'][0][0]+.001))]:
    candidate=json.loads(package_path.read_text());change(candidate);bad.write_text(json.dumps(candidate))
    rejected=False
    try:c.call('face_import',input_path=str(bad),expected_revision=initial_revision)
    except Exception:rejected=True
    check('reject invalid '+label,rejected and c.call('face_bridge_status')['revision']==initial_revision)
check('invalid input creates no checkpoint',len(list(checkpoint_root.glob('*.vroid')))==count)
rejected=False
try:c.call('face_import',input_path=str(package_path),expected_revision=initial_revision-1)
except Exception:rejected=True
check('reject stale revision',rejected and c.call('face_bridge_status')['revision']==initial_revision)
rejected=False
try:c.call('face_import',input_path=str(out/'outside-workspace.json'),expected_revision=initial_revision)
except Exception:rejected=True
check('reject outside workspace',rejected and c.call('face_bridge_status')['revision']==initial_revision)
raw('expression_preview reset')
imported=c.call('face_import',input_path=str(package_path),expected_revision=initial_revision)
check('checkpoint saved before import',Path(imported['checkpoint_path']).is_file());active();geometry('neutral');shot('native-default')
raw('expression_preview '+NEUTRAL);geometry('neutral');after=shot('native-open');check('import changes stable preview',after!=baseline)
for label,value in [('half',60),('closed',100),('neutral',26)]:
    closure(value);active();geometry(label);shot('native-'+label)
final=out/'asuna-import-v24.vroid';c.call('save',path=str(final));c.call('main_menu',mode='discard');c.call('open',path=str(final));active();geometry('neutral');shot('native-reopened-default')
raw('expression_preview '+NEUTRAL);reopened=shot('native-reopened');check('save/reopen stable neutral preview',reopened==after)
status=c.call('face_bridge_status');restored=c.call('face_restore',expected_revision=status['revision']);check('restore removes package and runtime meshes',not restored['stored'] and not restored['active'])
raw('expression_preview '+NEUTRAL);restored_image=shot('native-restored');check('restore recovers exact native preview',restored_image==baseline)
status=c.call('face_bridge_status');c.call('face_import',input_path=str(package_path),expected_revision=status['revision']);active();c.call('save',path=str(final))
for angle in ['threequarter_left','threequarter_right']:
    c.call('camera',action='frame',region='face',angle=angle);(out/('native-'+angle+'.png')).write_bytes(c.stable_preview())
raw('expression_preview '+NEUTRAL);shot('native-final')
(out/'live-verification.json').write_text(json.dumps({'checks':checks,'model':str(final),'package_sha256':digest},indent=2))
print(json.dumps({'passed':len(checks),'model':str(final)}),flush=True)
