# Asuna V49 screenshot provenance

These images show the local Asuna school-uniform study in the prepared VRoid Studio 2.14.0 app
with `VroidStudioMcp.dll` loaded. The primary reference was Mx2J's official Blue Archive standing
artwork; a user-supplied figure reference was used only for hair volume/depth. The halo was adapted
from user-supplied third-party material.

## Published captures

All three PNGs are byte-for-byte copies of the V49 **reopened** preview captures. They are not
cropped, retouched, generated or rendered in Blender. Each original preview is 2408 × 1742 pixels.
They were selected for this README update on 2026-10-28.

| Published file | Local source under `run/characters/asuna-shirt-collar-v49/` |
|---|---|
| `asuna-v49-body.png` | `reopened-body-front.png` |
| `asuna-v49-face.png` | `reopened-head-front.png` |
| `asuna-v49-three-quarter.png` | `reopened-upper-threequarter_left.png` |

The source directory is local evidence, ignored by Git; readers do not need it to view the images.
The README references only the tracked copies in this directory.

## Evidence and scope

- Model: `asuna-shirt-collar-v49.vroid`.
- Model SHA-256: `6fd20bea72ca9079bedf844df50acd8956f858fbe19f898c41536e596a5ef192`.
- Embedded package SHA-256: `c0a9e93d880f2d47c14eb0fb62e718998456a227db8599b7e3801846b64b3a3c`.
- Local `verification.json`: 56 passing runtime, preservation and save/reopen checks, including
  exact preview reproduction for these views.
- Local `blender-verification.json`: 8 native material submeshes plus 43 added meshes; 153
  shape-key arrays checked with maximum world-space error about 0.000000060 m and exact packed PNGs.

Those reports were inspected for this documentation update; the runtime checks were not rerun.
They establish the recorded model's technical behavior, not finished reference likeness or user
acceptance. This is a work in progress: hair, hair ribbon and clothing still need visual refinement.
The later V50 ribbon candidate was not fully saved/verified and is excluded from this gallery.
Current-pose and sampled-blink checks do not establish arbitrary animation, cloth simulation or
native VRM export support. Custom geometry requires the plugin when reopening the `.vroid`.

The character model, reference artwork and third-party source assets are not distributed here.
The code's MIT license does not grant rights to Blue Archive character designs or third-party
assets. The screenshots document an unofficial fan-made interoperability study.
