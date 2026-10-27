# Hair geometry editing: feasibility and implementation plan

Status: brush-point editing plus bounded native width/profile controls implemented and
runtime-validated on VRoid Studio 2.14.0, 2026-10-27. The initial investigation inspected generated `VRoidCore.dll` and `VRoid.Studio.dll`
interop assemblies with Mono.Cecil. Subsequent implementation and tests used the prepared cloned
app. The user's installed app remained untouched; the clone's prior model was saved to a unique
`run/qa/pre-hair-*.vroid` checkpoint before restarting.

## Implemented and verified

`hair_list`, `hair_get`, `hair_move_point`, and `hair_restore` now use the production MCP/raw queue.
The initial fixture is a stock front-hair preset (English label `Straight Blunt Bangs`, index 1
in this installation). A non-root brush control point is moved by a bounded relative offset;
all other points, normals and UVs are preserved. A `.vroid` checkpoint plus session-local point
backups provide recovery. The canonical contract and limits are in
[external-api.md](external-api.md#hair-geometry).

- `run/qa/hair-build.txt`: Release build, zero warnings/errors; deployed DLL hash matched.
- `run/qa/hair-live-final.txt`: **28 live checks passed**, including exact single-point change,
  all other strands unchanged, numeric/identity/revision guards, render change, scoped restore,
  stacked edits, session/preset invalidation, and exact geometry/render after save/reopen.
- `run/qa/hair-protocol.txt`: **26 protocol checks passed**.
- `run/game/hair_roundtrip_result.json`: production-handler in-plugin test passed, with
  `render_changed:true` and `exact_restore:true`. The test closed only the prepared app.
- Full external artifacts: `run/qa/hair/2d5461f5cc7245f9b31823978b6c82ff/`, containing point JSON,
  before/front/right/back/reopened renders, baseline and modified `.vroid` models, and result JSON.

The stock point command has drag-style history semantics. `ExecuteSyncCommand` is followed by
`FlushPendingHistory(false)` to complete the pending edit. The verified command produces a model
rebuild, so the handler requires `Signals.ModelVersion` to advance plus model-ready state, then
exact full-strand read-back and three identical nonempty preview frames. Guide-only commands have
not been validated and are not exposed. Neither mesh creation nor reference matching is implemented.

Initial validation failed the stable-preview requirement and successfully restored the original
point data. Further diagnosis found 9–24 changing pixels between sampled frames even on the
unmodified saved fixture at MSAA 4. Flushing history alone did not eliminate baseline flicker.
The cloned app was explicitly configured with `msaa_samples:1` and `render_scale:2`; all final
tests used that setting. Strict frame equality remains enforced, and other unstable rendering
configurations may fail and attempt rollback. The first full live pass reached 27 successful checks
but used the English category `Front` against Korean localization; the final pass used `앞머리`
as a literal API argument and passed preset invalidation too.

Run against a dedicated app at its model-selection screen:

```powershell
python tools/api-tests/hair_tests.py --model run/qa/hair-baseline.vroid --preset-category '<localized front category>'
# Raw command; uses a saved fixture with visible brush hair and closes the test app:
# test hair_roundtrip <path.vroid>
```

The test model is a technical deformation demonstration. Large offsets at one point can produce
a sharp kink: collision checks, automatic smoothing and appearance optimization remain follow-up work.
The final visible demo applies three bounded offsets to neighboring tip points, saves and reopens
with identical geometry and render. `run/qa/hair-demo.json` points to its manifest, before/after
views and `hair-tip-bend.vroid`. The prepared app is left open on this Korean-language demo.

## Findings

The app exposes editable hair geometry below the existing preset interface. There are separate
brush and procedural hair groups. A strand is an editable hair element, not necessarily one
physical hair. The following are observed types/members, not proposed functionality:

| Capability | Observed engine surface |
|---|---|
| Enumerate groups and strands | `GetHairGroupListQuery`, `GetHairGroupChildrenQuery`, `GetAllHairStrandsQuery` |
| Inspect strand points | `GetHairStrandControlPointCountQuery`, `GetHairStrandControlPointQuery` |
| Move a brush strand point | `MoveBrushHairStrandControlPointCommand(BrushHairStrandPath, int, ControlPoint)` |
| Inspect brush parameters | `GetBrushHairGroupParameterSetQuery`, `GetBrushHairStrandParameterSetQuery` |
| Change brush parameters | `ModifyBrushHairGroupParameterSetCommand`, `ModifyBrushHairStrandParameterSetCommand` |
| Inspect/change procedural parameters | `GetProceduralHairGroupParameterSetQuery`, `ModifyProceduralHairGroupParameterSetCommand` |
| Read guide coordinates | `Context.GetCountOfHairGuideCylinderPoint`, `GetHairGuideCylinderPoint`, `GetHairGuideOrigin` |
| Change guides | `MoveBrushHairGuideControlPointCommand`, `MoveProceduralHairGuideControlPointCommand` |
| Duplicate / mirror | `CloneBrushHairStrandCommand`, `CloneBrushHairGroupCommand`, `FlipBrushHairStrandCommand` |
| Create | `AddBrushHairGroupCommand`, `AddProceduralHairGroupCommand`, `StrokeHairCommand` and continuation variants |
| Convert procedural to brush | `ConvertProceduralHairGroupToBrushHairGroupCommand` |

Brush parameters include width/thickness scales, twist amplitude/distortion, cross-section,
division count and a width-curve ID. Strand overrides include nullable values: null must remain
distinct from an explicit zero. Procedural form parameters include length, hair count, angular
extent, parting, tip curl and flow. These names alone do not establish units, safe ranges or
visual semantics. `SetHairStrandCurveCommand` takes a 2D `Curve`; do not assume that curve is the
strand's 3D trajectory. The point-query/move-command pair is the initial geometry candidate.

`ControlPoint` contains `Point`, `Normal` and `Uv`. Guide data contains an origin transform and
raw/offset cylinder points. Treat world/head/guide coordinates as unresolved until calibrated;
preserve normals and UVs during the first position-only experiment. Guide edits have nested
parameter structures that need further inspection.

History commands can be submitted through `Context.ExecuteSyncCommand`, as in the texture work.
Some modifiers require IL2CPP delegates; rooting, callback lifetime and exception containment
must be verified. Do not mutate query-returned objects directly or call `RollForwardSync`
outside the history manager. Command names and rollback methods do not prove a usable rollback
contract under concurrent edits.

Observed update payloads include `HairStrandUpdatedEventArgs`, `HairGuideUpdatedEventArgs` and
`HairGroupChildrenUpdatedEventArgs`. Context also has requested/resolved update revisions.
Establish which events and model-ready transitions occur for each command before choosing a
completion condition; texture rebuild signaling may not cover every guide-only change.

## First deliverable: reshape one existing brush strand

1. On a saved test model with visible hair, enumerate transferable → group → strand and return
   session-scoped IDs, types, revision, parameters and control points. Record a baseline model
   and front/profile/back renders. Do not assume the existing iris demo contains editable hair.
2. Calibrate point coordinates against guide origin and rendered mesh. Move one non-root point
   by a small bounded offset using the production queue and stock history command. Read it back
   and wait for verified engine completion and three identical nonempty frames.
3. Confirm both the intended geometry change and visible silhouette change. Restore the original
   point, verify data/render restoration, then repeat an edit and save/reopen a new `.vroid` file.
4. Reject stale session/revision/strand IDs, invalid indices, non-finite/out-of-bound coordinates
   and excessive point counts before mutation. Failures must report whether restoration was
   confirmed and retain the pre-edit `.vroid` checkpoint.

Success means an existing bang can visibly bend and recover through MCP with persistence.
It does not mean reference-image reconstruction or arbitrary hair generation is complete.

Initial tools are now registered: `hair_list`, `hair_get`, `hair_move_point`, and a scoped
`hair_restore`. Raw `hair_group_width` and `hair_profile` additionally use the stock parameter
and width-curve history commands. `GetHairStrandCurveQuery` was confirmed at runtime to return
the ribbon's width profile, not the trajectory. The width edit preserves strand overrides;
profile edits target a single strand and preserve all control-point positions, normals and UVs.
Both checkpoint the complete model, wait for a rebuild, verify native values and require three
identical nonempty preview frames. `tools/api-tests/hair_width_tests.py` passed six live checks
covering model/input/revision guards, render changes, preservation of other strands, exact numeric
width restoration and save/reopen persistence with an active face bridge. The new native group
parameter delegates remain rooted for history and are capped at 128 per app process. Further
parameter editing must establish bounds and override semantics; avoid arbitrary property writes.

The single-group width operation also accepts back brush groups, selected by the current engine
group ID. It does not broaden the front-only mirror or offset operations. A separate world-triangle
probe supports offline back-hair work: it exports active avatar skin, clothing and native hair
meshes plus the head transform, without mutating the model. The back-width fixture verifies that
all points, per-strand profiles, other groups and the face package remain unchanged, that the
preview changes, and that restoring the original width restores the exact preview. Collision
measurements should use actual baked triangles in the current pose; runtime renderer bounds
can substantially exceed a strand's actual vertex extents. Source-specific head/guide mapping
still requires numerical calibration against exported hair geometry.

Group inspection also reports native thickness, division count and cross-section. A follow-up
experiment changed the group's `DivisionCount` from 20 to 64 through its native parameter history
command. Native read-back changed, but control points, strand profiles and the exact preview did
not. The existing-strand render check rejected it; no division setter was retained in the API.
Its role in new-stroke sampling is a hypothesis, not a verified existing-mesh smoothing control.

`hair_smooth` instead calls the observed `SmoothBrushHairStrandsCommand` on a single brush path,
with a full-model checkpoint and event/read-back/stable-frame checks. In the inner-hair fixture
the app resampled each selected 8-point path to 6 points while retaining all other strands and
group settings. This changes point indices, normals and UVs under native control; do not apply
the position-only preservation contract to smoothing or reuse old point indices afterwards.
Smoothing can reduce a visible bend without reproducing the reference curve; visual matching
and occlusion checks remain necessary.

## Subsequent deliverables

1. **Useful reshaping:** group/strand width and thickness, procedural length/parting, guide
   deformation, and bounded multi-point edits. Preserve roots and smooth neighboring points.
   Validate history grouping instead of assuming a batch is atomic. Topology changes invalidate
   all affected handles. Never implement restore as a blind global undo.
2. **Construction:** duplicate and position existing locks, mirror where appropriate, then add
   groups and strokes using verified guide/material/transform ownership. Establish new-ID
   discovery, resource budgets, deletion recovery and persistence before exposing creation.
   Conversion from procedural to brush is an explicit topology-changing operation.
3. **Reference matching:** select a close preset, map its parts to bangs/sides/back/crown, fit
   overall silhouette and major locks, then refine width, curvature and asymmetry. Semantic part
   labels are an agent-level interpretation; engine group names alone cannot supply them.

The reference loop should use camera-aligned head proportions, hair masks/contours and landmarks
(parting, tips, ear coverage), with several small candidate changes and multi-view comparison.
Compare against actual supplied views. A single front image leaves back geometry ambiguous;
record chosen assumptions. Additional AI-generated views are hypotheses, not ground truth.
Penalize scalp/face penetration, abrupt point changes and unintended asymmetry. Keep the best
checkpoint rather than assuming every step improves similarity.

ComfyUI remains useful for appearance and texture variation. Geometry fitting needs the hair
engine controls above. Start with fringe length, parting and side volume; complex braids and tied
structures need construction and collision work before promising accurate reproduction.

## Evidence and next code locations

Local metadata logs (ignored): `run/qa/hair-api-discovery.txt`, `hair-commands-types.txt`,
`hair-guide-types.txt`, `hair-context-events.txt`. These prove signatures only.

Implementation follows existing layers: `Editor/HairGeometry.cs` for engine access,
`Commands/Handlers/HairHandlers.cs` for validation and completion, MCP/dispatcher registration,
and a production-handler `Tests/HairRoundTripTest.cs`. The tested contract is in
`docs/external-api.md`. Reuse the prepared app, checkpoint workflow,
main-thread queue and frame verification infrastructure; do not automate the GUI.
