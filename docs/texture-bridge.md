# Texture bridge implementation report

This branch implements the P0 editable-layer round trip and a separate ComfyUI companion CLI.
The initial verified target is a real iris layer; the subsequent [makeup extension](makeup-editing.md)
also verifies alpha-changing eyeliner. Coverage of all materials is not claimed.

| Phase | Status | Evidence / remaining work |
|---|---|---|
| 0: environment and baseline | Complete | App clone with BepInEx; build and 26 baseline protocol checks passed |
| 1: read/export | Complete for initial target | Real 1024x512 iris layer, RGBA and hashes; UV guide unavailable |
| 2: import/restore | Complete for initial target | History command, backup, revision guards, rebuild signal, pixel read-back, stable render, save/reopen |
| 3: ComfyUI bridge | Complete for initial target | Mock, real image-processing graph and existing local Qwen 2.1 inference; job-specific download and alpha checks |
| 4: custom items | Partial | Read-only engine discovery added; no installed item available for apply/persistence testing |
| 5: VRM | Research only | Entry points and remaining metadata/completion work documented; no export tool |

## Implementation

- Plugin: six original texture tools, the subsequent `texture_prepare` extension, and `custom_item_list`. Existing MCP/raw transports dispatch to the
  same main-thread handlers. `Editor/TextureEditor.cs` owns version-specific engine access.
- `texture_workspace` setting controls PNG I/O. Backups use unique filenames and are retained.
  Import requires native dimensions and, by default, exact alpha; explicit `alpha_mode:replace`
  supports transparency edits. IDs/revisions reject stale edits.
- Completion requires a model rebuild, exact layer pixels and three stable, nonempty preview
  frames. Timeout/failure reports rollback outcome and backup location. Automatic recovery cannot
  be guaranteed after concurrent changes, process loss or an unresponsive engine.
- Python CLI keeps inference outside Unity, binds configurable workflow inputs, selects outputs
  by prompt ID, bounds PNGs, defaults to preview-only, supports explicit apply/alpha restoration,
  and records source/workflow/result/preview manifests. Cancellation never interrupts the shared
  server globally. Lost import responses are marked unconfirmed for manual reconciliation.
- Added production-handler in-plugin test, live external texture checks and unit/mock bridge tests.
  Fixed the upstream external harness's hardcoded `/tmp` directory assumption for Windows.
  Save now rejects directory destinations before calling the native engine; restore verifies
  that a backup still contains the original pixels before applying it.

## Running this checkout on Windows

The prepared development app is `run/game/VRoidStudio.exe`, separate from the user's installed app.
Only one instance should serve the configured ports. Preserve/save its current model before restarting.

```powershell
dotnet build plugin/VroidStudioMcp/VroidStudioMcp.csproj -c Release
Copy-Item plugin/VroidStudioMcp/bin/Release/VroidStudioMcp.dll run/game/BepInEx/plugins/
# Restart the prepared app after deployment; BepInEx cannot hot reload.
python tools/comfy-bridge/bridge.py healthcheck --config tools/comfy-bridge/config.example.json
python tools/comfy-bridge/bridge.py targets --config tools/comfy-bridge/config.example.json
```

See the [bridge guide](../tools/comfy-bridge/README.md) for commands, workflow bindings, mock and
Qwen examples, preview approval and recovery. The [external API](external-api.md#editable-textures)
is the authoritative tool contract. [Interop findings](texture-interop-findings.md) record exact
engine methods, environment, QA locations, the hidden-window rendering issue and remaining scope.

## QA artifacts

Local evidence lives under ignored `run/qa/` and `run/game/texture-workspace/`; no model/texture
binary is added to Git. `qwen-roundtrip.json` points to source/candidate PNGs, front/right/back
renders and the saved `qwen-emerald-iris.vroid` example. Initial black preview captures are retained
as diagnostic evidence only. The Qwen result contains green centers with existing brown tint;
this is a pipeline demonstration rather than a finished character design.

Validation results and any failed initial attempts are recorded in
`texture-interop-findings.md` and local test logs. Only the cloned app and test models were modified;
the initially running installed app was left intact. No commits or pushes were made automatically.

Final validation: Release build with zero warnings/errors, 21 bridge unit/mock tests, 17 live
texture checks, and the production-handler texture round-trip test passed. Existing external
regression checks passed across successive runs, including all 17 checks in the final render
suite after fixing directory-save validation. See the findings for the exact run breakdown.
The development app was left in Korean with the saved Qwen iris demo open and ready; its final
stable face preview is `run/qa/final-demo-preview.png`.
