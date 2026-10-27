"""Exercise experimental raw character controls on a dedicated, saved model copy.

Start at the model-selection screen. Uses production handlers, model signals and stable frames.
Never discards an existing editing session. Leaves a saved verification copy open.
"""
import argparse
import json
from pathlib import Path
import socket
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'comfy-bridge'))
from vroid_client import VroidClient
from image_validation import sha


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', required=True)
    parser.add_argument('--url', default='http://127.0.0.1:17891/mcp')
    parser.add_argument('--raw-port', type=int, default=17890)
    parser.add_argument('--output', default='run/qa/character-controls')
    args = parser.parse_args()
    c = VroidClient(args.url)
    c.initialize()
    if c.call('state')['in_edit']:
        raise RuntimeError('Dedicated app must be at model selection; refusing to discard session')
    root = Path(args.output).resolve() / uuid.uuid4().hex
    root.mkdir(parents=True)
    checks = {}
    passed, error = False, None

    def record(name):
        checks[name] = True
        print('PASS', name, flush=True)

    def raw(command, check=True):
        with socket.create_connection(('127.0.0.1', args.raw_port), timeout=100) as sock:
            sock.sendall((command + '\n').encode())
            result = json.loads(sock.makefile('r', encoding='utf-8').readline())
        if check:
            assert result.get('ok'), result
            return result['result']
        return result

    def rejected(name, command):
        result = raw(command, check=False)
        assert not result.get('ok'), result
        record(name)

    def revision():
        return c.call('hair_list')['revision']

    def hair():
        groups = c.call('hair_list')['groups']
        result = {}
        for group in groups:
            strands = {}
            for strand in group['strands']:
                data = c.call('hair_get', strand_id=strand['strand_id'])
                strands[strand['engine_strand_id']] = {
                    'visible': strand['visible'], 'points': data['points']}
            result[group['group_id']] = {
                'type': group['group_type'], 'visible': group['visible'],
                'parameters': group['native_parameters'], 'strands': strands}
        return result

    def preview(key):
        result = raw('expression_preview ' + key)
        assert result['stable_preview_hash']
        return c.stable_preview()

    def set_expression(category, part, key, value):
        return raw(f'expression_set {category} {part} {key} {value} {revision()}')

    try:
        for command in ('expression_list', 'expression_preview reset', 'hair_mirror_front 0', 'hair_visibility x true 0'):
            rejected('no_model_' + command.split()[0], command)
        c.call('open', path=str(Path(args.model).resolve()))
        expressions = raw('expression_list')
        baseline = hair()
        (root / 'baseline-hair.json').write_text(json.dumps(baseline, indent=2))
        (root / 'baseline-expressions.json').write_text(json.dumps(expressions, indent=2))
        category = expressions['categories'][0]
        key = category['key']
        candidates = [(i, p) for i, part in enumerate(category['parts'])
                      for p in part['parameters'] if p['key'] == 'Fcl_MTH_Joy']
        part_index, parameter = next((pair for pair in candidates if pair[1]['value'] > 0), candidates[0])
        before = preview(key)
        (root / 'before.png').write_bytes(before)
        rev = revision()
        guards = {
            'stale_expression': f'expression_set {key} {part_index} {parameter["key"]} 0 -1',
            'nonfinite_expression': f'expression_set {key} {part_index} {parameter["key"]} NaN {rev}',
            'range_expression': f'expression_set {key} {part_index} {parameter["key"]} 100000 {rev}',
            'part_expression': f'expression_set {key} -1 {parameter["key"]} 0 {rev}',
            'key_expression': f'expression_set {key} {part_index} unknown 0 {rev}',
            'category_expression': f'expression_set unknown {part_index} {parameter["key"]} 0 {rev}',
            'unknown_preview': 'expression_preview unknown',
            'stale_mirror': 'hair_mirror_front -1',
            'nonfinite_scale': f'hair_scale_back NaN {rev}',
            'range_scale': f'hair_scale_back 3 {rev}',
            'nonfinite_offset': f'hair_offset_front Infinity {rev}',
            'range_offset': f'hair_offset_front .2 {rev}',
            'zero_offset': f'hair_offset_front 0 {rev}',
            'unknown_group': f'hair_offset_group unknown .01 {rev}',
            'invalid_visibility': f'hair_visibility unknown maybe {rev}',
        }
        for name, command in guards.items():
            rejected(name, command)
            assert revision() == rev
        assert raw('expression_list') == expressions
        assert hair() == baseline
        assert sha(preview(key)) == sha(before)
        record('guards_preserve_values_geometry_and_render')

        changed = 10 if parameter['value'] != 10 else 55
        result = set_expression(key, part_index, parameter['key'], changed)
        assert result['value'] == changed
        after = preview(key)
        assert sha(after) != sha(before)
        (root / 'expression-changed.png').write_bytes(after)
        record('expression_readback_and_visible_change')
        set_expression(key, part_index, parameter['key'], parameter['value'])
        assert sha(preview(key)) == sha(before)
        record('expression_exact_render_restore')
        rev = revision()
        assert not set_expression(key, part_index, parameter['key'], parameter['value'])['changed']
        assert revision() == rev
        preview('reset')
        assert revision() == rev
        record('expression_noop_and_preview_preserve_revision')
        preview(key)

        group = next(g for g in c.call('hair_list')['groups'] if g['group_type'].endswith('.HairFront'))
        strand = next(s for s in group['strands'] if s['visible'])
        for visible in ('false', 'true'):
            result = raw(f'hair_visibility {strand["strand_id"]} {visible} {revision()}')
            assert result['visible'] == (visible == 'true') and result['stable_preview_hash']
            frame = preview(key)
            assert (sha(frame) == sha(before)) == (visible == 'true')
        assert hair() == baseline
        record('visibility_exact_geometry_and_render_restore')

        def group_edit(command):
            result = raw(command + ' ' + str(revision()))
            assert result['groups_modified'] > 0 and result['stable_preview_hash']
            assert Path(result['checkpoint_path']).is_file()
            return result

        def recover(result):
            c.call('main_menu')
            c.call('open', path=result['checkpoint_path'])
            assert hair() == baseline
            preview(key)

        offset = group_edit(f'hair_offset_group {group["group_id"]} .01')
        shifted = hair()
        assert abs(shifted[group['group_id']]['parameters']['offset_x'] -
                   baseline[group['group_id']]['parameters']['offset_x'] - .01) < 1e-6
        assert all(shifted[k] == v for k, v in baseline.items() if k != group['group_id'])
        recover(offset)
        record('group_offset_readback_scoping_and_exact_checkpoint_recovery')
        mirror = group_edit('hair_mirror_front')
        assert hair() != baseline
        mirrored = hair()
        assert all(mirrored[k] == v for k, v in baseline.items() if not v['type'].endswith('.HairFront'))
        recover(mirror)
        record('front_mirror_scoping_and_exact_checkpoint_recovery')
        before_scale = hair()
        scale = group_edit('hair_scale_back 1.02')
        scaled = hair()
        count = 0
        for group_id, data in before_scale.items():
            params = data['parameters']
            if not params:
                continue
            factor = 1.02 if data['type'].endswith('.HairBack') and params['guide_height'] >= .6 else 1
            assert abs(scaled[group_id]['parameters']['guide_height'] - params['guide_height'] * factor) < 1e-5
            count += factor != 1
        assert count > 0
        record('long_guide_scale_readback_and_crown_exclusion')

        c.call('save', path=str(root / 'roundtrip.vroid'))
        c.call('main_menu')
        c.call('open', path=str(root / 'roundtrip.vroid'))
        assert hair() == scaled
        assert raw('expression_list')['categories'] == expressions['categories']
        (root / 'reopened.png').write_bytes(preview(key))
        record('expression_and_hair_save_reopen')
        recover(scale)
        record('guide_scale_exact_checkpoint_recovery')

        c.call('main_menu')
        c.call('open', path=str(Path(args.model).resolve()))
        assert hair() == baseline
        assert sha(preview(key)) == sha(before)
        c.call('save', path=str(root / 'verified.vroid'))
        record('original_checkpoint_exact_recovery')
        passed = True
    except Exception as exc:
        error = str(exc)
        raise
    finally:
        (root / 'report.json').write_text(json.dumps({'ok': passed, 'error': error, 'checks': checks, 'count': len(checks)}, indent=2))
        print(root / 'report.json', flush=True)


if __name__ == '__main__':
    main()
