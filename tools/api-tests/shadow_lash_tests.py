"""Live stock face-paint preparation, eyelash and eyeshadow texture verification.

Requires a dedicated app at its menu and a saved fixture with nonempty FacePaint/Eyelash presets.
Each deterministic fixture modifies only the left atlas half; this is not image generation.
"""
import argparse
import json
from pathlib import Path
import sys
import uuid
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'comfy-bridge'))
from PIL import Image, ImageFilter, ImageChops
from image_validation import BridgeError, decode, encode, sha
from vroid_client import VroidClient

FACE_PAINT = 'TransferableGroupType.N00.Level1.FacePaint'


def candidate(source, kind):
    im = decode(source); out = im.copy()
    half = im.crop((0, 0, im.width//2, im.height)); a = half.getchannel('A')
    if not a.getbbox(): raise ValueError('Fixture must have visible pixels in the first atlas half')
    if kind == 'Eyelash':
        a = a.filter(ImageFilter.MaxFilter(5))
        part = Image.new('RGBA', half.size, (25, 20, 30, 0)); part.putalpha(a)
    else:
        part = Image.new('RGBA', half.size, (126, 52, 174, 0))
        part.putalpha(a.point(lambda x: min(255, round(x*1.4))))
    out.paste(part, (0, 0))
    assert out.crop((im.width//2,0,im.width,im.height)).tobytes() == im.crop((im.width//2,0,im.width,im.height)).tobytes()
    assert out.getchannel('A').tobytes() != im.getchannel('A').tobytes()
    return encode(out)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model', required=True)
    args=parser.parse_args()
    c=VroidClient('http://127.0.0.1:17891/mcp');c.initialize()
    if c.call('state')['in_edit']: raise RuntimeError('Refusing to discard an existing session')
    pre_checks={}
    for state in ('no_model', 'missing_preset'):
        if state == 'missing_preset': c.call('create',gender='female')
        rev = -1 if state == 'no_model' else c.call('texture_targets')['revision']
        try:c.call('texture_prepare',group_type=FACE_PAINT,expected_revision=rev)
        except BridgeError as e:
            assert ('NOT_IN_EDIT_MODE' if state == 'no_model' else 'UNKNOWN_TEXTURE_TARGET') in str(e),str(e)
        else:raise AssertionError(state+' unexpectedly accepted')
        pre_checks['prepare_'+state]=True;print('PASS','prepare_'+state,flush=True)
    c.call('main_menu')
    c.call('open',path=str(Path(args.model).resolve()))
    listing=c.call('texture_targets');root=Path(listing['workspace_root'])/'tests'/('shadow-lash-'+uuid.uuid4().hex);root.mkdir()
    checks=pre_checks; results={}
    def ok(name): checks[name]=True;print('PASS',name,flush=True)
    def reject(name, tool, code, **kw):
        try:c.call(tool,**kw)
        except BridgeError as e:assert code in str(e),str(e)
        else:raise AssertionError(name+' accepted')
        ok(name)
    def resolve(kind):
        t=next(t for t in c.call('texture_targets',section='Face')['targets'] if t['group_type'].endswith('.'+kind))
        layers=c.call('texture_layers',target_id=t['target_id'])['layers']
        assert len(layers)==1,'This fixture expects one layer'
        return dict(target_id=t['target_id'],layer_id=layers[0]['layer_id'])
    def layers():
        result=[]
        for t in c.call('texture_targets')['targets']:
            for l in c.call('texture_layers',target_id=t['target_id'])['layers']:
                key=dict(target_id=t['target_id'],layer_id=l['layer_id'])
                result.append((key,c.call('texture_layer_info',**key)['pixel_sha256']))
        return result
    try:
        c.call('camera',action='frame',region='face',angle='front')
        original=c.stable_preview();(root/'before-prepare.png').write_bytes(original)
        pre_layers=layers();rev=c.call('texture_targets')['revision']
        reject('prepare_bad_group','texture_prepare','PREPARATION_NOT_SUPPORTED',group_type='invalid',expected_revision=rev)
        reject('prepare_stale_revision','texture_prepare','STALE_TEXTURE_TARGET',group_type=FACE_PAINT,expected_revision=-1)
        prepared=c.call('texture_prepare',group_type=FACE_PAINT,expected_revision=rev)
        assert Path(prepared['checkpoint_path']).is_file();ok('preparation_checkpoint')
        (root/'prepare.json').write_text(json.dumps(prepared,indent=2))
        after_prepare=c.stable_preview();(root/'after-prepare.png').write_bytes(after_prepare)
        assert sha(after_prepare)==sha(original);ok('preparation_preserves_render')
        post_layers=layers()
        # Compare stable engine image/layer identity, excluding the invalidated session prefix.
        before_map={(k['target_id'].split('/',1)[1],k['layer_id']):h for k,h in pre_layers}
        after_map={(k['target_id'].split('/',1)[1],k['layer_id']):h for k,h in post_layers}
        assert all(after_map.get(k)==h for k,h in before_map.items());ok('preparation_preserves_other_layers')
        reject('preparation_invalidates_ids','texture_layer_info','STALE_TEXTURE_TARGET',**pre_layers[0][0])
        twice=c.call('texture_prepare',group_type=FACE_PAINT,expected_revision=prepared['revision'])
        assert twice['already_prepared'] and twice['revision']==prepared['revision'] and twice['checkpoint_path'] is None
        ok('preparation_idempotent')
        baseline=root/'prepared-baseline.vroid';c.call('save',path=str(baseline))
        # The automatic checkpoint also recovers the pre-conversion structure.
        c.call('main_menu');c.call('open',path=prepared['checkpoint_path'])
        assert not any(t['group_type']==FACE_PAINT for t in c.call('texture_targets')['targets'])
        c.call('camera',action='frame',region='face',angle='front');assert sha(c.stable_preview())==sha(original)
        ok('checkpoint_reopens_original_structure_and_render')
        c.call('main_menu');c.call('open',path=str(baseline));resolve('FacePaint');ok('prepared_layer_persists')
        for kind in ('Eyelash','FacePaint'):
            a=resolve(kind);p=root/kind;p.mkdir()
            c.call('camera',action='frame',region='face',angle='front');before=c.stable_preview();(p/'before.png').write_bytes(before)
            source=c.call('texture_export',**a,output_path=str(p/'source.png'))
            new=candidate((p/'source.png').read_bytes(),kind);(p/'candidate.png').write_bytes(new)
            snapshot=layers()
            reject(kind+'_alpha_default_guard','texture_import','alpha must match',**a,input_path=str(p/'candidate.png'),expected_revision=source['revision'])
            assert c.call('texture_layer_info',**a)['pixel_sha256']==source['pixel_sha256']
            r=c.call('texture_import',**a,input_path=str(p/'candidate.png'),expected_revision=source['revision'],alpha_mode='replace')
            c.call('texture_export',**a,output_path=str(p/'readback.png'))
            assert decode((p/'readback.png').read_bytes()).tobytes()==decode(new).tobytes();ok(kind+'_exact_rgba')
            assert all(c.call('texture_layer_info',**key)['pixel_sha256']==h for key,h in snapshot if key!=a);ok(kind+'_other_layers_unchanged')
            for angle in ('front','right','left'):
                c.call('camera',action='frame',region='face',angle=angle);(p/('after-'+angle+'.png')).write_bytes(c.stable_preview())
            diff=ImageChops.difference(decode(before).convert('RGB'),decode((p/'after-front.png').read_bytes()).convert('RGB')).getbbox()
            assert diff is not None;ok(kind+'_visible_multiview')
            saved=p/'edited.vroid';c.call('save',path=str(saved))
            c.call('camera',action='frame',region='face',angle='front')
            restored=c.call('texture_restore',backup_id=r['backup_id'],expected_revision=c.call('texture_layer_info',**a)['revision'])
            assert c.call('texture_layer_info',**a)['pixel_sha256']==source['pixel_sha256']
            assert sha(c.stable_preview())==sha(before);ok(kind+'_exact_restore')
            c.call('texture_restore',backup_id=restored['backup_id'],expected_revision=c.call('texture_layer_info',**a)['revision'])
            assert c.call('texture_layer_info',**a)['pixel_sha256']==r['pixel_sha256'];ok(kind+'_reverse_restore')
            c.call('main_menu');c.call('open',path=str(saved));a=resolve(kind)
            assert c.call('texture_layer_info',**a)['pixel_sha256']==r['pixel_sha256'];ok(kind+'_save_reopen_rgba')
            c.call('camera',action='frame',region='face',angle='front');reopened=c.stable_preview();(p/'reopened.png').write_bytes(reopened)
            assert sha(reopened)==sha((p/'after-front.png').read_bytes());ok(kind+'_save_reopen_render')
            results[kind]=dict(model=str(saved),render_diff_bbox=diff,pixel_sha256=r['pixel_sha256'])
            c.call('main_menu');c.call('open',path=str(baseline))
        # Combine both already-verified edits on the prepared baseline.
        for kind in ('Eyelash','FacePaint'):
            a=resolve(kind);c.call('texture_import',**a,input_path=str(root/kind/'candidate.png'),
                expected_revision=c.call('texture_layer_info',**a)['revision'],alpha_mode='replace')
        combined=root/'shadow-and-lashes.vroid';c.call('save',path=str(combined))
        c.call('main_menu');c.call('open',path=str(combined))
        assert all(c.call('texture_layer_info',**resolve(kind))['pixel_sha256']==r['pixel_sha256'] for kind,r in results.items())
        c.call('camera',action='frame',region='face',angle='front');(root/'combined-front.png').write_bytes(c.stable_preview());ok('combined_save_reopen')
        result=dict(ok=True,checks=checks,targets=results,artifacts=str(root),model=str(combined))
    except Exception as e:
        result=dict(ok=False,checks=checks,artifacts=str(root),error=str(e));raise
    finally:
        (root/'result.json').write_text(json.dumps(result,indent=2));print(json.dumps(result),flush=True)


if __name__=='__main__':main()
