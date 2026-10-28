# vroid-studio-mcp

[English](README.md) | [Русский](README.ru.md)

![vroid-studio-mcp promo](docs/media/promo.gif)

`vroid-studio-mcp` is a **VRoid Studio 2.14.0 extension with an MCP interface**. A BepInEx plugin
inside the app lets AI agents create and edit characters through its internal API. Experimental
tools also connect the app to ComfyUI for textures and Blender for custom face geometry and
accessories.

MCP is the control interface. The plugin also supplies the runtime that displays custom geometry
inside VRoid Studio and restores it from saved models.

## In practice: Asuna character study

An ongoing fan-made study of **Asuna (Blue Archive), school uniform**, using Mx2J's official standing
artwork as the primary reference. Native face parameters, hair strands and clothing are combined
with edited textures, fitted eye details, accessories and a halo.

![Asuna V49 in the VRoid Studio viewport, three-quarter view](docs/images/asuna/asuna-v49-three-quarter.png)

| Full body | Face and eye detail |
|---|---|
| [![Asuna V49, full-body front view](docs/images/asuna/asuna-v49-body.png)](docs/images/asuna/asuna-v49-body.png) | [![Asuna V49, face close-up](docs/images/asuna/asuna-v49-face.png)](docs/images/asuna/asuna-v49-face.png) |

These are unchanged app-preview captures from the **saved and reopened V49 model**, not generated
concept images or Blender renders. V49 passed 56 recorded runtime/preservation/persistence checks;
153 Blender shape-key arrays and packed PNG assets were also checked. These are technical checks,
not a claim of finished likeness. Hair, ribbons and clothing remain work in progress; the later
V50 hair-ribbon candidate is not shown. [Screenshot provenance](docs/images/asuna/README.md).

This example required repeated reference comparisons and targeted edits. It does not demonstrate
automatic one-image-to-character reconstruction. The character model and third-party source assets
are not bundled with this repository.

## What is saved, and what needs the plugin?

| Layer | Role | Reopening |
|---|---|---|
| Native VRoid edits | Presets, parameters, colors, native hair and editable textures | Saved as native model data; independent of the custom-geometry runtime |
| Experimental geometry extension | Same-source face corrections and fitted details such as liner, scalp insets, collars, ribbons and halo | Package and textures embedded in `.vroid`; this plugin must be loaded to display them |
| External authoring tools | ComfyUI texture workflows and Blender mesh/bake work | Needed for the corresponding authoring step; not needed just to reopen an embedded result |

### Open an extended model correctly

1. Launch **the VRoid Studio installation containing BepInEx and `VroidStudioMcp.dll`**. If you keep a
   separate working copy, launch its `VRoidStudio.exe` explicitly. Double-clicking a `.vroid` can
   launch a different, unmodified installation through the file association.
2. Open the saved `.vroid` from inside that app and let the model finish loading. The plugin
   restores the embedded geometry automatically; no separate JSON import or running Blender/ComfyUI
   session is required for a saved embedded result.
3. If custom parts are missing, check that the plugin loaded and inspect `face_bridge_status`
   through MCP. An active package should report matching saved/active hashes and no runtime error.
   Source-mesh changes or loss of the package carrier require the recovery steps in the
   [bridge guide](docs/face-bridge.md#compatibility-and-recovery).

**Opening in an unmodified VRoid Studio does not activate the custom geometry.** Saving a `.vroid`
does not make these additions native editable VRoid items. Arbitrary Blender-scene import, native
VRM export of the extension, cloth simulation and comprehensive expression/animation support are
outside the verified workflow. Keep the extended model and a checkpoint before further native edits.

## Features

- **Experimental eye makeup** — explicit alpha replacement can change eyeliner opacity and
  silhouette, with exact RGBA backup/restore. Stock eyeliner, eyelash and FacePaint eyeshadow
  edits, visible render changes and save/reopen are verified. FacePaint assets can be prepared
  as editable layers with a model checkpoint. [Scope and examples](docs/makeup-editing.md).
- **Experimental hair geometry editing** — inspect hair groups/strands, move a non-root brush
  control point, and restore the edit. Includes backups, revision checks and render verification.
  Save/reopen is tested on a front-hair fixture; automatic hairstyle reconstruction and guide
  editing are not implemented. [Contract and rendering requirements](docs/external-api.md#hair-geometry).
- **Character creation and editing** — create or open a character, let the agent build it, and save
  it (`.vroid`).
- **Editor controls** — work through supported sections (`Face`,
  `Hairstyle`, `Body`, `Outfit`), categories, presets, and individual sliders/colors.
- **Live visual feedback** — the agent receives rendered previews of the model and preset
  thumbnails, so it decides by looking instead of guessing.
- **High-quality preview render** — configurable MSAA and supersampling (SSAA) plus anisotropic
  filtering keep the preview clean and crisp for the agent to judge.
- **Readable preset names** — VRoid ships no human-friendly names; the plugin supplies them, so
  presets are told apart by meaning rather than an internal id.
- **Corrected English labels** — the editor's English translation gives different sliders the same
  name; the plugin fixes these in the interface.

## How it works

- The plugin attaches to VRoid Studio's **own internal API** and calls it directly. Commands use
  engine methods rather than mouse/keyboard simulation; visual feedback comes from rendered previews.
- Blender additions use a custom renderer managed by the plugin. The matching package is embedded
  through the app's serializer and restored after opening or supported model rebuilds. See the
  [face and accessory bridge](docs/face-bridge.md).
- Because it never touches the GUI, it **keeps working while the window is in the background or
  minimized** — start a long edit, switch to another app, and the agent keeps going.
- **Wine is supported out of the box.** If you run VRoid Studio through Wine on Linux, paths are
  translated in both directions automatically, so agents and scripts on your computer can just use
  normal Linux paths for open/save — there is nothing to configure.

## Requirements

- **VRoid Studio 2.14.0** (Windows, or run through Wine) — [official website](https://vroid.com/en/studio).
- **BepInEx 6 for Unity IL2CPP** — download
  [BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip).
  BepInEx bundles everything else the plugin needs.
- **Optional authoring tools:** ComfyUI for external texture workflows, Blender and Python for the
  experimental geometry bridge. They are separate installations and are not required for basic
  editor control or reopening an already embedded model.

## Installation

1. Install the BepInEx build from **Requirements** into your VRoid Studio folder, then launch the app
   once so BepInEx finishes setting itself up.
2. Copy the plugin file into BepInEx's plugin folder, so you end up with:
   ```
   <VRoidStudio>/BepInEx/plugins/VroidStudioMcp.dll
   ```
3. Copy the two data folders into the VRoid Studio folder (the one containing `VRoidStudio.exe`):
   ```
   <VRoidStudio>/i18n/      # corrected English labels
   <VRoidStudio>/labels/    # readable preset names
   ```
4. Optional: copy the included BepInEx config to keep log files off:
   ```
   <VRoidStudio>/BepInEx/config/BepInEx.cfg
   ```
5. Start VRoid Studio. The plugin is now serving an MCP endpoint at
   `http://127.0.0.1:17891/mcp`.

### What's in the release

The archive ships **only this plugin and its data** — never VRoid Studio itself and never BepInEx
(both are things you install separately):

```
BepInEx/plugins/VroidStudioMcp.dll   the plugin
BepInEx/config/BepInEx.cfg      BepInEx config with logging turned off
i18n/                           corrected English labels
labels/                         readable preset names
README.md  README.ru.md  LICENSE
```

## Getting started

Point your MCP client at the local endpoint. For example:

```json
{ "mcpServers": { "vroid-studio-mcp": { "url": "http://127.0.0.1:17891/mcp", "type": "http" } } }
```

Then just ask the agent for what you want, for instance: *"Create a female character, give her a
short bob and green eyes, then save it to my Documents."* The agent inspects the current sections,
presets and parameters, applies changes, and checks the result on the rendered preview.

## Configuration

Settings live in `<VRoidStudio>/BepInEx/config/com.kokokoshka.vroidstudiomcp.servers.json`. The defaults
are sensible; the ones you might care about:

| Setting | Default | What it does |
|---|---|---|
| `enabled` | `true` | turn the control server on/off |
| `msaa_samples` | `4` | smooths the preview (applied on restart) |
| `render_scale` | `2` | renders the preview at higher resolution for a crisper image |
| `aniso_filtering` | `true` | sharper textures at an angle |

If the preview feels slow, set `render_scale` to `1`.

The address (and these toggles) can also be edited inside VRoid Studio: open **Settings** from the
account icon and look for the **Programmatic Control (MCP)** section at the top.

## Experimental texture editing and ComfyUI

Editable-layer discovery, PNG export/import and backup restoration are available through MCP.
The first verified target is an iris layer: real ComfyUI/Qwen editing, changed 3D preview, exact
restore and `.vroid` save/reopen have been tested. A separate Python bridge supports configurable
API workflows, preview-only review, alpha validation and a deterministic mock mode.
See [setup and commands](tools/comfy-bridge/README.md) and [verification/limits](docs/texture-interop-findings.md).
Custom items currently support read-only discovery; import/apply and VRM export remain unimplemented.

## Documentation for developers

- [docs/internal-api.md](docs/internal-api.md) — the reverse-engineered internal API of VRoid Studio.
- [docs/external-api.md](docs/external-api.md) — the control surface exposed to clients (MCP).
- [docs/architecture.md](docs/architecture.md) — how the plugin is built and how to extend it.
- [docs/character-editing-harness.md](docs/character-editing-harness.md) — reference-driven character
  editing, lessons from visual failures, verification gates and session records.
- [docs/quirks.md](docs/quirks.md) — engine behaviours that are easy to get wrong.
- [docs/macos.md](docs/macos.md) — macOS status.
- [AGENTS.md](AGENTS.md) — build/test workflow and conventions.

[docs/artist-agent-prompt.md](docs/artist-agent-prompt.md) is an example system prompt for an agent
that acts as a character artist.

## macOS

macOS is **still under research** and not part of this release. On Apple Silicon it is achievable
(the plugin itself is platform-independent), but it needs BepInEx/Il2CppInterop builds that are not
yet part of BepInEx upstream. See [docs/macos.md](docs/macos.md).

## Experimental Blender face bridge

Same-source Blender eye/face studies can be returned to the app with `face_import`, saved inside
a `.vroid`, and restored on reopen. The same extension supports fitted accessories with supported
bone bindings. **The plugin is required to display custom geometry.** The exporter targets the
matching captured source mesh and its sampled poses; it is not a general `.blend` importer.
VRM export integration is unverified.
See the [workflow and limitations](docs/face-bridge.md) and [API](docs/external-api.md#blender-face-bridge).

## AI disclosure

Most of the reverse engineering and the initial implementation were produced with AI coding agents:
**DeepSeek V4.1 Flash** running in **OpenCode**. Every AI-generated result was manually reviewed and
verified against the running app (deterministic in-plugin tests and runtime checks); nothing was
merged without validation.

## Legal

This is an interoperability and automation project. VRoid Studio, BepInEx and other third-party
components belong to their owners and are not included. Use at your own risk.

The Asuna screenshots illustrate an unofficial fan-made technical study. Blue Archive character
designs and third-party assets remain the property of their respective rights holders; the code's
MIT license does not grant rights to those designs or assets. This project is not affiliated with
or endorsed by their owners.

## License

[MIT](LICENSE) © Ilya Chelyadin and vroid-studio-mcp contributors.
