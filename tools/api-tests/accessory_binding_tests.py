"""Check an active source-matched package's rigid accessory bindings without editing it."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'comfy-bridge'))
from vroid_client import VroidClient

parser=argparse.ArgumentParser()
parser.add_argument('--package',type=Path,required=True)
parser.add_argument('--out',type=Path,required=True)
args=parser.parse_args();args.out.mkdir(parents=True,exist_ok=True)
package=json.loads(args.package.read_text());digest=hashlib.sha256(args.package.read_bytes()).hexdigest()
c=VroidClient('http://127.0.0.1:17891/mcp');checks=[]


def check(name,ok,details=None):
    checks.append({'name':name,'passed':bool(ok),'details':details})
    (args.out/'result.json').write_text(json.dumps({'checks':checks},indent=2),encoding='utf-8')
    assert ok,(name,details)
    print('PASS',name,flush=True)


status=c.call('face_bridge_status')
check('selected_package_active',status['active'] and status['active_sha256']==digest and not status['runtime_error'])
c.call('camera',action='frame',region='upper',angle='front');preview=c.stable_preview()
body=json.loads(Path(c.call('hair_body_probe')['probe_path']).read_text())
check('read_only_rig_probe_contains_both_forearms',all(any(b['name']==name for b in body['rig_bones']) for name in ('J_Bip_L_LowerArm','J_Bip_R_LowerArm')))
probe=json.loads(Path(c.call('face_probe')['probe_path']).read_text())
extra=next(r for r in probe['renderers'] if r['name']=='MCPFaceBridge.Details')
offset=0;bone_parts={}
for a in package['additions']:
    count=len(a['base_world']);name=a.get('bone_name','J_Bip_C_Head');index=extra['bones'].index(name)
    assert all(w[0][0]==index and w[1][0]==1 and all(v==0 for v in w[1][1:]) for w in extra['weights'][offset:offset+count]),a['name']
    offset+=count;bone_parts[name]=bone_parts.get(name,0)+1
check('each_part_has_its_declared_single_bone_weight',offset==len(extra['weights']),bone_parts)
check('cuffs_and_wrist_band_use_forearms_not_head',all(next(a for a in package['additions'] if a['name']==name)['bone_name']==bone for name,bone in [
    ('Asuna.RolledCuff42.L','J_Bip_L_LowerArm'),('Asuna.RolledCuff42.R','J_Bip_R_LowerArm'),('Asuna.WristScrunchie42','J_Bip_L_LowerArm')]))
workspace=Path(c.call('texture_targets')['workspace_root'])
checkpoints=set((workspace/'face-bridge/checkpoints').glob('*.vroid'))
invalid=args.package.parent/'invalid-binding-check.json'
for name in ('J_Bip_R_Hand','J_Bip_X_LowerArm','j_bip_l_lowerarm'):
    bad=json.loads(args.package.read_text());bad['additions'][-1]['bone_name']=name
    invalid.write_text(json.dumps(bad,separators=(',',':')),encoding='utf-8')
    response=c.rpc('tools/call',{'name':'face_import','arguments':{'input_path':str(invalid),'expected_revision':status['revision']}})
    check('reject_unvalidated_bone_'+name,response.get('isError') and c.call('face_bridge_status')['revision']==status['revision'])
check('invalid_bindings_create_no_checkpoints',set((workspace/'face-bridge/checkpoints').glob('*.vroid'))==checkpoints)
check('probes_and_invalid_calls_preserve_stable_render',c.stable_preview()==preview)
check('probes_and_invalid_calls_preserve_package',c.call('face_bridge_status')['active_sha256']==digest)
print(json.dumps({'passed':len(checks)}),flush=True)
