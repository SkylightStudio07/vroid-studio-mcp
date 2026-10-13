# macOS: state

macOS support for VRoid Studio (Unity `6000.0.62f1`) **works on native arm64**
(Apple Silicon). The build/fixes of the stack live in a separate repository `il2cpp-macos-reverse`
(`tools/bepinex-arm64/`); here — only a summary for the context of the plugin.

## What works

- Native **arm64** BepInEx IL2CPP: the plugin loads, Harmony patches are applied, command-server
  (17890) and MCP (17891) come up. End-to-end on VRoid-arm64 verified.
- interop generation is solved (`Cpp2IL` + `cpp2il-machofix`, LTO/`Master`); managed-interop
  is architecture-independent, so the interop from the **x86_64** slice is used.
- Known limitations (do not block VroidStudioMcp): on arm64 the runtime hooks are skipped
  `Class::GetDefaultFieldValue`, `MetadataCache::GetTypeInfoFromTypeDefinitionIndex`,
  `GenericMethod::GetMethod` (x86-only target discovery; VroidStudioMcp does not use them).

## Plugin invariant (macOS/arm64)

A managed exception **cannot** cross the native detour: coreclr goes into livelock
(`PAL_DispatchException`), and a bare Dobby detour crashes the process. Therefore **the body of every
Harmony patch must be wrapped in try/catch** and not let an exception out.

## No longer relevant

- **Rosetta / x86_64.** Inline hooks (Dobby) do not work under Rosetta-AOT → the platform is deemed
  unsupported. Historical bring-up — in `il2cpp-macos-reverse/tools/macos-sandbox/`.

Full registry of improvements/workarounds: `il2cpp-macos-reverse/inventory-macos-arm64.md`.
