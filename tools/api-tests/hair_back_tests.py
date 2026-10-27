"""Exercise back-group width edits and the bounded read-only body geometry probe."""
import argparse
import json
from pathlib import Path
import socket
import sys
import uuid

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'comfy-bridge'))
from vroid_client import VroidClient


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--model',required=True);p.add_argument('--output',required=True)
    args=p.parse_args();root=Path(args.output).resolve()/uuid.uuid4().hex;root.mkdir(parents=True)
    c=VroidClient('http://127.0.0.1:17891/mcp');checks=[]
    def raw(line):
        with socket.create_connection(('127.0.0.1',17890),timeout=100) as sock:
            sock.sendall((line+'\n').encode());return json.loads(sock.makefile('r',encoding='utf-8').readline())
    def check(name,condition):
        checks.append({'name':name,'passed':bool(condition)});(root/'result.json').write_text(json.dumps(checks,indent=2))
        assert condition,name
        print('PASS',name,flush=True)
    def native():
        h=c.call('hair_list')
        return {'groups':h['groups'],'strands':{s['engine_strand_id']:c.call('hair_get',strand_id=s['strand_id']) for g in h['groups'] for s in g['strands']}}
    def strands(d):return {k:(v['geometry_sha256'],v['native_parameters']) for k,v in d['strands'].items()}
    def groups(d):return {g['group_id']:{'parameters':g['native_parameters'],'visible':g['visible'],
        'strands':{s['engine_strand_id']:s['visible'] for s in g['strands']}} for g in d['groups']}
    if c.call('state')['in_edit']:raise RuntimeError('Dedicated app must be at model selection')
    check('probe_no_model_guard',c.rpc('tools/call',{'name':'hair_body_probe','arguments':{}}).get('isError'))
    check('width_no_model_guard',not raw('hair_group_width invalid 1 0')['ok'])
    c.call('open',path=str(Path(args.model).resolve()));before=native()
    h=c.call('hair_list');revision=h['revision']
    group=next(g for g in h['groups'] if g['group_id'].startswith('ac8809d7'))
    gid=group['group_id'];width=group['native_parameters']['width_scale']
    c.call('camera',action='frame',region='upper',angle='back');preview=c.stable_preview()
    face=c.call('face_bridge_status')
    probe=c.call('hair_body_probe');data=json.loads(Path(probe['probe_path']).read_text())
    check('probe_read_only_revision_and_preview',probe['read_only'] and c.call('hair_list')['revision']==revision and c.stable_preview()==preview)
    check('probe_has_body_hair_and_head_transform',data['head_local_to_world'] is not None and any(r['name']=='Body' for r in data['renderers']) and any(r['name'].startswith('HairStrand-') for r in data['renderers']))
    check('probe_indices_valid',all(len(s['triangles'])%3==0 and all(0<=i<len(r['world_vertices']) for i in s['triangles']) for r in data['renderers'] for s in r['submeshes']))
    for line in [f'hair_group_width {gid} NaN {revision}',f'hair_group_width {gid} 0 {revision}',
                 f'hair_group_width {gid} 4.1 {revision}',f'hair_group_width invalid 1 {revision}',f'hair_group_width {gid} 1 -1']:
        check('invalid_'+line.split()[2]+'_'+line.split()[1][:8],not raw(line)['ok'])
    check('guards_preserve_all_native_data',before==native())
    result=raw(f'hair_group_width {gid} 1.55 {revision}');assert result['ok'],result
    result=result['result'];after=native()
    check('back_width_readback_and_checkpoint',abs(next(g for g in after['groups'] if g['group_id']==gid)['native_parameters']['width_scale']-1.55)<1e-6 and Path(result['checkpoint_path']).is_file())
    check('all_strand_points_profiles_and_other_groups_preserved',strands(before)==strands(after) and
          {k:v for k,v in groups(before).items() if k!=gid}=={k:v for k,v in groups(after).items() if k!=gid})
    check('back_width_changes_stable_preview',preview!=c.stable_preview())
    result=raw(f'hair_group_width {gid} {width} {c.call("hair_list")["revision"]}');assert result['ok'],result
    check('width_restore_preview_exact',preview==c.stable_preview())
    restored=native()
    check('all_native_data_restored',groups(before)==groups(restored) and strands(before)==strands(restored))
    current=c.call('face_bridge_status')
    check('face_package_remains_active',current['active'] and current['sha256']==current['active_sha256']==face['sha256'] and not current['runtime_error'])
    print(root,flush=True)


if __name__=='__main__':main()
