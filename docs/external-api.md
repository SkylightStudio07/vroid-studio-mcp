# External API

This document defines how VRoid Studio is exposed to clients. It is the client-facing contract: the
model of the editor (sections → categories → presets/parameters), the semantics of each operation,
and the transport details a client must respect.

## Transports

Editor control is offered over MCP (Streamable HTTP), with a raw line protocol as a
debugging/scripting companion. Both dispatch to the same `CommandDispatcher` on the Unity main
thread; command and tool names are identical.

| Transport | Address | Purpose |
|---|---|---|
| MCP (Streamable HTTP) | `http://<host>:17891/mcp` | primary client interface (`initialize` / `tools/list` / `tools/call`) |
| Raw line protocol | `tcp://<host>:17890` | debugging/scripting; text line or JSON-RPC 2.0 |

Configuration is stored in `<BepInEx>/config/com.kokokoshka.vroidstudiomcp.servers.json`; the address and
the `dev_tools` flag are also editable from the in-app Settings dialog, section **Programmatic
Control (MCP)**.

## Design principles

- The interface mirrors the editor's UI structure rather than exposing a flat list of every editable
  parameter: a client navigates section → category → (presets / parameters).
- Only stable `id`/`label`/`value` leave the plugin; Il2Cpp handles, RVAs, marshaling and
  event-based synchronization stay internal.
- Every mutating call is **blocking for the client**: the request enters the main-thread queue, the
  client waits, and the operation resolves on an engine event (`UpdateSelectedPresets` /
  `IsUpdatingModel true→false`), with a timeout as fallback. Screenshots are captured only after
  resolution (capturing during a rebuild throttles it).
- `initialize.instructions` returns the workflow, semantics and formats, and section labels are
  listed in the tool descriptions, so `sections` is not required as the first call.

## Threat model

The service targets a **local, honest but careless client** on loopback; it does not defend against
attackers.

- For any invalid, missing or wrong-typed argument the client receives a clear error; the server does
  not return HTTP 500 and does not leave the session ambiguous — `ping` (MCP, bypassing the queue)
  and `state` must keep responding. This is covered by the `misuse` test suite.
- Deliberately **outside the threat model**: huge requests (batches of tens of thousands of
  operations, megabyte strings/labels). This is a DoS, not carelessness; the effect is a temporary
  main-thread stall, cured by a restart.
- Guards against honest mistakes: `open` rejects non-files and non-regular files (directories,
  special files); the camera rejects non-finite and absurd magnitudes (`|v| > 1e6`); numbers, colors,
  indexes and preview ranges are range-checked.
- MCP enforces a loopback check on `RemoteEndPoint`; the TCP front does not. Non-loopback addresses
  are therefore not supported (file commands operate on paths of the machine running the app).

## Session state

`state` → `{ready, updating, selectors, in_edit, section}`.

- `in_edit` — a model is being edited (true after `open` or activation of the edit root; false on the
  model-select screen and before the first open). Tracked by the `EditRootScreen.OnScreenActivate` /
  `BackToModelSelect` hooks with a plugin-owned `_inEdit` flag, because `IsEditModelActive` is not
  reset when returning to the menu.
- `ready = modelReady && in_edit`; `updating` — a rebuild is in progress (`IsUpdatingModel
  true→false`); `section` — the current section, or `null` outside the editor.
- `open` returns once `in_edit` and `ready` are true (model built, camera reset).

## Sections

- Exposed sections: `Face`, `Hairstyle`, `Body`, `Outfit` (UI labels; the enum mapping stays internal,
  `*Editor` is neither returned nor accepted).
- **Case-sensitive** (`Ordinal`): section and category must match the labels returned by
  `sections`/`categories` (`face`, `irises` → `unknown section`, `no tab`). The errors are
  actionable: `unknown section` lists the valid labels, `no tab` lists the tabs of the section.
  Parameter `label`/`key`/`path` matching is case-insensitive.
- `Accessories` is not exposed yet (no preset selector; not critical).
- `Look` (light/shadow) is not exposed (subjective, few parameters).
- `sections` → `[{id, title}]`; available offline.

## Categories

`categories(section)` → `{section, categories:[…]}`. The call switches the editor to `section` and
returns it; each entry carries a kind and counters:

- `kind` — `parametric` if the category has its own adjustable parameters (numeric/colors), otherwise
  `preset-only`. Rules: Face/Body — by the presence of parameters; Hair — all tabs are `preset-only`
  except `Base Hair` (which has its own color); Outfit — `preset-only` at the tab level, because the
  sliders belong to the applied item (`enter` after selecting an item reports `parametric`).
- `params` / `colors` — the number of numeric / color parameters.
- `presets` — the number of presets; `has_clear_preset` — whether the tab has an empty (Clear) slot
  that removes the item entirely.

`has_clear_preset` semantics:

- `true` — the tab has a Clear slot selecting "nothing". While the tab is empty, `enter` returns an
  empty list and `needs_preset:true`; after any real preset is selected the parameter set is the
  same. This flag is not equivalent to `needs_preset` (a tab with a Clear slot may already be filled).
- `false` — no Clear slot; a preset is always applied and **replaces** all parameter values.

Kind and counters are read from the eager VM collection and are correct immediately, without warming
up by entering. Outfit is the exception: its sliders belong to the applied item, so its tabs always
report `preset-only`/`0` and the authoritative source is `enter` after selecting an item.

**`Expression Editor` is masked.** The node exists in the engine tree but is not exposed: it is not
listed by `categories`, and `enter`/`presets` respond `no tab`. Its data lives in
`FaceEditor.ViewModel`, not in the category tree (see `internal-api.md` §7).

**Preset names.** VRoid provides no human-readable names. `presets` returns a single `name` field per
preset: a human-readable label when known, otherwise the internal identifier; the empty/Clear preset
is named `"(clear)"`. The label source and maintenance are internal (see `internal-api.md`).

### Layers and overlap

- **Face: one key — one parameter.** `enter` returns a stable `key` (transferable key) for numeric
  parameters. The same `key` in different tabs is the same parameter: `Face Sets` aggregates
  eye-shape/contour/ears and shares keys with `Eyeliner`/`Eyebrows`/`Eyelids`/`Skin`. Selecting a
  preset in any such tab overwrites all keys it owns, including those visible in other tabs. There is
  no per-tab isolation; use the specific tab when editing a specific element, and expect a preset to
  overwrite shared keys.
- **Whole Sets (Outfit)** is `preset-only` with no own parameters: selecting a set applies items into
  other slots (`Tops`/`Bottoms`/`Dresses`/…). Fit is adjusted with sliders in those slot tabs
  afterwards.
- Clothing: `Tops` and `Bottoms` coexist; `Dresses` (Onepiece) is mutually exclusive with them —
  selecting a dress clears the top/bottom, and selecting a top or bottom removes the dress. Each slot
  remembers its item; no hidden/overlapped element is left behind.
- Parameters/colors of an overlapped or invisible element still apply, but are not visible until the
  element is uncovered (the upper layer removed). Likewise for an iris hidden by hair: the effect
  cannot be judged from the frame while the eye is not visible. This is correct engine behavior, not a
  plugin bug.

## Category and parameters

`enter(section, category)` → `{kind, parameters[], globals?, item?, needs_preset?}`.

- `parameters` is a **single list** of the category's own parameters, numeric and color alike. Each
  entry is `{index, path, kind: "float"|"combined"|"color", label, value, min?, max?, key?, global?}`.
- `path` is stable across repeated `enter` calls (`Section/Category/Name`; for global colors
  `Section/Name`); `index` is short-lived and valid only until the next `enter` or model change.
- `label` and the category/path strings are **language-bound** (they mirror the editor UI, so the
  agent's terminology matches the user's); `key` (and the color `id`) are language-independent.
  Prefer `key` for language-independent addressing.
- `key` is the stable system id (transferable key) when available. Numeric parameters are
  `SingleParameterViewModel` / combined variants (`Value` / `CombinedValue`).
- Color is a parameter too (`kind="color"`, value `#RRGGBB`).
- When two parameters collide on a label within a tab, `path` is disambiguated with ` #1`, ` #2`, …
  (the full `path` is unambiguous).
- `globals` holds the section's **shared** colors (hair Main/Emission, skin base). These are single
  values addressed from several tabs via a section-level path (`Skin/Color`, `Hairstyle/Main`) and
  are therefore not duplicated into `parameters`. Their entries still carry `global:true`.

Setting: `set_param(section, category, <path|label|key|index>, value)`, dispatched by `kind`
(number or `#RRGGBB` / `r,g,b`). `path` is preferred; `label`/`name`, `key` and `index` are also
accepted. `r,g,b` must be in 0..1; `NaN`/`Infinity` are rejected (they send the engine into an
infinite rebuild). Numeric read-back is rounded to 3 digits as in the UI; writes are clamped to the
union of `[min,max]` and the current value; the color echo is normalized to `#RRGGBB`.

### Colors as parameters

- **Eyes (Iris)** — three separate color parameters (`Both`, `R Eye`, `L Eye`). `Both` changes both;
  `R`/`L` change one side (enables heterochromia). Verified by `eyetest`.
- **Hair** — two global colors, `Main` and `Highlight` (Emission), on Hairstyle tabs except
  `Base Hair`. The value is shared, so the path is tab-agnostic: `Hairstyle/Main`,
  `Hairstyle/Emission` (returned in `globals`), settable from any non-Base tab. The color panel
  requires the Level-0 preset `Hairstyle Sets`; while it is active the globals are available on any
  non-Base tab. The section has no numeric parameters. `Base Hair` has its own single color,
  `Hairstyle/Base Hair/Main Color`; the globals do not apply to it.
- **Skin** — the base color (Color/Dark/Light) is one value shared across `FaceSkin`/`BodySkin`/
  `NoseLighting`/`Eyelid`; it is returned once in `globals` as `Skin/Color`, `Skin/Dark Color`,
  `Skin/Light Color`, addressable from any of those tabs, with no per-group duplicates.

Stock selection uses `Presets` (`SelectedItemGroup=Preset`). `custom_item_list(section, category)`
provides read-only installed-item discovery and reports item/group identity, revision, name and
selection state. It switches section but does not select an item. Custom-item import/apply remain
unsupported; each response explicitly reports this limit.

## Hair geometry

Experimental brush-strand point editing, verified on a stock front-hair preset in 2.14.0.
This is separate from the hairstyle preset/color interface. MCP point tools do not generate a
hairstyle from an image, add strands, edit guides, or change procedural-hair parameters. Additional
raw commands below expose bounded brush-group width and per-strand width profiles.

| Tool | Arguments (also raw-protocol order) | Result |
|---|---|---|
| `hair_list` | none | `groups` with engine group/transferable IDs, group type, brush/procedural kind, visibility and `strands`; current `revision`, `workspace_root`, `max_delta` |
| `hair_get` | `strand_id` | indexed `points` with position/normal/UV, revision, `geometry_sha256`, guide-origin matrix, move capability |
| `hair_move_point` | `strand_id,point_index,dx,dy,dz,expected_revision` | verified revision/hash, point index, backup ID/path, stable preview hash |
| `hair_restore` | `backup_id,expected_revision` | same mutation result; also backs up the current point edit |

Additional character controls are experimental **raw TCP commands only**, absent from MCP
`tools/list`. Use them on a saved copy. Their positional arguments are:

| Command | Arguments | Behavior |
|---|---|---|
| `hair_visibility` | `strand_id true\|false expected_revision` | Stock strand visibility command; native read-back, rebuild and stable-frame verification. Geometry is retained. |
| `hair_mirror_front` | `expected_revision` | Mirrors each current front brush group through the stock flip command. |
| `hair_offset_front` | `dx expected_revision` | Offsets each front brush group along native guide X; finite nonzero `abs(dx)<=0.15`, resulting offset bounded to `abs(x)<=0.3`. |
| `hair_offset_group` | `engine_group_id dx expected_revision` | Same offset for one current front brush group. |
| `hair_scale_back` | `factor expected_revision` | Multiplies guide height for back brush groups whose **current** height is at least 0.6. Factor 0.5..2.5; resulting height 0.02..4 native units. Short crown guides are excluded. |
| `hair_group_width` | `engine_group_id width expected_revision` | Sets one current front or back brush group's native width scale to 0.05..4 through its parameter history command. Strand overrides remain intact. |
| `hair_profile` | `strand_id x:y,x:y,... expected_revision` | Sets one brush strand's native width curve using `SetHairStrandCurveCommand`. Accepts 2..16 pairs, strictly increasing X in 0..1, Y in 0..1.5, and endpoints `0:0` and `1:0`. |
| `hair_smooth` | `strand_id expected_revision` | Runs `SmoothBrushHairStrandsCommand` on one brush strand. Saves a whole-model checkpoint first, waits for model readiness and stable frames, and returns old/new geometry hashes and point counts. Native smoothing may resample points and change their normals/UVs; re-read points before further edits. |
| `expression_list` | none | Native expression category keys, ordered parts, physical parameter keys, values/ranges and current revision. |
| `expression_set` | `category_key part_index physical_key value expected_revision` | Sets a saved expression parameter through its native view model. Finite value, native range, exact revision, completed rebuild and value read-back are required. |
| `expression_preview` | `category_key` or `reset` | Shows the selected expression in the face editor, or resets temporary mesh weights. Does not rewrite saved expression values. Requires three identical nonempty frames. |

Expression part indices come from the current `expression_list`; do not assume a physical key
occurs in only one part. The native Fem/Masc parts can have different shapes. Changing saved
settings and displaying their expression are separate operations: select a preview explicitly,
including after reopening. `reset` resets the temporary preview, not the saved Neutral mapping.
`expression_set` does not create an automatic checkpoint or promise rollback on a rebuild failure;
save a `.vroid` before a sequence of expression edits.

Group mirror/offset/scale operations save a unique full-model checkpoint before editing. The
checkpoint changes the active save path. Results include `groups_modified`, `checkpoint_path`,
`revision` and a stable preview hash. Group operations are sequential and **not atomic**; on
failure inspect the reported count and reopen the checkpoint to recover. They invalidate hair
handles and point backups; enumerate again. Scale selection depends on current guide height,
so a reciprocal scale is not guaranteed to target the same groups after crossing the 0.6 threshold.
Scaling all crown guides can create spikes. These controls do not solve collisions, guide fitting,
accessory construction or reference reconstruction. Native offset/guide callbacks stay rooted for
history/undo and are capped at 256/128 callbacks per app process; restart after saving if reached.
Native group operations can reproject control points and normals. Applying an opposite offset
or reciprocal scale is not an exact restore contract; recover through the full-model checkpoint.

Width and profile edits save a unique whole-model checkpoint before mutation and require a
completed model rebuild, native read-back and three identical nonempty preview frames. Recover
from their returned checkpoint if verification fails; these operations do not provide an atomic
rollback. Width setters invalidate hair handles; enumerate again. Width-profile editing retains
control points and their normals/UVs. `hair_get.native_parameters` exposes the native strand
width override, width-curve ID and `width_curve_points`; a null override is distinct from zero.
`hair_list` also reports group `thickness_scale`, `division_count` and `cross_section` for inspection.
The curve controls ribbon width along its length, not its 3D trajectory. Width/profile controls
were live-tested for unchanged other strands, input guards, render change, exact width restoration
and save/reopen persistence; see `tools/api-tests/hair_width_tests.py`. Group-width parameter
callbacks are retained for native history and capped at 128 per app process.

Native smoothing is a separate operation from position-only point edits. Its numerical result is
owned by the app; it does not preserve the old point count or indices and does not promise a
particular artist-facing curve. Successful execution requires a completed rebuild, unchanged
revision during verification and three identical nonempty frames. Recover through the returned
checkpoint on an error; it does not perform a blind history undo. Review the strand's new shape
and depth in several views before saving. The studied three 8-point inner-hair strands were each
resampled to 6 points; other strands, group settings and visibility remained unchanged.

Native hair appearance is also available through MCP and the raw command channel:

| Command/tool | Arguments | Behavior |
|---|---|---|
| `hair_appearance` | none | Global hair `outline_width`, current shared material IDs, native appearance parameters and revision. |
| `hair_outline` | `width expected_revision` | Sets only global hair outline width through `ModifyGlobalParametersCommand`; the API conservatively accepts 0..1. |
| `hair_outline_color` | `material_id color expected_revision` | Sets the current shared hair material's `OutlineColor` through `SetMaterialColorCommand`. Color uses `#RRGGBB` or three 0..1 components. |
| `hair_body_probe` | none | Read-only world-space triangle export of active renderers under the current avatar root, with the head transform, model path and revision. Writes a unique JSON under `workspace_root/hair-probes`. |

Outline setters require an exact current revision, save a whole-model checkpoint, wait for the
model rebuild and verify native readback plus three identical nonempty frames. Equal values are
no-ops. Width and color are separate operations; a sequence is not atomic. Checkpoint creation
changes the active save path. Reopen the reported checkpoint after verification failure. Width
callbacks remain rooted for native history and are capped at 128 per app process. Start with a
small width and inspect multiple views: every native hair ribbon can receive a contour, including
overlapping or hidden layers. These controls preserve native hair geometry and other material
parameters, and persist when saving/reopening the model. Custom face-bridge additions have their
own materials and do not inherit native hair outlines. The dedicated fixture harness is
`tools/api-tests/hair_appearance_tests.py`.

`hair_body_probe` bakes temporary copies of active skinned meshes and reads active static mesh
filters without replacing source meshes or materials. It fails above 350000 vertices or 1500000
triangle indices. Each renderer includes one `uv` pair per vertex for source-specific texture
fitting. Positions describe the current editor pose; body collision and coverage analysis
must distinguish hair, skin and clothing through the returned names/materials and use triangles,
not renderer bounds. Native guide-origin matrices alone are not world-space transforms. The probe
also returns the head local-to-world matrix and `rig_bones` (`J_Bip_*` names and local-to-world
matrices) for source-specific coordinate calibration and accessory fitting. Back-group
width isolation, exact restoration and the probe's read-only contract are exercised by
`tools/api-tests/hair_back_tests.py`.

Enumerate IDs first. They last through repeated lists and point moves but expire on model/context
or preset/group replacement. Check `move_supported`; procedural strands are read-only. IDs identify
engine elements, not semantic parts such as bangs. Listing is limited to 1024 groups/4096 strands;
reading is limited to 2048 control points per strand. Excessive models error without truncation.

Movement is a **relative offset in native control-point coordinates**, not screen pixels or a
world-space guarantee. Vector magnitude must be between 0.000001 and 0.05 inclusive, with finite
values and a resulting native position magnitude <= 10. Index zero is locked to preserve the root.
Offsets below point precision are rejected. Normals, UVs and all other control points are preserved.
The guide-origin matrix is diagnostic; do not assume it alone defines a world-space conversion.
Bounds are conservative API limits, not engine slider ranges. These guards do not detect scalp
penetration, self-intersection or aesthetically poor bends; inspect multiple views.

Both mutations require the exact current revision from `hair_get`. A stock history command and
pending-history flush perform the edit, followed by a completed model rebuild, exact full-strand
read-back, three identical nonempty preview frames, and a final revision/read-back check.
No manual GUI dragging or direct model writes are used. Success does not guarantee a visible
change if the chosen point is occluded; external acceptance tests separately verify changed renders.

Before mutation a unique diagnostic JSON is written under `texture_workspace/hair-backups/`.
It records original and intended full point arrays/hashes plus engine identities. Live restore
uses the in-memory snapshot and requires that the entire strand still matches its recorded result;
restore newer edits first. It never performs a blind global undo. Backup IDs expire with the session;
JSON files remain for inspection, but there is no JSON import/recovery tool. At most 128 snapshots
are retained per session. Save a separate `.vroid` checkpoint before a series of edits.

Preflight errors include `STALE_HAIR_TARGET`, `STALE_HAIR_REVISION`, `INVALID_HAIR_POINT`,
`INVALID_HAIR_DELTA`, `HAIR_NOT_SUPPORTED`, and `HAIR_LIMIT`. Mutation failures return error text
containing JSON with `error`, `rollback_applied`, `rollback_error`, `backup_id`, and `backup_path`.
Rollback is attempted only when revision and geometry still match; it verifies restored data.
Concurrent changes, process loss, or native hangs can prevent recovery. A lost HTTP response is
an unknown outcome, not proof of failure; inspect state and geometry before retrying.

**Rendering requirement:** a stock hair fixture exhibited persistent pixel flicker with MSAA 4.
The prepared development app uses `msaa_samples:1`, `render_scale:2`; this produced exact stable
frames. Change the servers JSON and restart to apply. Unstable previews fail with a rollback attempt;
the implementation does not weaken the three-frame equality requirement or change render settings
silently. See [hair implementation findings](hair-editing-plan.md).

## Editable textures

These tools operate on the current model's editable raster layers, not preset thumbnails. Initial
runtime acceptance covers iris, eyeliner, eyelash and prepared FacePaint eyeshadow editing in VRoid Studio 2.14.0. Other targets may be enumerated,
but coverage is limited to active leaf-transferable editable images. Section is inferred from the
engine's group-type name; `group_type` is authoritative.

| Tool | Arguments | Result |
|---|---|---|
| `texture_targets` | optional `section` | `{targets:[{target_id,section,group_type,image_id,width,height,revision,uv_guide_path}],workspace_root,revision}` |
| `texture_prepare` | `group_type,expected_revision` | `{ok,revision,checkpoint_path,targets}`; no-op also returns `already_prepared:true` |
| `texture_layers` | `target_id` | `{target_id,layers:[{layer_id,order,name,visible,opacity,width,height,editable}],revision}` |
| `texture_layer_info` | `target_id,layer_id` | dimensions, PNG `sha256`, raw RGBA `pixel_sha256`, current `revision` |
| `texture_export` | `target_id,layer_id,output_path` | same metadata plus `texture_path`; creates a new PNG |
| `texture_import` | `target_id,layer_id,input_path,expected_revision,[alpha_mode]` | `{ok,target_id,layer_id,revision,backup_id,backup_path,pixel_sha256,alpha_mode,model_ready,stable_preview_hash}` |
| `texture_restore` | `backup_id,expected_revision` | same as import; backs up the current layer before restoring |

The same argument order is used by the raw protocol. All calls use the existing main-thread queue.
`texture_prepare` accepts `TransferableGroupType.N00.Level1.FacePaint`, `.Tops`, `.Bottoms`
and `.Socks` with a nonempty active preset (the latter three share the same full prefix).
Some stock images are assets rather than editable raster layers,
so they are absent from `texture_targets` until prepared. The command uses engine-declared image
keys and the stock `BeginAdvancedEditCommand.OverlayInput` / `SimpleTextureKey` conversion. It
uses `BeginClothingAdvancedEditCommand` for clothing, including its body-mask images. The
FacePaint conversion does not recolor or bake skin tint. It saves a unique `.vroid` under `texture_workspace/preparation/`
before conversion, waits for model rebuild, verifies revision and discovery, and invalidates all
texture IDs. **The checkpoint save changes the current model's save path**; explicitly save-as
to a new output after editing. Preparation itself does not require stable render equality; the
acceptance harness verifies unchanged stable renders and exact contents of existing layers.

Already prepared images are a no-op with unchanged revision, no checkpoint and no ID invalidation.
Missing presets, unsupported groups and stale revisions fail before conversion. Errors after
conversion begins include `checkpoint_path`; preparation is not automatically reversed. Inspect
state before further work, or return to the menu and open the checkpoint to recover the original
structure. PNG-layer restore only restores pixels; it does not undo asset-to-layer conversion.
Other groups, including skin and dresses, are not supported by this preparation tool. The live
`clothing_prepare_tests.py` fixture covers supported schema, stale/unsupported guards, already
prepared no-ops, stable previews and UV probe immutability.

Target IDs expire on session/context changes or replacement of transferable groups (including
presets). Layer IDs are engine node IDs within a target. Import and restore require an exact current
revision from `texture_layer_info` or `texture_export`; **do not reuse** IDs after model/preset changes.
Changing a preset can replace previously imported pixels. Choose presets before editing textures.

Set `texture_workspace` in the servers JSON; default `<app>/texture-workspace`, relative to the app
root. All texture paths must be PNGs inside this directory, relative or absolute. Parent traversal,
alternate data streams and reparse points are rejected. Existing exports are not overwritten.
The input limit is 64 MiB / 16 megapixels, PNG 8-bit RGBA, exactly matching layer dimensions.
`alpha_mode` is case-sensitive: `preserve` (default) rejects any alpha difference; `replace`
explicitly permits the candidate's alpha, allowing makeup silhouette/opacity edits. Neither mode
alters the submitted pixels. Invalid modes fail before reading the input or changing the model.
The same dimension, path, revision, backup and RGBA verification checks apply in both modes.
`texture_restore` restores all original RGBA channels regardless of current alpha and reports
`alpha_mode:restore`; it requires no replacement option. Full transparency and full opacity are
allowed in replacement mode, so review the candidate and multiple model views before applying.
There is no automatic resize or implicit alpha repair. The external bridge can explicitly copy the
source alpha before import. Export preserves stored pixels, including RGB under transparent pixels;
the app's color parameters still affect the final rendered result. UV guides are not available
(`uv_guide_path:null`).

Import saves an original PNG before issuing the engine history command. It waits for a model rebuild,
compares exact decoded pixels, then requires three consecutive identical nonempty preview frames.
Success confirms those checks, not visual similarity or UV quality. A no-op import still records an
engine history change. Restore verifies that the backup's pixels still match the recorded original.
Backup IDs are session-local; PNG files remain on disk after invalidation/restart for manual recovery.

Errors retain the existing MCP `isError:true` text contract. Preflight messages begin with codes such
as `NOT_IN_EDIT_MODE`, `MODEL_NOT_READY`, `UNKNOWN_TEXTURE_TARGET`, `STALE_TEXTURE_TARGET`,
`INVALID_PNG`, `INVALID_PNG_DIMENSIONS`, `INVALID_ALPHA_MODE`, or `PATH_NOT_ALLOWED`. File-system exceptions can also be
returned as text. After a mutation, failure text contains JSON with `ok:false`, `code`, `error`,
`rollback_applied`, `rollback_error`, `backup_id` and `backup_path`. Rollback uses the original history
command and verifies read-back. Concurrent edits, a busy engine or lost session can prevent recovery;
these report `ROLLBACK_FAILED`, not success. A client/network timeout does not cancel a queued
operation: inspect `status` and layer state before retrying. Process crashes require manual recovery.

The companion [ComfyUI bridge](../tools/comfy-bridge/README.md) performs HTTP/inference waits outside
the plugin. It defaults to preview-only; `--apply` explicitly enables import. It validates dimensions
and alpha, keeps job manifests and backups, and never interrupts other users' ComfyUI jobs globally.
Use its `--allow-alpha-change` to forward `alpha_mode:replace`; it is mutually exclusive with
`--restore-source-alpha`. The manifest records the chosen mode. [Makeup findings](makeup-editing.md)
document the tested one-sided stock eyeline atlas and remaining scope.

## Actions

- `select_preset(section, category, index, [params])` — by the index from `presets`. The Clear preset
  is the one whose `name` is `"(clear)"`; without one, index 0 is the first real preset. The Clear
  preset calls `ClearTransferableGroup()`. Returns the applied preset's `name` (label, else resource
  id) or `"cleared"`; `resourceName` is never exposed. With `params=true` it returns an object
  `{name, category, kind, has_clear_preset, parameters, item?}` (the same payload as `enter`), which
  lets an Outfit client obtain the new item's sliders without a second `enter`.
- `set_param(...)` — the single setter for numeric/combined and color values; the global `path` from
  `enter` is preferred, `index` is short-lived.
- `save [path]` — without a path, saves in place (`saved`); with a path, performs save-as
  (`MainActionHandler.SaveSync(path)`, no dialog) and returns the resolved path. Both variants are
  confirmed by a signal rather than a fixed pause (save-as: the file appears; in-place: the dirty flag
  `IsUnSaved` clears); otherwise `{ok:false,error}`. After save-as the path is registered on the
  engine, so a later `save` without a path writes to the new file.
- `presets(section, category)` → `[{index,name}]` (`name`: label, else resource id; Clear:
  `"(clear)"`). The label machinery is internal.
- `create <female|male>` — a new character from a built-in template (the same choice as the stock
  "Create New Model" dialog). Blocking, like `open`: on return the model is built and
  `state.ready=true`. Valid only from the start screen; with a model open it returns
  "first save/main_menu", forcing the caller to resolve pending changes explicitly (`save` →
  `main_menu`, or `main_menu` to discard). Returns `{gender, variant}`.
- `preset_preview_list(section, category, indexes)` and
  `preset_preview_range(section, category, start, count)` — visual selection of presets. The list
  variant takes 1..12 explicit indexes; the range variant walks consecutive presets (`count` 1..12;
  out-of-range is an error, not trimmed). Each result element carries `{index, name}` (the same
  `name` as in `presets`), so no manual join is needed. In MCP the images arrive as inline image
  blocks; `mode` controls writing `preset_<i>.png`. `b64` (range only) returns base64 PNG inline for
  the VLM harness. Caveats: some Face overlays (lips/lashes/cheeks) render on a generic head rather
  than the current model; clothing/hairstyle/eyes reflect the real preset. All previews carry the
  editor's fixed light/self-shadowing (e.g. a pink shadow on the neck), which is not a parameter and
  cannot be removed (the Look section is outside the API).
- `screenshot(mode)` — preview-only PNG. `inline` (MCP default) returns a base64 image without writing
  to disk; `file` writes `screenshot.png` (raw-protocol default); `both` does both. Requires an open
  model; `ui_screenshot` is the exception and works without one.
- `main_menu()` — return to the main menu (resets the dirty flag, no dialog).
- **`batch`** — an array of operations executed sequentially on the main thread with a per-element
  result: `{method, args}` objects (MCP) or an array of commands (raw protocol). MCP `batch` accepts
  any tool name, including `help`. For image-returning operations (`screenshot`, `ui_screenshot`,
  `preset_preview_*` with `inline`/`both`) it appends inline image blocks, each preceded by an
  `[op N] <method>` text marker, capped at 12 images / 8 MB per batch (excess dropped). The raw
  protocol returns text only. Batch size is bounded by `max_batch_ops` (`servers.json`, default 200,
  0 = unlimited); exceeding it is an error before the batch starts. Operation arguments live in
  `args` (raw protocol also accepts `params`); a leading repeat of the method name is tolerated.
  Array values (e.g. `indexes`) are comma-joined.
- **`status`** (MCP) — a liveness/queue probe answered **outside the main-thread queue** (like the
  JSON-RPC `ping`), so it works during a mutation or batch. Returns `{inflight?, inflight_seconds?,
  queued, batch?}`: the current command (if running > 3 s), the queue length, and the batch position
  `{index,total,method}`. Useful to recognize that a timed-out batch is still applying and avoid
  repeating it. No model required.

## Camera

Subcommands: `reset`, `frame`, `angles`, `zoom`, `pan`, `projection`, `rotate`, `view`, `probe`.
Each call is blocking, waits for application, and returns
`{angles:{yaw,pitch}, distance, position, rotation, projection, fov}`; an unknown subcommand or a
missing/non-numeric argument yields `{"ok":false,"error":…}`.

- **`frame <region> [angle]`** — the primary inspection command. `region` sets the framing and orbit
  center: `body|face|head|upper|legs|feet`. `angle` (default `front`):
  `front|back|left|right|threequarter_left|threequarter_right`. E.g. `frame face right` (profile),
  `frame legs threequarter_left`.
- **`angles x y`** rotates around the current center (`x=yaw`, `y=pitch`; model front is `yaw 180`)
  and preserves the current distance and pan — it does not reframe. For a new region or angle use
  `frame`.
- **`projection`** toggles the projection; it is "sticky" — only `projection` changes it, `reset`
  restores the projection captured at open, and `frame`/`angles`/`zoom`/`pan`/`view` leave it alone.
- `frame` and an explicit `<region>` on `enter` re-aim from the front (yaw returns to front); pass an
  `angle` or follow with `camera angles` for other views.
- `rotate` takes a `CameraRotationMode` index; the valid values are listed in the tool schema and by
  `help('camera')` (resolved from the enum at runtime). `view` (numeric) is manual calibration and
  `probe` is diagnostics.
- Requires an open model (`no model open` otherwise).

### Camera policy

The plugin uses its own category→view policy. `ThumbnailCameraConfigResolver.Resolve(tgtype)` returns
the default for every category outside preview generation, so it cannot be reused. The policy maps:
Face → `face`; back hair → `head:back`, side hair → `head:threequarter_*`;
Body/Tops/Bottoms/Dresses → `body`; Socks/Shoes → `feet`; neck/hand accessories → `upper`.

Framing uses `SetCustomView(pos, center)`, where the center (and orbit center) is a fraction of the
avatar height from the renderer bounds and the distance is a fraction of the default; `frame` also
sets the angle. Each camera command waits for the rotation coroutine and one frame
(`WaitCameraIdle`), so `camera …` followed by `screenshot` in one `batch` yields a fresh frame.

## Exit

The agent must not be left with the "unsaved changes" dialog.

- `SaveAndExit()` — save, wait, quit.
- `ExitWithoutSave()` — reset dirty (`MainActionHandler.IsUnSaved=false`), then `Application.Quit()`.
- `BackToModelSelect()` — return to the main menu, also resetting dirty.
- Plugin surface: `QuitClean()`, the `exit` command/tool (`save`/`discard`), and `main_menu`.
  `exit save` waits for the in-place save to finish (with a timeout) before quitting; `discard`
  resets dirty and quits.

## Blender face bridge

Experimental, same-source Blender face restoration. Requires an open, ready model and this
plugin on every reopen. The package is embedded through native FacePaint history/serialization;
no external assets are needed after saving. This does not implement a plugin-free native mesh
importer or validated VRM export. See [the workflow and limitations](face-bridge.md).

| Tool | Arguments | Result |
|---|---|---|
| `face_probe` | none | `probe_path`, renderer count; exports Face/Head and any active bridge geometry/rig metadata inside the workspace |
| `face_bridge_status` | none | stored/active flags, embedded/active SHA-256, byte and added-vertex counts, revision, runtime error, `plugin_required:true` |
| `face_import` | `input_path`, `expected_revision` | status plus whole-model `checkpoint_path` and stable preview hash |
| `face_restore` | `expected_revision` | status after removing the package and settling the native preview |

Raw commands use the same names and positional argument order. Import accepts a regular `.json`
file inside `workspace_root`, excluding reparse points, up to 32 MiB. Schema is
`vroid-face-bridge/1`: at most 20,000 source vertices, 64 additions / 60,000 added vertices,
finite bounded pose arrays, valid UVs/triangle indices and embedded 8-bit RGBA PNGs. Source
vertex count, UV/triangle SHA-256 and rest-world geometry must match. Malformed input, stale
revision and path/compatibility failures are rejected before checkpoint creation or mutation.

An addition may set `bone_name` to `J_Bip_C_UpperChest`, `J_Bip_C_Neck`,
`J_Bip_L_LowerArm` or `J_Bip_R_LowerArm`. Omission retains
the existing head binding and face-morph following. Explicit bone-bound additions use a rigid
single-bone binding and do not inherit facial expressions or sampled blinks. Unknown or absent
binding bones fail validation before checkpoint creation. This supports fitted ribbons, collars,
rolled cuffs and wrist bands. `accessory_binding_tests.py` checks active weights and guards;
it does not validate arbitrary animation combinations. This does not support arbitrary bone
weights, native clothing items, cloth physics or general rig import.

Import saves a new `.vroid` checkpoint, uses `ReplaceTransferableCommand`, waits for a completed
model rebuild, confirms active runtime restoration and requires three identical nonempty preview
frames. Save the imported model explicitly to persist the embedded extension. Failed application
reports the checkpoint when available; inspect status and reopen the checkpoint for full
recovery. Restoration removes the package from native history; save to persist that removal.

Errors include `STALE_FACE_TARGET`, `FACE_PACKAGE_SCHEMA`, `FACE_PACKAGE_GEOMETRY`,
`FACE_PACKAGE_TEXTURE`, `FACE_TOPOLOGY_MISMATCH`, `FACE_SOURCE_POSE_MISMATCH` and
`FACE_TARGET_MISSING`. A stored package can remain inactive after a source mismatch; status
reports the reason instead of applying it to incompatible geometry.

## Developer and debug surface

The raw line protocol additionally exposes commands not published over MCP. They are intended for
reverse-engineering and tooling (label overlays, i18n dumps, render-quality probes, the settings
dialog, full-window screenshots) and are documented in [`internal-api.md`](internal-api.md). Preset
icons are exported by the `icons` command and keyed by `resourceName`; raw `resourceName`/`itemId`
stay on the developer surface (identifier parsing — `internal-api.md` §5).

## Implementation notes

- **Network:** `HttpListener` binds `0.0.0.0:17891`, so the MCP handler explicitly rejects
  non-loopback clients (`RemoteEndPoint` → `403`). The raw command server binds `127.0.0.1:17890`
  directly. Remote clients are not supported.
- **HTTP framing:** every MCP response is sent with an explicit `Content-Length` and
  `Connection: close` (`KeepAlive=false`). Otherwise the runtime uses `Transfer-Encoding: chunked`,
  whose terminator is not flushed under Wine on non-keep-alive connections — the client hangs,
  connections leak, and the listener stops accepting after roughly 140 sequential requests
  (covered by `protocol` → `sequential-300`).
- **Undo/redo** exists in the engine history (`Context`) but is not exposed yet.
- `IsChecked` (`ColorViewModelBase`) toggles an overlay (`*_OverlayColorDisableFlag`); the UI has
  almost no explicit checkboxes, and it is not needed for the current surface.
