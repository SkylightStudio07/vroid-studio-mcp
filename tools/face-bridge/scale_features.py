"""Scale the captured eyes and brows of a same-source face bridge package.

This changes geometry only. Source fingerprints, captured neutral offsets, mesh
topology, textures and non-eye additions remain intact.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import numpy as np


def scale_package(package, captures, eye_scale, brow_scale, brow_lift, eye_lift=0):
    result = json.loads(json.dumps(package))
    face = captures['neutral']
    topology = bytearray(struct.pack('<i', len(face['vertices'])))
    topology.extend(np.array(face['uv'], dtype='<f4').tobytes())
    topology.extend(struct.pack('<i', len(face['submeshes'])))
    for sub in face['submeshes']:
        topology.extend(struct.pack('<i', len(sub['triangles'])))
        topology.extend(np.array(sub['triangles'], dtype='<i4').tobytes())
    if hashlib.sha256(topology).hexdigest() != package['topology_sha256']:
        raise ValueError('Captured source topology differs from package')
    expected_neutral = np.array(package['reference_rest_world']) + np.array(package['reference_neutral_offset'])
    if np.max(abs(np.array(face['vertices']) - expected_neutral)) > 2e-6:
        raise ValueError('Captured neutral geometry differs from package source')
    if any(len(v['vertices']) != package['vertex_count'] or v['uv'] != face['uv'] or v['submeshes'] != face['submeshes'] for v in captures.values()):
        raise ValueError('Captured poses do not share source vertex/UV/triangle correspondence')
    original_open = np.array(face['vertices']) + np.array(package['delta_open'])
    materials = {s['material']: np.array(sorted(set(s['triangles'])), dtype=int)
                 for s in face['submeshes']}
    find = lambda suffix: next(v for k, v in materials.items() if suffix in k)
    eyes = np.concatenate([find(s) for s in ('EyeIris_', 'EyeHighlight_', 'EyeWhite_', 'FaceEyelash_', 'FaceEyeline_')])
    brows, skin = find('FaceBrow_'), find('Face_00_SKIN')
    whites = find('EyeWhite_')
    centers, brow_centers = {}, {}
    for side in (-1, 1):
        p = original_open[whites]
        p = p[p[:, 0] * side > 0]
        centers[side] = (p.min(0) + p.max(0)) / 2
        p = original_open[brows]
        p = p[p[:, 0] * side > 0]
        brow_centers[side] = (p.min(0) + p.max(0)) / 2

    def transform(points, kind):
        points = np.array(points, dtype=float)
        changed = points.copy()
        for side in (-1, 1):
            mask = points[:, 0] * side > 0
            center = brow_centers[side] if kind == 'brow' else centers[side]
            xy = points[mask, :2] - center[:2]
            scale = brow_scale if kind == 'brow' else eye_scale
            delta = xy * (np.array(scale) - 1)
            if kind == 'brow':
                delta[:, 1] += brow_lift
            else:
                delta[:, 1] += eye_lift
            if kind == 'skin':
                # The aperture is fully transformed; distant cheek/nose/scalp
                # vertices receive a smooth, compactly supported transition.
                distance = abs(xy)
                t = np.clip((distance - [.029, .023]) / [.021, .022], 0, 1)
                weights = (1 - t * t * (3 - 2 * t)).prod(axis=1)
                depth = np.clip((points[mask, 2] - .012) / .018, 0, 1)
                delta *= (weights * depth)[:, None]
            changed[mask, :2] += delta
        return changed

    metrics = {}
    for state, key in [('neutral', 'open'), ('half', 'half'), ('closed', 'closed')]:
        source = np.array(captures[state]['vertices'])
        old = source + np.array(package['delta_' + key])
        new = old.copy()
        new[eyes] = transform(old[eyes], 'eye')
        new[brows] = transform(old[brows], 'brow')
        new[skin] = transform(old[skin], 'skin')
        delta = np.round(new - source, 9)
        if abs(delta).max() > .03:
            raise ValueError('Scaled face exceeds the existing 30 mm package displacement guard')
        result['delta_' + key] = delta.tolist()
        metrics[key] = {'changed_vertices': int((np.linalg.norm(new - old, axis=1) > 1e-8).sum()),
                        'maximum_change_m': float(np.linalg.norm(new - old, axis=1).max()),
                        'maximum_source_delta_component_m': float(abs(delta).max())}

    preserved = []
    for old, new in zip(package['additions'], result['additions']):
        if old['name'].startswith('Asuna.InnerHair.'):
            preserved.append(old['name'])
            continue
        if not old['name'].startswith(('Crease_', 'Lash', 'LowerOuter_', 'OuterCorner_', 'UpperLiner_')):
            raise ValueError('Unclassified addition: ' + old['name'])
        for key in ('base_world', 'open_world', 'half_world', 'closed_world'):
            new[key] = np.round(transform(old[key], 'eye'), 9).tolist()

    return result, {'eye_scale_xy': list(eye_scale), 'brow_scale_xy': list(brow_scale),
                    'brow_lift_m': brow_lift, 'eye_lift_m': eye_lift,
                    'eye_centers_world': {str(k): v.tolist() for k, v in centers.items()},
                    'brow_centers_world': {str(k): v.tolist() for k, v in brow_centers.items()},
                    'preserved_additions': preserved, 'pose_changes': metrics}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--eye-x', type=float, default=1.18)
    parser.add_argument('--eye-y', type=float, default=1.30)
    parser.add_argument('--brow-x', type=float, default=1.16)
    parser.add_argument('--brow-y', type=float, default=1.50)
    parser.add_argument('--brow-lift', type=float, default=.0015)
    parser.add_argument('--eye-lift', type=float, default=0)
    args = parser.parse_args()
    values = (args.eye_x, args.eye_y, args.brow_x, args.brow_y)
    if not all(np.isfinite(v) and .75 <= v <= 1.75 for v in values) or not all(
            np.isfinite(v) and abs(v) <= .015 for v in (args.brow_lift, args.eye_lift)):
        parser.error('Scales must be finite in [0.75, 1.75], eye/brow lifts within 15 mm')
    package = json.loads(args.package.read_text())
    captures = {state: next(r for r in json.loads((args.source / (state + '.json')).read_text())['renderers'] if r['name'] == 'Face')
                for state in ('neutral', 'half', 'closed')}
    if package['vertex_count'] != len(captures['neutral']['vertices']):
        raise ValueError('Source vertex count differs from package')
    result, metrics = scale_package(package, captures, values[:2], values[2:], args.brow_lift, args.eye_lift)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, separators=(',', ':')), encoding='utf-8')
    metrics.update({'input_package': str(args.package.resolve()),
                    'input_sha256': hashlib.sha256(args.package.read_bytes()).hexdigest(),
                    'output_package': str(args.output.resolve()),
                    'output_sha256': hashlib.sha256(args.output.read_bytes()).hexdigest()})
    args.output.with_suffix('.scaling.json').write_text(json.dumps(metrics, indent=2), encoding='utf-8')
    print(json.dumps(metrics, indent=2))


if __name__ == '__main__':
    main()
