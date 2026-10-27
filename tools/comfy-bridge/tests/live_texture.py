"""Destructive-to-session integration test: use a dedicated VRoid instance at its menu.

Creates its own model, never overwrites a user's model; leaves the test model open.
"""
import argparse
import json
from pathlib import Path
import sys
import tempfile
import time
import uuid
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from vroid_client import VroidClient
from image_validation import BridgeError, decode, encode, mock_edit, sha


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--url',default='http://127.0.0.1:17891/mcp')
    args=parser.parse_args()
    c=VroidClient(args.url);c.initialize()
    if c.call('state')['in_edit']:
        raise RuntimeError('Use a dedicated app at the model selection screen; test will not discard the current session')
    c.call('create',gender='female')
    listing=c.call('texture_targets')
    root=Path(listing['workspace_root'])/'tests'/uuid.uuid4().hex;root.mkdir(parents=True)
    target=next(t for t in listing['targets'] if t['group_type'].endswith('.Iris'))
    layer=c.call('texture_layers',target_id=target['target_id'])['layers'][0]
    a=dict(target_id=target['target_id'],layer_id=layer['layer_id'])
    export=c.call('texture_export',**a,output_path=str(root/'source.png'))
    c.call('camera',action='frame',region='face',angle='front')
    before=c.stable_preview();(root/'before.png').write_bytes(before)
    results={}
    same=c.call('texture_import',**a,input_path=export['texture_path'],expected_revision=export['revision'])
    assert c.call('texture_layer_info',**a)['pixel_sha256']==export['pixel_sha256']
    assert sha(c.stable_preview())==sha(before)
    results['unchanged_roundtrip']=True
    original=(root/'source.png').read_bytes()
    backup=Path(same['backup_path']);backup_original=backup.read_bytes()
    try:
        backup.write_bytes(mock_edit(backup_original))
        try:c.call('texture_restore',backup_id=same['backup_id'],expected_revision=c.call('texture_layer_info',**a)['revision'])
        except BridgeError:results['tampered_backup_rejected']=True
        else:raise AssertionError('Modified backup was accepted')
    finally:backup.write_bytes(backup_original)
    start=time.monotonic()
    try:c.call('save',path=tempfile.gettempdir())
    except BridgeError:pass
    else:raise AssertionError('Directory save accepted')
    assert time.monotonic()-start<5;results['save_directory_fast']=True
    (root/'invalid.png').write_bytes(b'invalid')
    (root/'truncated.png').write_bytes(original[:80])
    (root/'small.png').write_bytes(encode(decode(original).resize((8,8))))
    changed_alpha=decode(original);changed_alpha.putalpha(255);(root/'alpha.png').write_bytes(encode(changed_alpha))
    guards=[('invalid_png',dict(input_path=str(root/'invalid.png'))),
            ('truncated_png',dict(input_path=str(root/'truncated.png'))),
            ('missing_file',dict(input_path=str(root/'missing.png'))),
            ('dimensions',dict(input_path=str(root/'small.png'))),
            ('alpha',dict(input_path=str(root/'alpha.png'))),
            ('escape',dict(input_path='../outside.png')),
            ('stale_revision',dict(expected_revision=-1)),
            ('unknown_layer',dict(layer_id='not-a-layer')),
            ('stale_target',dict(target_id='old-session/invalid'))]
    for label,overrides in guards:
        info=c.call('texture_layer_info',**a)
        call=dict(**a,input_path=export['texture_path'],expected_revision=info['revision']);call.update(overrides)
        try:c.call('texture_import',**call)
        except BridgeError:pass
        else:raise AssertionError(label+' unexpectedly succeeded')
        assert c.call('texture_layer_info',**a)['pixel_sha256']==export['pixel_sha256']
        assert c.call('state')['ready']
        c.rpc('ping');results[label]=True
    modified=root/'modified.png';modified.write_bytes(mock_edit(original))
    imported=c.call('texture_import',**a,input_path=str(modified),expected_revision=c.call('texture_layer_info',**a)['revision'])
    after=c.stable_preview();(root/'after.png').write_bytes(after);assert sha(after)!=sha(before)
    results['render_changed']=True
    saved=root/'modified.vroid';c.call('save',path=str(saved))
    restored=c.call('texture_restore',backup_id=imported['backup_id'],expected_revision=c.call('texture_layer_info',**a)['revision'])
    assert c.call('texture_layer_info',**a)['pixel_sha256']==export['pixel_sha256']
    assert sha(c.stable_preview())==sha(before);results['exact_restore']=True
    c.call('main_menu');c.call('open',path=str(saved))
    try:c.call('texture_layer_info',**a)
    except BridgeError:results['session_stale_id']=True
    else:raise AssertionError('Old session ID accepted')
    t=next(t for t in c.call('texture_targets')['targets'] if t['group_type'].endswith('.Iris'))
    l=c.call('texture_layers',target_id=t['target_id'])['layers'][0]
    info=c.call('texture_layer_info',target_id=t['target_id'],layer_id=l['layer_id'])
    assert info['pixel_sha256']==imported['pixel_sha256'];results['save_reopen']=True
    c.call('camera',action='frame',region='face',angle='front')
    (root/'reopened.png').write_bytes(c.stable_preview())
    old=dict(target_id=t['target_id'],layer_id=l['layer_id'])
    category=c.call('categories',section='Face')['categories'][0]['name']
    c.call('select_preset',section='Face',category=category,index=0)
    try:c.call('texture_layer_info',**old)
    except BridgeError:results['preset_stale_id']=True
    else:raise AssertionError('Preset replacement did not invalidate target ID')
    report=dict(ok=True,checks=results,artifact_dir=str(root))
    (root/'result.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))


if __name__=='__main__':main()
