"""Verify real clothing conversion on an unprepared fixture and restore the selected model."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'comfy-bridge'))
from vroid_client import VroidClient

parser = argparse.ArgumentParser()
parser.add_argument('--source-model', type=Path, required=True)
parser.add_argument('--restore-model', type=Path, required=True)
parser.add_argument('--out', type=Path, required=True)
args = parser.parse_args()
args.out.mkdir(parents=True, exist_ok=True)
c = VroidClient('http://127.0.0.1:17891/mcp')
checks = []


def check(name, ok, details=None):
    checks.append({'name': name, 'passed': bool(ok), 'details': details})
    (args.out / 'result.json').write_text(json.dumps({'checks': checks}, indent=2), encoding='utf-8')
    assert ok, (name, details)
    print('PASS', name, flush=True)


def geometry():
    return json.loads(Path(c.call('hair_body_probe')['probe_path']).read_text())['renderers']


original = c.call('face_bridge_status')
try:
    c.call('main_menu')
    c.call('open', path=str(args.source_model.resolve()))
    c.call('camera', action='frame', region='body', angle='front')
    preview = c.stable_preview()
    before_geometry = geometry()
    before_face = c.call('face_bridge_status')
    for suffix in ('Tops', 'Bottoms', 'Socks'):
        group = 'TransferableGroupType.N00.Level1.' + suffix
        initial = c.call('texture_targets')
        check(suffix + '_fixture_is_not_yet_prepared', not any(t['group_type'] == group for t in initial['targets']))
        result = c.call('texture_prepare', group_type=group, expected_revision=initial['revision'])
        check(suffix + '_conversion_saves_checkpoint_and_increments_revision', result['ok'] and Path(result['checkpoint_path']).is_file() and result['revision'] > initial['revision'])
        check(suffix + '_conversion_creates_editable_targets', any(t['group_type'] == group for t in result['targets']))
        check(suffix + '_conversion_preserves_exact_stable_render', c.stable_preview() == preview)
    after_geometry = geometry()
    clean = lambda rows: {r['name']: {k: v for k, v in r.items() if k not in ('bounds_center', 'bounds_size')} for r in rows}
    check('all_conversion_vertex_uv_triangle_and_material_data_preserved', clean(before_geometry) == clean(after_geometry))
    status = c.call('face_bridge_status')
    check('conversion_keeps_original_face_package_active', status['active'] and status['active_sha256'] == before_face['active_sha256'])
    saved = args.out.resolve() / 'prepared-fixture.vroid'
    c.call('save', path=str(saved))
    c.call('main_menu')
    c.call('open', path=str(saved))
    c.call('camera', action='frame', region='body', angle='front')
    check('converted_fixture_save_reopen_render_exact', c.stable_preview() == preview)
    check('converted_fixture_save_reopen_all_groups_editable', all(any(t['group_type'] == 'TransferableGroupType.N00.Level1.' + suffix for t in c.call('texture_targets')['targets']) for suffix in ('Tops', 'Bottoms', 'Socks')))
finally:
    c.call('main_menu')
    c.call('open', path=str(args.restore_model.resolve()))
    status = c.call('face_bridge_status')
    check('selected_model_restored_with_original_package', status['active'] and status['active_sha256'] == original['active_sha256'])
    c.call('camera', action='frame', region='upper', angle='front')
print(json.dumps({'passed': len(checks), 'restored_model': str(args.restore_model.resolve())}), flush=True)
