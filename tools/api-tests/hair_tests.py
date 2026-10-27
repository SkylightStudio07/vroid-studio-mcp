"""Hair geometry integration checks against a dedicated app at the model-selection screen.

Requires a saved model with visible brush hair. Never discards an existing editing session.
Writes unique artifacts and leaves a modified test copy open. No fixed-delay engine waits.
"""
import argparse
import json
from pathlib import Path
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'comfy-bridge'))
from vroid_client import VroidClient
from image_validation import BridgeError, sha


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', required=True)
    parser.add_argument('--url', default='http://127.0.0.1:17891/mcp')
    parser.add_argument('--output', default='run/qa/hair')
    parser.add_argument('--preset-category', help='Optional current-language hairstyle category to test preset invalidation')
    args = parser.parse_args()
    c = VroidClient(args.url)
    c.initialize()
    if c.call('state')['in_edit']:
        raise RuntimeError('Dedicated app must be at the model-selection screen; refusing to discard session')
    root = Path(args.output).resolve() / uuid.uuid4().hex
    root.mkdir(parents=True)
    checks = {}

    def record(name):
        checks[name] = True
        print('PASS', name, flush=True)

    def rejected(name, tool, expected_code, **kw):
        try:
            c.call(tool, **kw)
        except BridgeError as exc:
            assert expected_code in str(exc), str(exc)
        else:
            raise AssertionError(name + ': unexpected success')
        record(name)

    def listing():
        return [(g, s) for g in c.call('hair_list')['groups'] for s in g['strands']]

    def info(sid):
        return c.call('hair_get', strand_id=sid)

    def restore(result, sid):
        return c.call('hair_restore', backup_id=result['backup_id'], expected_revision=info(sid)['revision'])

    try:
        rejected('no_model', 'hair_list', 'NOT_IN_EDIT_MODE')
        c.call('open', path=str(Path(args.model).resolve()))
        pairs = listing()
        g, s = next((g, s) for g, s in pairs if g['visible'] and s['visible'] and s['move_supported'])
        sid = s['strand_id']
        before = info(sid)
        assert len(before['points']) >= 3
        index = len(before['points']) - 2
        other = {x['strand_id']: info(x['strand_id'])['geometry_sha256'] for _, x in pairs if x['strand_id'] != sid}
        record('enumerate_read')
        assert any(x['strand_id'] == sid for _, x in listing())
        assert info(sid)['geometry_sha256'] == before['geometry_sha256']
        record('list_preserves_handles')
        (root / 'before.json').write_text(json.dumps(before, indent=2))
        c.call('camera', action='frame', region='head', angle='front')
        image = c.stable_preview()
        (root / 'before-front.png').write_bytes(image)
        c.call('save', path=str(root / 'baseline.vroid'))
        base = dict(strand_id=sid, point_index=index, dx=-0.025, dy=0, dz=0, expected_revision=info(sid)['revision'])
        cases = [
            ('root_locked', {'point_index': 0}, 'INVALID_HAIR_POINT'),
            ('negative_index', {'point_index': -1}, 'INVALID_HAIR_POINT'),
            ('point_range', {'point_index': len(before['points'])}, 'INVALID_HAIR_POINT'),
            ('fractional_index', {'point_index': 1.5}, 'INVALID_HAIR_POINT'),
            ('nonfinite', {'dx': 'NaN'}, 'INVALID_HAIR_DELTA'),
            ('overflow', {'dx': 1e100}, 'INVALID_HAIR_DELTA'),
            ('oversized_offset', {'dx': 0.1}, 'INVALID_HAIR_DELTA'),
            ('combined_offset_bound', {'dx': 0.04, 'dy': 0.04}, 'INVALID_HAIR_DELTA'),
            ('zero_offset', {'dx': 0}, 'INVALID_HAIR_DELTA'),
            ('stale_revision', {'expected_revision': -1}, 'STALE_HAIR_REVISION'),
            ('unknown_strand', {'strand_id': 'unknown'}, 'STALE_HAIR_TARGET'),
        ]
        for name, overrides, code in cases:
            rejected(name, 'hair_move_point', code, **(base | overrides))
            assert info(sid)['geometry_sha256'] == before['geometry_sha256']
            assert info(sid)['revision'] == base['expected_revision']
        rejected('unknown_backup', 'hair_restore', 'STALE_HAIR_TARGET', backup_id='unknown', expected_revision=base['expected_revision'])
        assert sha(c.stable_preview()) == sha(image)
        record('guards_preserve_geometry_and_render')

        moved = c.call('hair_move_point', **base)
        (root / 'move.json').write_text(json.dumps(moved, indent=2))
        after = info(sid)
        (root / 'after.json').write_text(json.dumps(after, indent=2))
        assert after['geometry_sha256'] == moved['geometry_sha256'] != before['geometry_sha256']
        for i, (a, b) in enumerate(zip(before['points'], after['points'])):
            assert a['normal'] == b['normal'] and a['uv'] == b['uv']
            if i != index:
                assert a == b
            else:
                assert abs(b['position'][0] - a['position'][0] + 0.025) < 1e-7
                assert b['position'][1:] == a['position'][1:]
        record('exact_point_edit_preserves_normals_uv_root')
        assert all(info(k)['geometry_sha256'] == v for k, v in other.items())
        record('other_strands_unchanged')
        for angle in ('front', 'right', 'back'):
            c.call('camera', action='frame', region='head', angle=angle)
            preview = c.stable_preview()
            (root / ('after-' + angle + '.png')).write_bytes(preview)
            if angle == 'front':
                assert sha(preview) != sha(image)
        record('stable_multiview_render_changed')
        c.call('camera', action='frame', region='head', angle='front')
        saved = root / 'modified.vroid'
        c.call('save', path=str(saved))
        reversed_edit = restore(moved, sid)
        assert info(sid)['geometry_sha256'] == before['geometry_sha256']
        assert sha(c.stable_preview()) == sha(image)
        record('exact_restore_geometry_and_render')
        # A restore itself can be restored, without using global undo history.
        reapplied = restore(reversed_edit, sid)
        assert info(sid)['geometry_sha256'] == after['geometry_sha256']
        record('restore_is_reversible')
        second = c.call('hair_move_point', **(base | {'dx': 0.005, 'expected_revision': info(sid)['revision']}))
        rejected('changed_strand_restore_rejected', 'hair_restore', 'STALE_HAIR_TARGET',
                 backup_id=moved['backup_id'], expected_revision=info(sid)['revision'])
        restore(second, sid)
        restore(moved, sid)
        assert info(sid)['geometry_sha256'] == before['geometry_sha256']
        record('stacked_scoped_restore')

        c.call('main_menu')
        c.call('open', path=str(saved))
        pairs2 = listing()
        sid2 = next(x['strand_id'] for group, x in pairs2
                    if group['group_id'] == g['group_id'] and x['engine_strand_id'] == s['engine_strand_id'])
        assert info(sid2)['geometry_sha256'] == after['geometry_sha256']
        record('save_reopen_exact_geometry')
        rejected('old_session_id', 'hair_get', 'STALE_HAIR_TARGET', strand_id=sid)
        rejected('old_session_backup', 'hair_restore', 'STALE_HAIR_TARGET',
                 backup_id=moved['backup_id'], expected_revision=info(sid2)['revision'])
        c.call('camera', action='frame', region='head', angle='front')
        reopened = c.stable_preview()
        (root / 'reopened-front.png').write_bytes(reopened)
        assert sha(reopened) == sha((root / 'after-front.png').read_bytes())
        record('save_reopen_exact_render')
        if args.preset_category:
            presets = c.call('presets', section='Hairstyle', category=args.preset_category)
            selection = next(p for p in presets if p['name'] != '(clear)')
            c.call('select_preset', section='Hairstyle', category=args.preset_category, index=selection['index'])
            rejected('preset_invalidates_id', 'hair_get', 'STALE_HAIR_TARGET', strand_id=sid2)
            c.call('main_menu')
            c.call('open', path=str(saved))
            c.call('camera', action='frame', region='head', angle='front')
        result = {'ok': True, 'checks': checks, 'artifacts': str(root), 'model': str(saved)}
    except Exception as exc:
        result = {'ok': False, 'checks': checks, 'error': str(exc), 'artifacts': str(root)}
        raise
    finally:
        (root / 'result.json').write_text(json.dumps(result, indent=2))
        print(json.dumps(result), flush=True)


if __name__ == '__main__':
    main()
