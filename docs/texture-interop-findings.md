# Texture interop findings (VRoid Studio 2.14.0)

## Environment and baseline — 2026-10-25

- Upstream: `a6b0719d01fa6cac9a857956907243d9916390b2`, MIT license. Initial worktree clean,
  branch `master`; implementation branch `codex/texture-comfy-bridge`.
- Windows 11 x64; Unity 6000.0.62f1, IL2CPP; .NET SDK 10.0.100, target/runtime .NET 6.
- The existing installed app was running without BepInEx. It was left running and unchanged.
  A local copy was prepared under ignored `run/game`, with BepInEx 6.0.0-be.788 and its generated
  interop assemblies. App binaries and generated metadata are never committed.
- Initial build before interop generation failed with missing Unity references; after generation,
  unchanged upstream built with zero warnings/errors. Baseline protocol suite: 26 passed, 0 failed.
  `create female`, `save`, `open`, MCP initialization and tool enumeration were verified.
- Reused the existing local ComfyUI runtime, virtual environment and models on loopback port 8190.
  `/system_stats`: ComfyUI 0.37.0, PyTorch 2.8.0+cu128, RTX 5060 Ti 16 GB.
  No AI weights were installed/downloaded; an existing local rigging workflow was consulted read-only
  for its existing Qwen Image 2.1 workflow and filenames.

## Verified engine path

The declarations below were inspected in the **generated assemblies for the installed app**, then
exercised in its running clone. They are not inferred from the names in the design brief.

1. Captured `VRoid.Studio.Engine.Model.Context` is `VRoidCore.Editing.Context`.
   `editingModel`, `_revision`, `IsQueryExecutable`, and `IsCommandExecutable` are available.
2. `Model.GetActiveLeafTransferables()` returns concrete transferable IDs and group types.
   `GetEditableImagePathsForTransferableQuery(id).Execute(model)` returns actual editable image
   paths. The baseline female model exposes seven targets, including `Level3.Iris` (1024x512).
3. `GetEditableImageRasterLayersQuery(path)` returns ordered raster-layer paths. The first iris
   has one layer. `GetEditableImageLayerTreeParameterSetQuery` provides name, opacity and visibility.
4. `GetRasterLayerContentQuery(path).Execute(model)` returns `Result.size` and `Result.rgbaBytes`.
   `Texture2D.LoadRawTextureData` + `EncodeToPNG` preserves dimensions and alpha. This reads actual
   layer pixels, not thumbnail resources, tint-composited materials, or a screenshot crop.
5. `LoadImageToEditableImageRasterLayerCommand(path, BitmapSize, Color[])` is an existing history
   command. Submit via `Context.ExecuteSyncCommand`. It rebuilds the model, updates history and
   persists through `.vroid` save/reopen. `LoadImageAsNewRasterLayerCommand` also exists but is
   not exposed. `ActionHandler.ImportLayer/ExportLayer` are file-dialog entry points, avoided here.
6. Export/load orientation is consistent: Unity's PNG encoder/decoder and engine raw RGBA round
   trip without a flip. No-op import reproduced exact layer pixels and the stable rendered PNG.

Read-only queries are gated on a ready editing context and execute on the shared main-thread queue.
The facade is the only new layer referencing VRoidCore types; transport and handlers use plain DTOs.
Temporary Unity textures are destroyed in `finally`. Import waits for a **model rebuild counter**,
not merely a preset notification or the existing timeout-based `Signals.Ready` fallback.

## Session identity, files and validation

Target IDs combine a random session token with transferable/image IDs. A changed context, leaving
the editor, preset replacement/clear, or the engine's transferable-group-list notification
invalidates cached targets and backup IDs. Layer IDs are engine node IDs. Import/restore requires
the current engine revision and rechecks state after rendering. Unrelated edits can conservatively
invalidate an operation. Section classification is a convenience inferred from group-type names;
the returned `group_type` is authoritative. Coverage is active leaf-transferable editable images,
not all possible materials, baked textures or base-collection images.

All texture files must be `.png` under configured `texture_workspace`; relative paths resolve there.
Canonical containment, alternate data-stream rejection and reparse-point checks prevent ordinary
path escapes. This remains a local trusted-process API, not a hostile filesystem race sandbox.
Export uses create-new. Import stores a unique original PNG first. The original is retained on disk
even when its session ID expires. PNG decode is limited to 64 MiB / 16 megapixels; exact dimensions
are required, without resizing. Alpha must match by default; subsequent `alpha_mode:replace`
support explicitly permits changed alpha (see [makeup verification](makeup-editing.md)).
Rollback is attempted through the same history command;
busy engines, changed sessions or concurrent edits produce an explicit rollback failure and backup
path rather than a claim that state was recovered. No-op import still adds an engine history entry.

## Runtime evidence

- `run/qa/baseline-protocol.txt`: 26 protocol checks passed.
- `run/game/texture_roundtrip_result.json`: production-handler in-plugin test passed; rendered
  change and exact restore verified with nonempty, stable frames.
- `run/qa/live-texture-final.json`: 17 live checks passed, including unchanged import, malformed
  and truncated PNG, missing file, dimensions, alpha, path escape, stale revision/target, unknown
  layer, changed render, exact restore, stale session ID, save/reopen persistence, tampered-backup
  rejection, immediate save-directory rejection and preset-change target invalidation.
- `run/qa/bridge-tests.txt`: all 21 Python bridge unit/mock tests passed, covering decoding,
  alpha, paths, local URLs, workflow bindings,
  output ownership, failure, cancellation, timeout, and preview-only/apply behavior.
- `run/qa/final-build.txt`: Release build succeeded with zero warnings and errors.
- Existing external regression coverage passed across runs: protocol 26, TCP 5, queue 3,
  stress 6, and 105 guards/render/misuse checks. The initial full run stopped with Windows
  `/tmp` assumptions; the portable rerun (`regression-portable.txt`) had 104 passes and one
  slow save-directory rejection. The handler now rejects directories before native save;
  the affected render suite (`regression-render-final.txt`) passed all 17 checks, including
  immediate rejection. These are combined results, not a single clean full-suite run.
- `run/qa/mock-roundtrip.json` and `restore-result.json`: deterministic channel edit, changed
  front render, exact pixel and rendered-PNG restoration.
- `run/qa/comfy-roundtrip.json`: real ComfyUI LoadImage → ImageInvert → SaveImage round trip;
  this validates HTTP execution and output retrieval, not inference.
- `run/qa/qwen-roundtrip.json`: **real local AI inference**, existing Qwen Image 2.1 INT8 model,
  existing Qwen3VL 8B INT8 encoder and BF16 VAE, 8 steps, seed 20261025, resolution=0. Prompt ID
  `96595d07-ebc7-40e5-9d72-3d6b744facbe`, server execution approximately 24 seconds.
  Exported 1024x512 iris layout was edited to green, original alpha explicitly restored, candidate
  visually reviewed, imported, rendered in three directions, saved and reopened with exact pixels.
  Artifacts: `run/game/texture-workspace/runs/9663a6e2e33646cf8ef2df274dd2af47/`.
  Existing brown iris tint remains active, so the final eyes contain green centers and brown shading.

## Rendering issue found during validation

Launching the cloned app with a hidden initial window produced uniform black preview and full-window
captures despite live model updates. Showing a normally launched app produced valid frames. Initial
black-frame captures were **not accepted as visual evidence**. Texture import now requires three
identical **non-uniform** frames and the bridge rejects uniform previews before inference/import.
The shared screenshot cache is cleared before each capture so failures cannot return a previous PNG.
This guard detects empty frames, not model visibility or artistic quality. No GUI automation was used.

## P1 custom items — discovery only

`PresetCategoryViewModel.CustomItemOptions` includes `CustomItemOptionViewModel`; its model is
`FileCustomItemModel` with `CustomItemId`, group ID/type/revision, display name and file path.
`custom_item_list` enumerates these via current engine VMs without folder scanning or selection.
The development user's installed custom-items directory contained zero `.vroidcustomitem` files;
empty discovery was verified, populated enumeration and apply/persistence could not be verified.
No external asset was downloaded or fabricated to claim P1 success.

`CustomItemRegistry.ImportCustomItem` needs parsed `V1CustomItemContents<VRoidCustomItemFileMetaData>`,
`CustomItemTransferableObject`, global bus and error callback. `ImportCustomItemByTransferableType`
is asynchronous. Selection uses a different VM from stock presets and may involve temporary-item
save dialogs. Import/apply remain unexposed until these ownership/lifecycle and error paths can be
verified with an actual installed item.

## P2 VRM — research only

Existing entry points remain `EditRootScreen.ViewModel.EnterToVRMExport`, `VRMExportScreen.ViewModel`,
`VRoidStudio.Exporter.VRoidExporter`, and `VRoidStudio.PhotoBooth.ExportSettings`. No VRM tool was
added or claimed. Required follow-up: choose VRM version, explicit author/license metadata and
settings, understand confirmation screens, observe export completion/error signals, validate the
result container and preserve the current `.vroid` session on failure. Saving `.vroid` is not VRM export.

## Remaining limits

UV-guide extraction is unimplemented (`uv_guide_path:null`). Hair, clothing and other layer types
are not claimed runtime-tested; initial acceptance was the iris layer. Subsequent eyeliner,
eyelash and prepared FacePaint eyeshadow coverage is recorded in [makeup findings](makeup-editing.md).
No geometry generation,
automatic resizing, custom-item import/apply, or VRM export. PNG backups survive process exits;
automatic restore IDs do not. Native crash/process loss cannot be automatically rolled back, and
an HTTP timeout can leave an import outcome unknown. Retain manifests/backups and inspect before
retrying. Qwen quality was inspected on one simple atlas; UV alignment on complex clothing still
requires per-view review.
