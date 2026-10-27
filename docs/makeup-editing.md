# Eye makeup: explicit alpha replacement

Implemented and runtime-verified on VRoid Studio 2.14.0, 2026-10-25. The existing `texture_import`
now accepts optional `alpha_mode:replace`; default `preserve` retains its previous rejection of
changed alpha. The ComfyUI bridge forwards the option through `--allow-alpha-change`, records it
in the manifest, and rejects conflicting alpha restoration. The canonical contract is in
[external-api.md](external-api.md#editable-textures).

## Verified workflow

1. Preserve the cloned app's current model to a new `.vroid` checkpoint, then deploy/restart.
2. Create a dedicated female test model and export the `Level2.Eyeline` raster layer (1024x256).
3. Build a deterministic test fixture: dilate the alpha of the first atlas half and extend its
   outer tip. Newly covered pixels use a dark RGB value; the other half is byte-identical.
4. Confirm default/preserve import rejects the candidate without changing revision, pixels or
   render. Explicit replacement imports exact RGBA through the existing engine history command.
5. Check stable front/right/left renders, unchanged other layers, exact restore, reverse restore,
   backup tamper rejection and exact pixels/render after save/reopen.

The front-view changed-pixel bounds are `(210,681)-(359,772)` on the 864x1438 preview: only the
eye on the viewer's left changes. The atlas half-to-eye mapping is observed on this fixture,
not a universal UV convention. The visible result thickens that eye's line and extends its wing.
The original eye geometry and other texture layers remain unchanged.

This is a deterministic API validation fixture, not AI inference or a finished makeup design.
The test's atlas coordinates are specific to the stock model; do not reuse them blindly on
another preset. The app's existing color parameters still tint the texture.

## Usage

Export with `texture_export`, edit the RGBA PNG inside the returned workspace, then call:

```json
{
  "name": "texture_import",
  "arguments": {
    "target_id": "<current target ID>",
    "layer_id": "<current layer ID>",
    "input_path": "<reviewed PNG inside workspace>",
    "expected_revision": 123,
    "alpha_mode": "replace"
  }
}
```

Use the actual revision from export/layer info. Keep the returned backup ID and restore through
`texture_restore` with the current revision. A saved `.vroid` checkpoint is still needed for
process/session-loss recovery. Neither alpha mode resizes or paints the image automatically.

## Validation and artifacts

- `run/qa/makeup-build.txt`: Release build, zero warnings/errors.
- `run/qa/makeup-unit.txt`: 26 bridge unit/mock tests passed. Includes alpha-mode conflicts,
  RGBA preservation, dimensions, default rejection, and MCP option forwarding.
- `run/qa/makeup-live.txt`: 20 live makeup checks passed using production MCP handlers.
- `run/qa/makeup-protocol.txt`: 26 protocol checks passed.
- `run/qa/makeup-texture-regression.txt`: existing 17 live iris checks passed, including
  default alpha rejection, exact restore, save/reopen and preset/session invalidation.
- Artifacts: `run/game/texture-workspace/tests/makeup-f50091a397764da1aaf9fe2840e6dabe/`:
  source/candidate/read-back PNGs, before/front/right/left/reopened previews, result JSON,
  baseline and `one-eye-liner.vroid`.
- Prior hair demo checkpoint: `run/qa/pre-makeup-17d9531848c144be834732b966c4a30c.vroid`.

Run `python tools/api-tests/makeup_tests.py` only against a dedicated prepared app at its
model-selection screen. It refuses to discard an existing session, creates unique artifacts,
and leaves its saved test model open. Rendering used MSAA 1 and render scale 2, as in hair QA.

## Remaining scope

Eye shape still uses the existing preset/parameter tools; this change edits the visible line,
not eyelid mesh geometry. Eyelash and FacePaint eyeshadow were subsequently verified as described
below. Eyebrow alpha edits and direct skin-layer edits remain unverified.
There is no automatic eye-side selection, UV-aware brush, makeup style generator, or image-based
eye matching. Generic RGBA editing is now available for those follow-ups without weakening the
default import behavior.

## Eyelash and eyeshadow extension

The stock `Long Curled Bold Lashes` preset (index 12 in this installation) has a nonempty
1024x256 editable raster layer. The default female model's eyelash layer was empty, so it was
not used to claim visible lash editing. The verification fixture dilates one atlas half and
darkens its RGB; the other half remains byte-identical.

The stock FacePaint `Pink Eye Shading` preset (index 13) uses a 1024x1024 asset image. It is
in the active leaf transferables but has no editable image until conversion. The new
`texture_prepare` tool converts only selected FacePaint image keys through the app's existing
advanced-edit history command. It first creates a whole-model checkpoint. The verified conversion
preserves the stable rendered image and every existing raster layer, persists through save/reopen,
and repeated preparation is a no-op. Reopening the checkpoint restores the original asset-based
structure. See the [contract](external-api.md#editable-textures) for save-path and recovery semantics.

Discovery initially passed a base `BeginAdvancedEditCommand.Input`, which the native engine
rejected as `Unknown input`. The endpoint timed out without reporting success; the saved fixture
was reopened after restart. The correct input is `OverlayInput` containing `SimpleTextureKey`
instances built from the group's declared image elements. No direct image/model writes or GUI
automation are used. Only the FacePaint conversion path is exposed; skin conversion is unverified.

After conversion, the test changes the first atlas half from pink to purple and increases its
alpha. The screen-left eye changes; bounds in the 864x1438 front render are `(220,642)-(352,779)`
for eyeshadow and `(222,686)-(359,740)` for lashes. These are observations on the stock fixture,
not universal atlas-to-eye coordinates. Both edits preserve all other layers, restore exact RGBA
and rendered PNGs, and persist with exact pixels/renders after reopening. The combined model also
retains both edits after reopening. Bright asymmetric colors deliberately make the test visible;
this is API verification, not a polished character design or AI inference.

Run `tools/api-tests/shadow_lash_tests.py --model <fixture.vroid>` against a dedicated app at
its model-selection screen. The fixture must have the above FacePaint and Eyelash presets selected,
with FacePaint not yet prepared. Test artifacts contain source/candidate/read-back PNGs, before
and multi-view after images, separate saved models, a combined model and a result report.

Final extension validation:

- `run/qa/shadow-lash-final.txt`: 28 live checks passed, including preparation guards,
  checkpoint recovery, idempotence, independent target edits/restores, save/reopen and combined edits.
- `run/qa/shadow-build-final.txt`: Release build with zero warnings/errors.
- `run/qa/shadow-unit.txt`: 26 bridge unit/mock tests passed.
- `run/qa/shadow-protocol.txt`: 26 protocol checks passed.
- Final artifacts: `run/game/texture-workspace/tests/shadow-lash-cb86be7097f945258213745553e8b404/`.
  The combined result is `shadow-and-lashes.vroid`; `combined-front.png` shows the screen-left
  eye with purple shadow and thicker lashes. The other eye retains the stock pink shadow.
- The earlier 26-check run is retained as `run/qa/shadow-lash-live.txt`; the final run adds
  no-model and missing-preset rejection checks.

The cloned app is left on the combined saved model, with Korean localization. The originally
running installed app remains untouched. No commits or pushes were made automatically.
