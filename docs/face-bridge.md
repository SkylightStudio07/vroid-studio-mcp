# Experimental Blender face bridge

For reference matching and the geometry/appearance/persistence review gates, follow the
[character editing harness](character-editing-harness.md), including the collar projection failure.
Accurate package restoration does not establish that the imported geometry is visually correct.

This bridge returns the current diagnostic Blender face study to the prepared app. It is a
**plugin-dependent appearance extension**, not a native arbitrary `.blend`/VRM importer.
The app saves an embedded face package inside its normal `.vroid` file. This plugin restores
the custom renderers after opening and after supported rebuilds. No companion asset file is
needed to reopen the saved model. Without the plugin, the custom appearance is not activated.
Native VRM export integration has not been implemented or verified.

## Design

Interop inspection found `MeshOverride` references to resource meshes but no validated native
history command that serializes an arbitrary replacement face asset. Instead, a reviewed package
is stored in the active FacePaint transferable's `Collection.Blob.Blobs`, under
`com.kokokoshka.face-bridge.v1`. `CloneSpecificDeep` and `ReplaceTransferableCommand` use the
existing engine history and serializer. The original source model is preserved by a save-as
checkpoint before import. No `.vroid` bytes are patched externally.

`FaceBridgeRuntime` clones the native Face mesh and applies the exported world-space correction
through the original weighted skin matrices. Existing bones, weights, UVs, submeshes and morph
names are retained. A reference-neutral offset reconciles the captured Neutral expression with
the app's normal zero-preview pose. This prevents the imported lashes from separating from the
lid immediately after opening. The neutral closure recipe interpolates the captured 26/60/100
samples; left/right closure morphs remove the corresponding open correction at full closure.

Added ink and crease meshes are combined into one head-bound skinned renderer. They retain
their captured open/half/closed shapes and follow native morph weights. Other expression motion
uses nearest-source-vertex displacement and has not been comprehensively validated. Material
color and alpha are baked in Blender and assembled through ComfyUI; textures are embedded as
RGBA PNGs. Cloned MToon materials use emission to reproduce the study's flat colors.

The same renderer can also contain an addition with explicit `bone_name`
`J_Bip_C_UpperChest`, `J_Bip_C_Neck`, `J_Bip_L_LowerArm` or `J_Bip_R_LowerArm`
for fitted ribbons, collars, rolled cuffs and wrist bands. These parts use
their own single-bone weights and bind poses; their vertices do not receive face morphs or
blink corrections. Existing additions without this field retain the head-bound behavior.
The exporter reads the Blender object's `bridge_bone` property. Package validation checks
the binding name and skeleton presence before saving a checkpoint. This is a rigid accessory
extension, not native outfit import or cloth simulation.

The main-thread ticker restores the package once per completed model rebuild and copies the
native morph weights to the additional renderer. It does not rebuild meshes every frame.
Leaving the edit session disposes owned meshes, materials and textures. Removing the package
restores the native mesh/material references.

## Supported workflow

The exporter currently supports the repository's same-source diagnostic study: `neutral.json`,
`half.json`, `closed.json` and compact Blender objects created from their material submeshes.
It is not a general artist-scene importer.

1. Preserve the source `.vroid`. Capture the three source states with `face_mesh_export` as
   described in [the geometry study](lash-geometry-study.md).
2. In the matching unmodified source model, reset temporary expression preview and call `face_probe`.
   Its native probe must have zero blend-shape weights and matching source UVs.
3. Edit a copy of the generated Blender study. Preserve native vertex ordering and shape-key
   correspondence. Added objects need `Basis`, `half` and `closed` keys; hidden objects are skipped.
4. Export and pack under the configured texture workspace:

```powershell
& '<blender.exe>' --background --factory-startup --python tools/face-bridge/export_blender.py -- `
  --blend '<study.blend>' --source '<snapshot-folder>' --native-probe '<probe.json>' `
  --output '<workspace_root>/face-bridge/my-face'
python tools/face-bridge/pack.py '<workspace_root>/face-bridge/my-face/export.json'
```

5. Call `face_bridge_status`, then `face_import` with `face-package.json` and the returned
   revision. Review the result and save to a new `.vroid`. That file embeds all package assets.

The [external API](external-api.md#blender-face-bridge) defines arguments and limits.

### Scaling captured eyes and brows

`tools/face-bridge/scale_features.py` scales the supported diagnostic package's eye
apertures, irises, highlights, native lashes/eyelines and added ink/creases around
each eye's own center. Eyes and brows have independent vertical offsets; brows
also have independent horizontal/vertical scales. Nearby skin receives a smooth falloff. All three captured blink
poses use the same transforms. Source topology and neutral geometry are checked
before writing; textures, UVs, bindings, source guards and the scalp inset are retained.

```powershell
python tools/face-bridge/scale_features.py --package '<existing-package.json>' `
  --source '<matching-snapshot-folder>' --output '<workspace>/face-bridge/scaled/face-package.json' `
  --eye-x 1.18 --eye-y 1.30 --brow-x 1.16 --brow-y 1.50 --brow-lift 0.0045 --eye-lift 0
```

Import the result through `face_import`, inspect front and angled views, and verify
neutral/half/closed geometry and saved/reopened appearance. The scaling manifest
records the parameters and package hashes. This operates on the captured custom
geometry; native face/body parameter changes still require a matching package.

## Compatibility and recovery

- A matching vertex/UV/triangle fingerprint and source rest-world geometry are required.
  Source pose tolerance is 0.1 mm. Changing source face/body parameters or rig placement can
  suspend restoration with `FACE_SOURCE_POSE_MISMATCH`; restore those parameters or export a
  matching package. Native parameter fitting is not automatically regenerated.
- Native Neutral closure at the three sampled poses is validated. Other expressions, gaze
  extremes, topology-changing presets and animation combinations remain outside the verified
  scope. Source captures do not supply a complete independently exportable VRM rig.
- The current fitting assumes source Neutral closure 26 and additional captures at 60/100.
  Other sample schedules are not supported by this exporter/runtime.
- Selecting a different FacePaint transferable can remove the package carrier. Keep the saved
  imported `.vroid` and the input package as recovery artifacts.
- A school-hair fitting session also observed the embedded blob disappear after entering
  Hairstyle and changing global hair colors. The individual triggering operation was not isolated.
  Brush point/width/profile edits retained the accepted package in the live checks. Inspect
  `face_bridge_status` after section/color changes and before saving; when the blob is absent,
  explicitly reimport the original matching package, then verify its hash, active renderer and
  saved/reopened geometry. The fitting workflow recovered the accepted face this way; automatic
  preservation across every editor operation is not implemented.
- `face_restore` removes the embedded extension through native history and restores the native
  renderers. Save afterwards to persist removal. An import error reports its checkpoint when
  available; reopen that checkpoint for whole-model recovery. Automatic rollback after every
  possible runtime failure is not claimed.
- Do not distribute the result as a plugin-free native face mesh modification or claim native
  VRM export support from the editor preview alone.

## Verification

### Experimental scalp inset

The school-hair curvature study also reuses the package's head-bound additions for a small inner
hair surface behind native side locks. The generator appends a cap and six cubic ridge ribbons
to the accepted eye package; all original face deltas, textures, bindings and added eye meshes
remain structurally identical. Front design curves are raycast onto the same-source native Face
and Head triangles and lifted slightly above that surface. Triangles without a surface hit and
unused vertices are removed so no fallback geometry can protrude behind the head or alter bounds.
The old native inner strands are hidden through the production visibility handler; their data is
retained in the model and checkpoint. The meshes and packed materials are also saved in a Blender
study. Native global hair colors do not recolor these custom materials.

This is a source-specific head-bound inset, not a native editable hair strand. It inherits the
existing face-bridge plugin requirement and source-geometry guards. Its selected model needs
front/angled occlusion review, accepted eye geometry and blink checks, and exact save/reopen data
and preview checks. Do not infer VRM export support from this editor preview.

A subsequent inset study replaces the seven procedural additions with one alpha-textured scalp
surface, using GPT-generated strand artwork. It retains the same source projection, head binding,
face/eye package preservation and occlusion checks. Opaque magenta generation was keyed and
despilled through ComfyUI after the generator rejected transparent output; the resulting RGBA is
embedded in the package. ComfyUI tone matching also introduces at most one 8-bit alpha level of
rounding. This remains a painted scalp inset rather than native strand construction. The accepted
face/eye additions and all native hair paths remain unchanged.

Release build completed with zero warnings/errors. The live harness
`tools/api-tests/face_bridge_tests.py` uses production MCP handlers and checks malformed topology
and triangles, path/revision guards, no checkpoint side effects on invalid input, embedding,
native/additional world vertices at neutral/half/closed, stable save/reopen, exact native-render
restoration and reimport. The fixture passed 23 checks; the existing protocol suite passed 26.

The prepared app was also fully restarted while the input package file was unavailable.
The embedded hash and active renderers were restored, and the default preview was byte-identical
to its pre-restart capture. Both angled views were reviewed. Local evidence and the saved model
are under `run/characters/asuna-import-v24/`; the original installed app processes were untouched.
