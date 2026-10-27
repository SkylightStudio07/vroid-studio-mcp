#!/usr/bin/env python3
"""VRoid editable layer -> ComfyUI -> validated layer -> stable 3D previews."""
import argparse
import json
import signal
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path
from comfy_client import ComfyClient
from vroid_client import VroidClient
from image_validation import BridgeError, workspace_path, read_png, prepare, mock_edit, sha


def write_new(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(data)


def roundtrip(config, args):
    allow_alpha_change = getattr(args, 'allow_alpha_change', False)
    if allow_alpha_change and args.restore_source_alpha:
        raise BridgeError('INVALID_ALPHA_MODE', 'Alpha restoration and replacement are mutually exclusive')
    root = Path(config['workspace_root']).resolve()
    run = workspace_path(root, 'runs/' + uuid.uuid4().hex)
    run.mkdir(parents=True)
    manifest_path = run / 'manifest.json'
    manifest = {'started':datetime.now(timezone.utc).isoformat(), 'status':'started',
                'mock_comfy':args.mock_comfy, 'applied':False, 'target_id':args.target_id, 'layer_id':args.layer_id}
    cancelled = [False]
    previous_handler = signal.signal(signal.SIGINT, lambda *_: cancelled.__setitem__(0, True))
    comfy = ComfyClient(config['comfyui_url'], config.get('comfyui_timeout_seconds',900))
    client = VroidClient(config['vroid_mcp_url'])
    applied = None

    def save_manifest():
        temp = manifest_path.with_suffix('.tmp')
        temp.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
        temp.replace(manifest_path)

    def is_cancelled():
        return cancelled[0] or bool(args.cancel_file and workspace_path(root,args.cancel_file).exists())

    def submitted(prompt_id):
        manifest['prompt_id'] = prompt_id
        save_manifest()

    target = {'target_id':args.target_id, 'layer_id':args.layer_id}
    try:
        client.initialize()
        listing = client.call('texture_targets')
        if Path(listing['workspace_root']).resolve() != root:
            raise BridgeError('PATH_NOT_ALLOWED', 'Bridge workspace_root must equal the plugin texture_workspace')
        exported = client.call('texture_export', **target, output_path=str(run/'source.png'))
        manifest['source'] = exported
        source = read_png(workspace_path(root, exported['texture_path']))
        client.call('camera', action='frame',region=args.region,angle='front')
        before = client.stable_preview()
        write_new(run/'before-front.png', before)
        if args.mock_comfy:
            generated = mock_edit(source)
        else:
            if not args.workflow:
                raise BridgeError('COMFYUI_WORKFLOW_INVALID', '--workflow is required without --mock-comfy')
            graph = json.loads(Path(args.workflow).read_text(encoding='utf-8-sig'))
            # Validate bindings before uploading anything to the shared service.
            values = {'source_texture':'pending.png', 'positive_prompt':args.prompt,
                      'reference_image':'pending.png' if args.reference_image else None,
                      'mask':'pending.png' if args.mask else None}
            ComfyClient.bind(graph, config.get('bindings',{}), values)
            comfy.health()
            values['source_texture'] = comfy.upload(run/'source.png')
            for name, path in (('reference_image',args.reference_image),('mask',args.mask)):
                if path:
                    values[name] = comfy.upload(workspace_path(root,path))
            graph = ComfyClient.bind(graph, config.get('bindings',{}), values)
            write_new(run/'submitted-workflow.json', json.dumps(graph,indent=2).encode())
            generated = comfy.run(graph, str(config['output_node']), is_cancelled, submitted)
        write_new(run/'generated.png', generated)
        candidate = prepare(source,generated,args.restore_source_alpha,allow_alpha_change)
        write_new(run/'candidate.png',candidate)
        manifest.update(candidate_sha256=sha(candidate), restore_source_alpha=args.restore_source_alpha,
                        alpha_mode='replace' if allow_alpha_change else 'preserve',
                        status='preview_only', manifest_path=str(manifest_path))
        if is_cancelled():
            raise BridgeError('CANCELLED','Cancelled before import')
        if args.apply:
            manifest['status'] = 'import_submitted'
            manifest['import_attempted'] = True
            save_manifest()
            applied = client.call('texture_import', **target, input_path=str(run/'candidate.png'),
                                  expected_revision=exported['revision'], alpha_mode=manifest['alpha_mode'])
            manifest.update(applied=True, import_result=applied, status='applied')
            save_manifest()
            after = client.stable_preview()
            write_new(run/'after-front.png',after)
            manifest['render_changed'] = sha(before) != sha(after)
            for angle in ('right','back'):
                client.call('camera',action='frame',region=args.region,angle=angle)
                write_new(run/f'after-{angle}.png',client.stable_preview())
            client.call('camera',action='frame',region=args.region,angle='front')
            if args.save_as:
                model = workspace_path(root,args.save_as)
                if model.exists() or model.suffix.lower() != '.vroid':
                    raise BridgeError('PATH_NOT_ALLOWED','save-as requires a new .vroid path inside the workspace')
                model.parent.mkdir(parents=True,exist_ok=True)
                manifest['saved_model'] = client.call('save',path=str(model))
        save_manifest()
        return manifest
    except Exception as exc:
        manifest.update(status='failed',error={'code':getattr(exc,'code','BRIDGE_ERROR'),'message':str(exc)})
        if manifest.get('import_attempted') and applied is None:
            manifest['application_state'] = 'unconfirmed; inspect VRoid status and layer_info before retrying'
        if applied:
            try:
                current = client.call('texture_layer_info',**target)
                if current['revision'] != applied['revision']:
                    raise BridgeError('STALE_TEXTURE_TARGET','Concurrent edit detected; automatic restore skipped')
                manifest['rollback'] = client.call('texture_restore',backup_id=applied['backup_id'],expected_revision=current['revision'])
                manifest['applied'] = False
            except Exception as rollback:
                manifest['rollback_error'] = str(rollback)
        save_manifest()
        raise BridgeError(getattr(exc,'code','BRIDGE_ERROR'), f'{exc}; manifest: {manifest_path}') from exc
    finally:
        signal.signal(signal.SIGINT, previous_handler)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command',choices=['healthcheck','targets','texture-roundtrip'])
    parser.add_argument('--config',required=True)
    parser.add_argument('--target-id')
    parser.add_argument('--layer-id')
    parser.add_argument('--workflow')
    parser.add_argument('--reference-image')
    parser.add_argument('--mask')
    parser.add_argument('--prompt')
    parser.add_argument('--mock-comfy',action='store_true')
    parser.add_argument('--apply',action='store_true',help='Explicitly approve importing the validated candidate; default is preview only')
    alpha = parser.add_mutually_exclusive_group()
    alpha.add_argument('--restore-source-alpha',action='store_true')
    alpha.add_argument('--allow-alpha-change',action='store_true',help='Explicitly allow generated transparency/silhouette changes; original RGBA is backed up')
    parser.add_argument('--cancel-file',help='Stop before import when this workspace file exists; Ctrl+C also cancels')
    parser.add_argument('--region',default='face',choices=['face','body','head','upper','legs','feet'])
    parser.add_argument('--save-as',help='Optional new .vroid path inside workspace_root')
    args = parser.parse_args()
    try:
        config_path = Path(args.config).resolve()
        config = json.loads(config_path.read_text(encoding='utf-8-sig'))
        config['workspace_root'] = str((config_path.parent / config['workspace_root']).resolve())
        client = VroidClient(config['vroid_mcp_url'])
        if args.command == 'healthcheck':
            result = {}
            for name, check in [('vroid',lambda: (client.initialize(),client.call('state'))[1]),
                                ('comfyui',lambda:ComfyClient(config['comfyui_url']).health())]:
                try:
                    result[name] = {'ok':True,'result':check()}
                except BridgeError as exc:
                    result[name] = {'ok':False,'code':exc.code,'error':str(exc)}
            print(json.dumps(result,indent=2))
            return 0 if all(v['ok'] for v in result.values()) else 1
        if args.command == 'targets':
            client.initialize()
            result = client.call('texture_targets')
            for target in result['targets']:
                target['layers'] = client.call('texture_layers',target_id=target['target_id'])['layers']
        else:
            if not args.target_id or not args.layer_id:
                parser.error('texture-roundtrip requires --target-id and --layer-id from targets')
            result = roundtrip(config,args)
        print(json.dumps(result,indent=2))
        return 0
    except (BridgeError,OSError,ValueError,KeyError) as exc:
        print(json.dumps({'ok':False,'code':getattr(exc,'code','CONFIG_ERROR'),'error':str(exc)}),file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
