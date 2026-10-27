"""Live alpha-changing eyeliner acceptance test on a new, dedicated test model.

Uses a deterministic one-sided fixture, not an AI-generated cosmetic design.
Requires the app at its model-selection screen; leaves a saved test model open.
"""
import argparse
import json
from pathlib import Path
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'comfy-bridge'))
from vroid_client import VroidClient
from image_validation import BridgeError, decode, encode, sha
from PIL import Image, ImageDraw, ImageFilter, ImageChops


def eyeliner_fixture(source):
    original = decode(source)
    if original.size != (1024, 256):
        raise ValueError('Fixture is calibrated only for the stock 1024x256 eyeline atlas')
    result = original.copy()
    half = original.crop((0, 0, 512, 256))
    alpha = half.getchannel('A').filter(ImageFilter.MaxFilter(11))
    # Stock atlas outer tip: extend only this test eye, keeping the other half byte-identical.
    wing = Image.new('L', half.size)
    ImageDraw.Draw(wing).polygon([(112, 100), (78, 70), (124, 120)], fill=255)
    alpha = ImageChops.lighter(alpha, wing)
    pixels, old_alpha, new_alpha = half.load(), half.getchannel('A').load(), alpha.load()
    for y in range(half.height):
        for x in range(half.width):
            if new_alpha[x, y] > old_alpha[x, y]:
                pixels[x, y] = (35, 28, 30, new_alpha[x, y])
    result.paste(half, (0, 0))
    return encode(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--url', default='http://127.0.0.1:17891/mcp')
    args = parser.parse_args()
    c = VroidClient(args.url); c.initialize()
    if c.call('state')['in_edit']:
        raise RuntimeError('Refusing to discard an open model; use a dedicated app at its menu')
    c.call('create', gender='female')
    targets = c.call('texture_targets', section='Face')
    root = Path(targets['workspace_root']) / 'tests' / ('makeup-' + uuid.uuid4().hex)
    root.mkdir(parents=True)
    checks = {}

    def record(name):
        checks[name] = True; print('PASS', name, flush=True)

    def rejected(name, code, tool='texture_import', **kw):
        try: c.call(tool, **kw)
        except BridgeError as exc: assert code in str(exc), str(exc)
        else: raise AssertionError(name + ': unexpected success')
        record(name)

    def resolve():
        ts = c.call('texture_targets', section='Face')['targets']
        t = next(t for t in ts if t['group_type'].endswith('.Eyeline'))
        l = c.call('texture_layers', target_id=t['target_id'])['layers'][0]
        return dict(target_id=t['target_id'], layer_id=l['layer_id'])

    a = resolve()
    def info(): return c.call('texture_layer_info', **a)
    def restore(r): return c.call('texture_restore', backup_id=r['backup_id'], expected_revision=info()['revision'])

    try:
        exported = c.call('texture_export', **a, output_path=str(root / 'source.png'))
        source = (root / 'source.png').read_bytes()
        candidate = eyeliner_fixture(source)
        (root / 'candidate.png').write_bytes(candidate)
        src, new = decode(source), decode(candidate)
        assert src.getchannel('A').tobytes() != new.getchannel('A').tobytes()
        assert src.crop((512, 0, 1024, 256)).tobytes() == new.crop((512, 0, 1024, 256)).tobytes()
        record('one_sided_alpha_fixture')
        others = []
        for target in targets['targets']:
            for layer in c.call('texture_layers', target_id=target['target_id'])['layers']:
                key = dict(target_id=target['target_id'], layer_id=layer['layer_id'])
                if key != a: others.append((key, c.call('texture_layer_info', **key)['pixel_sha256']))
        c.call('camera', action='frame', region='face', angle='front')
        before = c.stable_preview(); (root / 'before-front.png').write_bytes(before)
        c.call('save', path=str(root / 'baseline.vroid'))
        base = dict(**a, input_path=str(root / 'candidate.png'), expected_revision=info()['revision'])
        for mode in (None, 'preserve'):
            extra = {} if mode is None else {'alpha_mode': mode}
            rejected('alpha_rejected_' + str(mode), 'alpha must match', **base, **extra)
        for mode in ('invalid', 'Replace', True):
            rejected('bad_mode_' + str(mode), 'INVALID_ALPHA_MODE', **base, alpha_mode=mode)
        (root / 'invalid.png').write_bytes(b'not a png')
        (root / 'small.png').write_bytes(encode(new.resize((16, 16))))
        for name, override, code in [
            ('replace_invalid_png', {'input_path': str(root/'invalid.png')}, 'INVALID_PNG'),
            ('replace_wrong_dimensions', {'input_path': str(root/'small.png')}, 'INVALID_PNG_DIMENSIONS'),
            ('replace_stale_revision', {'expected_revision': -1}, 'STALE_TEXTURE_TARGET'),
            ('replace_path_escape', {'input_path': '../outside.png'}, 'PATH_NOT_ALLOWED'),
        ]:
            rejected(name, code, **(base | override), alpha_mode='replace')
        assert info()['pixel_sha256'] == exported['pixel_sha256']
        assert info()['revision'] == base['expected_revision']
        assert sha(c.stable_preview()) == sha(before)
        record('guards_leave_model_unchanged')
        applied = c.call('texture_import', **base, alpha_mode='replace')
        assert applied['alpha_mode'] == 'replace'
        after_info = c.call('texture_export', **a, output_path=str(root / 'readback.png'))
        assert decode((root/'readback.png').read_bytes()).tobytes() == new.tobytes()
        assert after_info['pixel_sha256'] == applied['pixel_sha256']
        record('replace_exact_rgba_readback')
        assert all(c.call('texture_layer_info', **key)['pixel_sha256'] == h for key, h in others)
        record('other_layers_unchanged')
        for angle in ('front', 'right', 'left'):
            c.call('camera', action='frame', region='face', angle=angle)
            preview = c.stable_preview(); (root / ('after-'+angle+'.png')).write_bytes(preview)
            if angle == 'front': assert sha(preview) != sha(before)
        record('stable_multiview_render_changed')
        c.call('camera', action='frame', region='face', angle='front')
        model = root / 'one-eye-liner.vroid'; c.call('save', path=str(model))
        reversed_edit = restore(applied)
        assert reversed_edit['alpha_mode'] == 'restore'
        assert info()['pixel_sha256'] == exported['pixel_sha256']
        assert sha(c.stable_preview()) == sha(before)
        record('exact_alpha_rgb_render_restore')
        redo = restore(reversed_edit)
        assert info()['pixel_sha256'] == applied['pixel_sha256']
        assert sha(c.stable_preview()) == sha((root/'after-front.png').read_bytes())
        record('restore_reversible_with_changed_alpha')
        backup = Path(redo['backup_path']); original_backup = backup.read_bytes()
        try:
            backup.write_bytes(candidate)
            rejected('tampered_backup_rejected', 'no longer match', tool='texture_restore',
                     backup_id=redo['backup_id'], expected_revision=info()['revision'])
        finally: backup.write_bytes(original_backup)
        restore(redo)
        c.call('main_menu'); c.call('open', path=str(model)); a = resolve()
        assert info()['pixel_sha256'] == applied['pixel_sha256']
        record('save_reopen_exact_rgba')
        c.call('camera', action='frame', region='face', angle='front')
        reopened = c.stable_preview(); (root/'reopened-front.png').write_bytes(reopened)
        assert sha(reopened) == sha((root/'after-front.png').read_bytes())
        record('save_reopen_exact_render')
        rejected('old_session_id_rejected', 'STALE_TEXTURE_TARGET', **(base | {'expected_revision': info()['revision']}), alpha_mode='replace')
        result = dict(ok=True, checks=checks, artifacts=str(root), model=str(model), import_result=applied)
    except Exception as exc:
        result = dict(ok=False, checks=checks, artifacts=str(root), error=str(exc))
        raise
    finally:
        (root/'result.json').write_text(json.dumps(result, indent=2))
        print(json.dumps(result), flush=True)


if __name__ == '__main__': main()
