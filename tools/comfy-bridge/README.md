# ComfyUI texture bridge

A separate Python process exports an editable VRoid raster layer, submits a local ComfyUI
API workflow, validates its PNG, and optionally applies it through the plugin's history command.
Inference never occupies the Unity command queue. See [the contract](../../docs/external-api.md#editable-textures)
and [runtime findings](../../docs/texture-interop-findings.md).

## Setup

Use Python 3.10+ and install `requirements.txt`. Start the updated plugin in VRoid Studio 2.14.0
and an existing local ComfyUI server. No models or custom nodes are downloaded by this bridge.
Open/create a model before enumerating targets.

```powershell
python -m pip install -r tools/comfy-bridge/requirements.txt
python tools/comfy-bridge/bridge.py healthcheck --config tools/comfy-bridge/config.example.json
python tools/comfy-bridge/bridge.py targets --config tools/comfy-bridge/config.example.json
```

Copy an example config for local customization. Relative `workspace_root` is relative to the
config file, and must match `texture_workspace` in the plugin's servers JSON. The plugin defaults
to `<app>/texture-workspace`; the example points at this repository's `run/game/texture-workspace`.
Relative reference/mask/cancel/save paths are relative to that workspace. Workflow/config paths
are explicit local files. The bridge rejects non-loopback services and HTTP redirects.

## Mock and real ComfyUI round trips

Use the exact target and layer IDs returned by `targets`. They expire when a model or preset changes.

```powershell
python tools/comfy-bridge/bridge.py texture-roundtrip `
  --config tools/comfy-bridge/config.example.json `
  --target-id '<returned target_id>' --layer-id '<returned layer_id>' `
  --mock-comfy --apply

python tools/comfy-bridge/bridge.py texture-roundtrip `
  --config tools/comfy-bridge/config.example.json `
  --target-id '<returned target_id>' --layer-id '<returned layer_id>' `
  --workflow tools/comfy-bridge/workflows/invert-api.json `
  --restore-source-alpha --apply --save-as examples/inverted.vroid
```

Without `--apply`, generation stops at a validated candidate and manifest. This is the default
review step; `--apply` explicitly approves applying the result for that invocation. To apply a
previously reviewed candidate without rerunning inference, call MCP `texture_import` using the
manifest's target/layer, `candidate.png`, and source revision. If that revision is stale, re-export
and review against the current layer. Restore with `texture_restore(backup_id, expected_revision)`;
obtain the current revision from `texture_layer_info`.

`invert-api.json` is a deterministic real-server test using built-in image processing nodes,
not AI inference. `config.qwen21.example.json` and `workflows/qwen21-iris-api.json` demonstrate
an actual Qwen Image 2.1 edit using model filenames confirmed on the development server. They
require those models and `TextEncodeQwenImage21`/`QwenImage21Cache`; they are not portable model
installation instructions. The 8-step example is a pipeline check, not a quality preset.

```powershell
python tools/comfy-bridge/bridge.py texture-roundtrip `
  --config tools/comfy-bridge/config.qwen21.example.json `
  --target-id '<returned target_id>' --layer-id '<returned layer_id>' `
  --workflow tools/comfy-bridge/workflows/qwen21-iris-api.json `
  --restore-source-alpha
```

For your own workflow, export **API format JSON** and configure `output_node` (exactly one PNG)
and `bindings`: `source_texture`, optional `reference_image`, `mask`, `positive_prompt`, each with
`node_id` and `input`. The bridge never guesses these IDs. Use `--reference-image`, `--mask`, and
`--prompt` only with corresponding bindings. Results are selected by the returned `prompt_id`
and configured output node, then downloaded through `/view`.

## Files, validation and recovery

Each job gets a unique `runs/<uuid>/` directory: source, generated and validated PNGs, submitted
workflow, manifest, and before/front/right/back previews when applied. Plugin backups are in
`backups/`. Existing outputs and `.vroid` files are not overwritten. Backups are retained until
you explicitly remove them; the plugin's backup IDs last only for the current model/preset session.

Both sides bound input to 64 MiB and 16 megapixels. Import requires 8-bit RGBA PNG with exactly the
original dimensions; by default alpha must also match. There is no implicit resize. `--restore-source-alpha` explicitly
copies the exported alpha onto an RGB/RGBA generation; it does not repair UV placement or seams.
The layer is exported as stored: existing VRoid color parameters still tint it in the model.

For deliberate opacity/silhouette changes (such as eyeliner), use `--allow-alpha-change`.
It retains the candidate's RGBA and sends `alpha_mode:replace` to `texture_import`. It is mutually
exclusive with `--restore-source-alpha`; without either option changed alpha is rejected. The
manifest records the selected mode. This does not make an RGB-only AI output a useful makeup mask:
prepare and review an actual transparent atlas, especially before allowing an opaque candidate.
Restore recovers the original alpha and RGB. See [makeup verification](../../docs/makeup-editing.md).

Ctrl+C or `--cancel-file` stops collection/import and deletes only this bridge's pending ComfyUI
job. It never globally interrupts a shared server; a running job can finish after cancellation.
Failures before import leave the layer untouched. The plugin attempts rollback after a failed
mutation and reports `rollback_applied`; a concurrent edit/busy engine can prevent rollback, in
which case the backup path is returned. A lost HTTP response is an **unconfirmed application**,
not proof of failure: inspect `status` and `texture_layer_info` before retrying. A bridge failure
after confirmed import attempts restore only if the revision has not changed.

## Tests

```powershell
python -m unittest discover -s tools/comfy-bridge/tests -v
python tools/comfy-bridge/tests/live_texture.py
```

The live test requires a dedicated VRoid instance at the model-selection screen. It creates a
test model, verifies no-op import, invalid inputs, changed renders, exact restore, stale session
IDs, and save/reopen, and leaves its own test model open. It refuses to discard an existing session.
In-plugin test: `test texture_roundtrip <path.vroid>` from the model-selection screen; this test
uses production handlers, writes `texture_roundtrip_result.json`, and closes its test app on success.
