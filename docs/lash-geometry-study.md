# Upper-lash geometry study

The Asuna reference uses a connected dark upper-lid silhouette covering part of the iris and
outer sclera. Increasing alpha coverage on the stock Eyeline atlas did not achieve that shape:
extending the upper region past its UV boundary affected the lower-lid strip. Filling the stock
Eyelash sheet primarily enlarged its upward silhouette.

Eight representative Eyelash presets (0, 2, 3, 8, 12, 29, 36, 45) were selected through the
production handler and given identical diagnostic RGBA pixels. All eight produced the same
stable preview hash on this fixture. This establishes no useful geometry variation in those
presets; it does not prove that every native face configuration is equivalent. An inspection of
the generated interop assemblies found general blend-shape and bone parameter commands, but
no exposed eyelash-specific vertex-edit command. XWear mesh editing is a separate subsystem.

## Diagnostic export

`test face_mesh_export <saved-model.vroid>` opens the supplied checkpoint and writes
`<app>/mesh-study/`. It captures the current saved Neutral expression, then temporary eye closure
weights 60 and 100 through the production expression handlers. Each state has a stable preview
PNG and JSON containing baked world-space vertices, UV coordinates, submesh triangles and
material metadata. The first state also exports material main textures by GPU readback.

The test restores the original closure weights on success and exits the app without saving the
temporary expressions. It creates no mesh mutations. Output files are replaced on another run.
This is a diagnostic snapshot, not a VRM export: bones, full expression definitions, shader
semantics and native material masks are not preserved. On failure reopen the supplied saved
checkpoint before continuing.

## Blender prototype

`run/qa/blender_lash_study.py` imports the snapshots in Blender 5.3 Beta. It samples the original
upper-line silhouette in UV space and constructs an opaque ribbon anchored to that edge. Its
lower edge extends downward by up to 1.7 mm and forward to lie in front of the iris. The two eyes
are built symmetrically. A 2.8 mm trial covered the central glint and was rejected.

The added mesh has shape keys fitted to the captured half-closed and closed eyelid positions.
`Blink_Control["Blink"]` interpolates these three sampled poses on frames 1–20. The textures are
packed into the saved `.blend`. Front, three-quarter, half-blink and closed renders check the
occlusion and closure behavior. Flat emission materials are used for inspection; they are not
the app's toon shaders.

The prototype confirms that modifying geometry can create the intended downward coverage
while retaining iris movement and a closed-eye pose. It remains a Blender study: a complete
VRM rig, other expressions, gaze extremes and a VRoid reimport path have not been implemented
or verified. The working `.vroid` remains unchanged.

Local artifacts: `run/characters/asuna-blender-lash-v14/`; preset evidence:
`run/characters/asuna-structure-v13/preset-test/results.json`.

## Outer-corner continuation

`run/qa/blender_lash_corner_study.py` and `run/qa/blender_asuna_corner.py` extend the prototype
with a tapered outer-corner strip. Its front projection follows the visible white wedge and
joins the upper band to the lower outer edge. A small white triangle remains beside the iris.
Two tapered burgundy mesh strokes add layering to the upper band.

The corner is fitted to the original Eyeline surface using barycentric coordinates. Overlapping
native sections caused depth and blink-displacement discontinuities, so its depth is fitted to
the front surface and sampled blink coordinates are smoothed. These are fitted prototype shape
keys, not a new native VRoid edit capability. Tests cover both three-quarter views, front, half
closure and full closure; timeline checks include all four added pieces. The front raster check
also confirms that the new corner is connected to the upper band and leaves visible sclera.

The v15 `.blend` and comparisons are under `run/characters/asuna-blender-corner-v15/`. Iris style
and proportions, layered lash silhouette and the original FacePaint crease curves still differ
from the reference. The VRoid working model remains at v12.

## Crease and lower-contour revision

The v16 study uses a temporarily cleared FacePaint layer to export clean face skin. The app is
then reopened from its pre-test checkpoint; the restored FacePaint is verified against the
import's exact original RGBA backup. No Blender geometry is written into the working `.vroid`.

`run/qa/blender_asuna_style.py` adds two skin-bound crease strokes following the upper-lid curve,
a soft crease border, an extended lower contour, two tapered outer lash blades, and burgundy
brush strokes bound to the actual new black lash geometry. The lower contour joins the outer
corner and continues under the iris. Blink shapes for the ink additions collapse onto the
captured closed-lid rim; the crease strokes follow the captured skin deformation.

`run/characters/asuna-blender-style-v16/` contains the packed `.blend`, front and both angled
renders, half/closed captures, and the reference/v15/v16 comparison. Timeline drivers, finite
geometry, local vertex continuity, closed ink width and visible/hidden iris states are checked.
These checks establish prototype behavior, not reference likeness or a complete expression rig.

## Fine connected lash details

The v17 revision preserves the v16 geometry and adds three short upper-contour protrusions,
two lower-contour protrusions, two thin near-contour linking strands and a descending outer
strand joining the upper corner to the lower outline. Each root overlaps the existing dark
surface. The more prominent tips were reduced after reviewing the enlarged render.

`run/qa/blender_lashdetail_verify.py` verifies the added blink keys and closed-pose collapse.
`run/qa/blender_lashdetail_publish.py` checks that each of the five new protrusions adds visible
dark pixels connected to the main ink component in the front render, and that the iris remains
visible open and hidden closed. Both angled renders are reviewed visually. These are scoped
geometry and raster checks, not a full expression or gaze validation. Artifacts are under
`run/characters/asuna-blender-lashdetail-v17/`.

## Inner-tip droop and auxiliary folds

The v18 study removes a small upward turn at the inner end of the upper liner. A smooth local
downward deformation is applied to the upper native Eyeline UV region and overlapping ink,
with reduced deformation in the half-blink and none in the closed pose. The native lower-liner
vertices and closed liner geometry are compared exactly against v17 and remain unchanged.
The rendered inner lower edge is checked for a continuous descending trend toward the tip.

Two auxiliary crease strokes are added: a lower partial stroke close to the upper-lid contour
and a shorter, lighter fragment above the existing folds. They use the same sampled skin
deformation as the earlier crease ribbons. Artifacts and verification reports are under
`run/characters/asuna-blender-droop-v18/`.

## Eye-opening curvature

The v19 study changes the actual skin aperture rather than only its ink coverage. Independent
quadratic targets reshape the upper and lower contours, with local deformation applied to the
face skin, liner, lashes and nearby crease ribbons. The upper contour gains an arch while the
lower contour becomes shallower. At the fixed front camera, the mean opening height in the
measured central region decreases from 47.69 to 38.15 pixels relative to v18.

The iris, sclera, highlight and brow geometry remain exactly unchanged. A local depth guard
keeps deformed skin in front of the original eye surfaces and ink in front of the skin. The
half-blink uses reduced deformation; all closed shape coordinates and the closed render remain
exactly equal to v18. Verification checks the real face mesh selection, preserved geometry,
packed textures, sampled blink timeline and visible-aperture curve fits. Front and both angled
renders are reviewed visually. These checks establish deformation behavior, not likeness.

`run/qa/blender_eyeprofile_study.py` builds the revision; `blender_eyeprofile_verify.py`,
`blender_eyeprofile_mask.py` and `blender_eyeprofile_publish.py` verify and publish its evidence.
Artifacts are under `run/characters/asuna-blender-eyeprofile-v19/`. This remains a Blender
prototype with three captured blink poses, not a complete rig or a VRoid reimport. The native
working model remains v12; iris style and proportions still differ from the official artwork.

## Local lower-lid bend

The v20 revision replaces the smooth lower outer arc with a flatter under-iris section followed
by a short, steeper rise near the outer iris and a continuation into the outer corner. A local
deformation moves the skin aperture, native lower ink and attached lower-lash details together.
The upper liner, crease ribbons, iris, sclera, highlight and brow geometry remain unchanged.
All closed shape coordinates are exactly preserved; the half-blink uses 45% of the deformation.

The native liner material uses blended rather than dithered transparency to remove exposed
overlap artifacts near the corner. Its texture pixels and alpha remain unchanged. This changes
some closed-render edge pixels, so v20 does not claim pixel identity with the parent render.
The sampled blink timeline, packed textures, unchanged geometry and visible contour bend are
verified; front and both angled renders are reviewed. In the measured rising segment, the
front raster slope changes from -0.31 to -0.84 pixels per horizontal pixel, while the under-iris
segment stays shallow. This measures the local bend, not artistic likeness.

`run/qa/blender_lowerturn_study.py`, `blender_lowerturn_verify.py` and
`blender_lowerturn_publish.py` produce the study and evidence under
`run/characters/asuna-blender-lowerturn-v20/`. The complete rig and VRoid reimport limitations
remain unchanged; the native working model is still v12.

## Descending ink beside the iris

The v21 study adds an opaque filled ribbon beside the outer iris, connecting the upper band
to the rising lower contour. The outer edge covers the remaining iris sliver so the new ink
defines the iris-side silhouette. The matching ribbon on the opposite eye is mirrored.
All existing mesh shape coordinates are preserved exactly. The addition has sampled half and
closed shapes, follows the existing blink drivers, and collapses to a thin closed-lid strip.

`run/qa/blender_sideink_study.py` builds the addition. `blender_sideink_verify.py` checks existing
geometry preservation, symmetry, finite coordinates, packed textures and the sampled blink
timeline. `blender_sideink_publish.py` confirms visible new ink connected to the main liner and
the visible-open/hidden-closed iris states. Both angled views are reviewed. Artifacts are under
`run/characters/asuna-blender-sideink-v21/`. This remains a sampled Blender prototype; no full
expression rig or VRoid reimport is provided.

## Boundary-aligned lower and outer ink

The v21 side piece was rejected in visual review: it intruded into the iris and did not follow
the eye outline. Connectivity and blink checks did not detect that placement error.

The v22 revision starts from v20, without the rejected side piece. It finds the actual skin
aperture boundary loops and resamples their lower chains in each captured pose. A continuous
variable-width ribbon follows those edges, thickening at the outer rise and joining the upper
corner. The previous lower ribbon is hidden to remove the doubled contour. Two lower-lash
spur roots are repositioned onto the replacement line; their closed shapes remain unchanged.
The iris, upper liner, creases and other existing geometry are preserved.

`run/qa/blender_rimink_study.py` builds the revision. `blender_rimink_verify.py` verifies geometry
preservation, the absence of the rejected piece, symmetry and sampled blink drivers.
`blender_rimink_publish.py` measures rendered added ink against the real aperture boundary:
the reviewed v22 outer-region pixels remain within 7.12 pixels of that boundary, whereas the
rejected piece extended up to 18.24 pixels away. These distances check placement, not likeness.
Front, both angled views and half/closed captures are reviewed. Artifacts are under
`run/characters/asuna-blender-rimink-v22/`; the full rig and VRoid reimport limitations remain.

## Eye-area material colors and UV bake

The v23 study preserves every v22 mesh shape coordinate. A projection attribute anchored to
the neutral skin UV interpolation adds soft rose shading around the upper folds and coral
color below the eye and near the outer corner. Crease ribbons use softer mauve/pink-brown
colors with transparent edges. The existing upper-liner surface gains a subtle burgundy patch;
its brush details use burgundy and muted gray-purple. These are material edits only.

`run/qa/blender_makeup_study.py` bakes the skin wash color and coverage through the original
face UV map. `blender_makeup_publish.py` uses ComfyUI to assemble the 1024x1024 RGBA candidate
`facepaint-eye-wash-1024.png`, preserving the coverage channel within one 8-bit level. The live
app's available FacePaint target was read as 1024x1024; it was not modified. The skin atlas
contains only the wash, not the separate crease or liner mesh materials.

`blender_makeup_verify.py` checks all mesh shape coordinates, visible sampled blink drivers,
packed textures and a render with the baked atlas reapplied. Compared with the procedural
skin wash, that baked preview differs by at most one 8-bit channel level in the measured eye
region. Front, both angled views and half/closed captures are reviewed. Artifacts are under
`run/characters/asuna-blender-makeup-v23/`.

Texture transfer and mesh transfer must be distinguished. The existing production FacePaint
import can apply a reviewed/composited atlas to the native model after backup and UV validation.
It does not import the Blender eye aperture, added lashes or crease geometry. A complete
Blender-to-`.vroid` face mesh round trip has not been implemented; the native working model
therefore remains v12. The current snapshots also do not constitute a complete VRM rig.

## Plugin-assisted return to the app

The v24 result imports the v23 appearance through the new [experimental face bridge](face-bridge.md).
The source model remains preserved; the result is saved separately as
`run/characters/asuna-import-v24/asuna-import-v24.vroid`. The embedded package restores the
modified Face mesh, 20 added detail pieces and baked material colors in the prepared app.
It requires the MCP plugin and does not establish native VRM export support.

Live production-handler checks verify world-space agreement with the Blender/source captures
at open, half and closed poses, exact stable native preview after removal, save/reopen and a
full app restart without the input package available. The default edit pose is reconciled with
the captured Neutral pose, so it also restores correctly with expression preview disabled.

## Outer sclera and iris visibility

The v25 Blender revision returns to v23 after review of the imported result. Its lower-lid rise
started too far inward and removed most of the outer white wedge while cutting across the
outer iris. The rise is moved outward, the outer lower contour is lowered, and the iris/highlight
X span is reduced to 84% around the existing pupil center. The continuous rim ink is refitted
to the deformed skin boundary. Upper-liner and crease geometry, colors and all closed shapes
are preserved.

`run/qa/blender_sclera_study.py` builds the revision. `blender_sclera_verify.py` verifies preserved
geometry and sampled blink drivers, then renders sclera-only diagnostic masks. In the measured
outer-eye window, visible white pixels increase from 91 to 932. The colored render retains the
iris when open/half-closed and hides it when closed; the closed render is exactly equal to v23.
These measurements establish restored space, not artistic likeness. The official/v23/v25
comparison and both angled renders are reviewed.

Artifacts are under `run/characters/asuna-blender-sclera-v25/`. This change is saved only in the
new Blender study. The prepared app and saved native working model remain at imported v24.

## Upper-lash finishing

The v26 revision changes only three upper tip meshes and two near-contour linking meshes.
Each tip uses two cubic edges with its root embedded in the main band, a small swept rise and
a fine endpoint. The outer tip follows the visible upper-blade envelope rather than the hidden
edge of the original upper ribbon. The old thin linking loops are replaced with low filled
crests that overlap the band instead of leaving a bright gap. A continuous forward depth
envelope removes native-strip depth discontinuities in the new open and half poses.

The eye aperture, iris, white space, lower outline, creases and makeup are preserved from v25.
All closed coordinates and the closed render remain exactly unchanged. Verification checks
geometry scope, finite continuous detail meshes, packed textures, the sampled blink timeline,
visible connected pixels in all three revised tip regions and unchanged opening pixels below
the modified upper silhouette. Both angled views and the half-blink are reviewed.

`run/qa/blender_lashfinish_study.py`, `blender_lashfinish_verify.py` and
`blender_lashfinish_publish.py` produce artifacts under
`run/characters/asuna-blender-lashfinish-v26/`. This remains the current Blender study; the app
model has not been replaced since imported v24.

## Accepted eye revision returned to the app

The accepted v26 Blender study is automatically exported and packed by the existing face-bridge
tools, then imported through the production `face_import` handler. The resulting model is
`run/characters/asuna-import-v27/asuna-import-v27.vroid`. The preceding v24 model is retained,
with an additional pre-import checkpoint in the v27 artifact directory.

No runtime bridge changes or manual per-object transfer are needed for this revision. The
exporter gathers 20 visible detail pieces (10,256 vertices), native mesh corrections and baked
colors into the package. Thirteen live checks verify the embedded hash, world-space agreement
with Blender at open/half/closed poses, replacement of the earlier package and an exactly
preserved stable preview after save/reopen. Both angled views are reviewed. The package remains
plugin-dependent and same-source; arbitrary Blender scenes and native VRM export are outside
the verified workflow.
