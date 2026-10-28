"""Live tests for bounded native hair width and width-profile edits on a saved copy."""
import argparse
import json
from pathlib import Path
import socket
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'comfy-bridge'))
from vroid_client import VroidClient


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', required=True)
    parser.add_argument('--output', default='run/qa/hair-width')
    args = parser.parse_args()
    c = VroidClient('http://127.0.0.1:17891/mcp')
    root = Path(args.output).resolve() / uuid.uuid4().hex
    root.mkdir(parents=True)
    checks = []

    def raw(line, check=True):
        with socket.create_connection(('127.0.0.1', 17890), timeout=100) as sock:
            sock.sendall((line + '\n').encode())
            result = json.loads(sock.makefile('r', encoding='utf-8').readline())
        if check and not result.get('ok'):
            raise RuntimeError(result)
        return result['result'] if check else result

    def record(name):
        checks.append(name)
        (root / 'result.json').write_text(json.dumps({'checks': checks}, indent=2))
        print('PASS', name, flush=True)

    def listing():
        return c.call('hair_list')

    def all_info():
        return {s['engine_strand_id']: c.call('hair_get', strand_id=s['strand_id'])
                for g in listing()['groups'] for s in g['strands']}

    def compare(a, b):
        return all(a[k]['geometry_sha256'] == b[k]['geometry_sha256'] and
                   a[k]['native_parameters'] == b[k]['native_parameters'] for k in a)

    if c.call('state')['in_edit']:
        raise RuntimeError('App must be at model selection; refusing to discard an existing session')
    assert not raw('hair_group_width invalid 1 0', False)['ok']
    assert not raw('hair_profile invalid 0:0,1:0 0', False)['ok']
    record('no_model_guards')
    c.call('open', path=str(Path(args.model).resolve()))
    c.call('camera', action='frame', region='head', angle='front')
    initial = all_info()
    h = listing()
    group = next(g for g in h['groups'] if g['group_type'].endswith('.HairFront'))
    strand = group['strands'][0]
    gid, sid = group['group_id'], strand['strand_id']
    width = group['native_parameters']['width_scale']
    before = c.stable_preview()
    (root / 'before.png').write_bytes(before)
    for line in (
        f'hair_group_width {gid} 0 {h["revision"]}',
        f'hair_group_width {gid} 4.1 {h["revision"]}',
        f'hair_group_width {gid} NaN {h["revision"]}',
        f'hair_group_width unknown 1 {h["revision"]}',
        f'hair_group_width {gid} 1 -1',
        f'hair_profile {sid} 0:0,0.7:1,0.6:1,1:0 {h["revision"]}',
        f'hair_profile {sid} 0:0,0.5:1.6,1:0 {h["revision"]}',
        f'hair_profile {sid} 0:1,1:0 {h["revision"]}',
        f'hair_profile {sid} 0:0.000001,1:0 {h["revision"]}',
        f'hair_profile {sid} 0:0,1:0 -1',
    ):
        assert not raw(line, False)['ok'], line
    assert compare(initial, all_info()) and before == c.stable_preview()
    record('invalid_values_identities_revisions_preserve_model')
    r = raw(f'hair_group_width {gid} {width * 0.65} {h["revision"]}')
    changed = c.stable_preview()
    assert before != changed and compare(initial, all_info())
    assert abs(next(g for g in listing()['groups'] if g['group_id'] == gid)
               ['native_parameters']['width_scale'] - width*0.65) < 1e-6
    assert Path(r['checkpoint_path']).is_file()
    record('native_width_changes_render_preserves_points_and_strand_overrides')
    raw(f'hair_group_width {gid} {width} {listing()["revision"]}')
    assert c.stable_preview() == before and compare(initial, all_info())
    record('width_numeric_and_render_restore')
    info = all_info()
    target = info[strand['engine_strand_id']]
    profile = '0:0,0.08:0.6,0.25:0.78,0.65:0.82,0.87:0.75,0.96:0.4,1:0'
    raw(f'hair_profile {target["strand_id"]} {profile} {listing()["revision"]}')
    altered = all_info()
    key = strand['engine_strand_id']
    assert altered[key]['native_parameters']['width_curve_points'] != initial[key]['native_parameters']['width_curve_points']
    assert all(v['geometry_sha256'] == altered[k]['geometry_sha256'] for k, v in initial.items())
    assert all(v['native_parameters'] == altered[k]['native_parameters'] for k, v in initial.items() if k != key)
    c.call('camera', action='frame', region='head', angle='front')
    modified_preview = c.stable_preview()
    assert modified_preview != before
    (root / 'profile.png').write_bytes(modified_preview)
    record('profile_targets_one_strand_preserves_all_control_points')
    file = root / 'modified.vroid'
    c.call('save', path=str(file))
    c.call('main_menu')
    c.call('open', path=str(file))
    c.call('camera', action='frame', region='head', angle='front')
    reopened = all_info()
    (root / 'modified-hair.json').write_text(json.dumps(altered, indent=2))
    (root / 'reopened-hair.json').write_text(json.dumps(reopened, indent=2))
    assert compare(altered, reopened), 'Hair point/profile data changed after reopening'
    assert modified_preview == c.stable_preview(), 'Canonical head preview changed after reopening'
    face = c.call('face_bridge_status')
    assert face['active'] and not face['runtime_error']
    record('save_reopen_exact_geometry_profiles_preview_face_bridge_active')
    print(root, flush=True)


if __name__ == '__main__':
    main()
