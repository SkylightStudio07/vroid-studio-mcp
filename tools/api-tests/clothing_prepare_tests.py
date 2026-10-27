"""Read-only/idempotent live checks on an already prepared clothing fixture."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'comfy-bridge'))
from vroid_client import VroidClient

parser = argparse.ArgumentParser()
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


listing = c.call('texture_targets')
revision = listing['revision']
workspace = Path(listing['workspace_root'])
checkpoints = set((workspace / 'preparation').glob('*.vroid'))
face = c.call('face_bridge_status')
c.call('camera', action='frame', region='body', angle='front')
preview = c.stable_preview()
schema = next(t for t in c.rpc('tools/list')['tools'] if t['name'] == 'texture_prepare')
allowed = schema['inputSchema']['properties']['group_type']['enum']
check('MCP_schema_lists_only_the_four_supported_groups', set(allowed) == {
    'TransferableGroupType.N00.Level1.' + s for s in ('FacePaint', 'Tops', 'Bottoms', 'Socks')})
for suffix in ('Tops', 'Bottoms', 'Socks'):
    group = 'TransferableGroupType.N00.Level1.' + suffix
    rejected = c.rpc('tools/call', {'name': 'texture_prepare', 'arguments': {'group_type': group, 'expected_revision': revision-1}})
    check(suffix + '_rejects_stale_revision_without_mutation', rejected.get('isError') and c.call('texture_targets')['revision'] == revision)
    result = c.call('texture_prepare', group_type=group, expected_revision=revision)
    check(suffix + '_already_prepared_is_noop', result.get('already_prepared') and result['checkpoint_path'] is None and result['revision'] == revision)
    targets = [v for v in result['targets'] if v['group_type'] == group]
    check(suffix + '_returns_discoverable_editable_layers', bool(targets) and all(
        any(l['editable'] for l in c.call('texture_layers', target_id=t['target_id'])['layers']) for t in targets))
bad = c.rpc('tools/call', {'name': 'texture_prepare', 'arguments': {'group_type': 'TransferableGroupType.N00.Level1.BodySkin', 'expected_revision': revision}})
check('unsupported_group_rejected_without_mutation', bad.get('isError') and c.call('texture_targets')['revision'] == revision)
check('no_checkpoint_side_effects_on_noops_and_invalid_input', set((workspace / 'preparation').glob('*.vroid')) == checkpoints)
check('noops_preserve_stable_preview_exact', c.stable_preview() == preview)
status = c.call('face_bridge_status')
check('noops_preserve_embedded_face_and_accessories', status['sha256'] == face['sha256'] and status['active'] and not status['runtime_error'])
probe = json.loads(Path(c.call('hair_body_probe')['probe_path']).read_text())
check('read_only_avatar_probe_includes_one_uv_per_vertex', all(len(r['uv']) == len(r['world_vertices']) for r in probe['renderers']))
check('probe_preserves_revision_and_stable_preview', c.call('texture_targets')['revision'] == revision and c.stable_preview() == preview)
print(json.dumps({'passed': len(checks), 'preview_sha256': hashlib.sha256(preview).hexdigest()}), flush=True)
