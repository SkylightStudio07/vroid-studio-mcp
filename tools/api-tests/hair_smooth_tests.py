"""Exercise native single-strand smoothing on a dedicated saved fixture."""
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
    p.add_argument('--model',required=True)
    p.add_argument('--strand-prefix',default='293425d2')
    p.add_argument('--output',default='run/qa/hair-smooth')
    args=p.parse_args()
    root=Path(args.output).resolve()/uuid.uuid4().hex
    root.mkdir(parents=True)
    c=VroidClient('http://127.0.0.1:17891/mcp')
    checks=[]
    def raw(line,check=True):
        with socket.create_connection(('127.0.0.1',17890),timeout=100) as sock:
            sock.sendall((line+'\n').encode())
            r=json.loads(sock.makefile('r',encoding='utf-8').readline())
        if check and not r.get('ok'):
            raise RuntimeError(r)
        return r['result'] if check else r
    def check(name,condition):
        checks.append({'name':name,'passed':bool(condition)})
        (root/'result.json').write_text(json.dumps(checks,indent=2))
        assert condition,name
        print('PASS',name,flush=True)
    def data():
        h=c.call('hair_list')
        return {'groups':h['groups'],'strands':{s['engine_strand_id']:c.call('hair_get',strand_id=s['strand_id']) for g in h['groups'] for s in g['strands']}}
    def signature(d):
        return {'groups':d['groups'],'strands':{k:{'points':v['points'],'parameters':v['native_parameters']} for k,v in d['strands'].items()}}
    if c.call('state')['in_edit']:
        raise RuntimeError('Dedicated app must be at model selection; refusing to discard an editing session')
    check('no_model_guard',not raw('hair_smooth invalid 0',False)['ok'])
    c.call('open',path=str(Path(args.model).resolve()))
    before=data()
    key=next(k for k in before['strands'] if k.startswith(args.strand_prefix))
    selected=before['strands'][key]
    sid=selected['strand_id'];rev=selected['revision']
    check('invalid_identity',not raw(f'hair_smooth invalid {rev}',False)['ok'])
    check('invalid_revision',not raw(f'hair_smooth {sid} -1',False)['ok'])
    check('guards_preserve_native_data',signature(data())==signature(before))
    c.call('camera',action='frame',region='head',angle='front')
    preview=c.stable_preview()
    result=raw(f'hair_smooth {sid} {rev}')
    (root/'operation.json').write_text(json.dumps(result,indent=2))
    after=data()
    check('checkpoint_and_native_result',Path(result['checkpoint_path']).is_file() and
          result['geometry_sha256']==after['strands'][key]['geometry_sha256'] and result['geometry_sha256']!=selected['geometry_sha256'])
    check('all_other_strands_and_group_settings_preserved',before['groups']==after['groups'] and
          all(v['geometry_sha256']==after['strands'][k]['geometry_sha256'] and v['native_parameters']==after['strands'][k]['native_parameters'] for k,v in before['strands'].items() if k!=key))
    c.call('camera',action='frame',region='head',angle='front')
    modified=c.stable_preview()
    check('stable_render_changed',modified!=preview)
    file=root/'smoothed.vroid'
    c.call('save',path=str(file))
    c.call('main_menu')
    c.call('open',path=str(file))
    # Session IDs expire when opening a model, so compare engine identities only.
    reopened=data()
    def stable(d):
        value=signature(d)
        value['groups']=[{'group_id':g['group_id'],'parameters':g['native_parameters'],'visible':g['visible'],
                          'strands':{s['engine_strand_id']:s['visible'] for s in g['strands']}} for g in d['groups']]
        return value
    check('save_reopen_native_geometry_exact',stable(after)==stable(reopened))
    c.call('camera',action='frame',region='head',angle='front')
    check('save_reopen_preview_exact',modified==c.stable_preview())
    state=c.call('face_bridge_status')
    check('face_bridge_remains_active',state['active'] and state['runtime_error'] is None)
    print(root,flush=True)


if __name__=='__main__':
    main()
