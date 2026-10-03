# Landscape Fixer

*Snow Fixer and AutoBlend in one tool: fix the landscape meshes of your load order, in one plugin.*

Landscape Fixer goes through your load order and fixes the meshes that live in the game's own
landscape folders (rocks, cliffs, mountains, tundra, volcanic ground, DLC landscape, ...). It replaces
the two tools it grew out of — **Snow Fixer** and **AutoBlend** — with a single run, a single output
folder and a single ESL-flagged plugin. Your original files are never touched, and you can remove the
output any time.

Works with **Skyrim Special Edition and Legendary Edition** (PBR options are SE only), with Mod
Organizer 2, Vortex or a plain Data folder.

## What it does

Two passes share one scan of your load order and one output plugin:

### Blend (formerly AutoBlend)

- Flips **alpha test to alpha blend** on the landscape meshes whose textures have a `statics`,
  `blending` or `blend` sibling, with the matching ZBuffer-write / no-fade shader flags. The winning
  mesh of your load order (ERM, SMIM, vanilla, ...) is patched **in place, at its own vanilla path** —
  no renamed copies, so PG Patcher and DynDOLOD find the files under the names they expect.
- Gives the records that show such a texture a **derived "blend" TextureSet**, assigned through an
  Alternate Texture. The derived TextureSets look like vanilla ones (no `pbr\` paths): PBR conversion
  is left to PG Patcher.
- **Generates the missing textures** for an allowlist of landscape textures, plus the PBRNifPatcher and
  PBRTextureSets json that go with them.

### Snow (formerly Snow Fixer)

- Gives a **snow record** its own copy of the winning mesh (named after the record, next to the
  original) and points the record at it, with the snow fixes applied to the copy only - but **only when
  one of those fixes actually changes something in that mesh**. Otherwise the copy is dropped and the
  record stays untouched. A copy keeps the texture paths of the mesh it came from and the record keeps
  its own Alternate Textures, exactly as in vanilla.
- **Clears baked vertex color tinting** on terrain (LAND) records and on landscape meshes: only where a
  snow-classified texture is involved, or everywhere. "Everywhere" on meshes is done in place on the
  winning mesh, once per mesh, with no extra copies. LAND records that a mod deliberately repainted are
  left alone.
- **Makes footsteps match**: Dirt/Grass collision materials of the snow copies become Snow, per
  collision chunk.
- **Fixes distant snow**: ZBuffer-write / no-fade shader flags on the copies.
- Optional: **Hide Decal Shapes** (no z-fighting with decal-based dynamic snow shaders) and **Ice Snow
  Material** (no projected snow on glaciers).

### Scope

Only meshes with a `landscape` folder in their path are processed (`Meshes\Landscape`,
`Meshes\DLC01\Landscape`, `Meshes\_ResourcePack\Landscape`, ...). Terrain bases of cities, campfires,
furniture and the like are left alone unless they match a pattern of the **Additional Meshes** list.

### Made to be safe

- Only **active** plugins are scanned, so a disabled mod never ends up as a master of the output.
- The plugin lists its masters in the real load order and is ESL-flagged whenever it fits.
- Output Location can't be the game's own Data folder, and an existing output folder is only wiped
  when it's provably Landscape Fixer's own previous output.
- Malformed asset paths are reported and skipped per record; non-ASCII paths are supported.
- Runs fully offline against your files — it never touches the running game.

## Structure

- **`src/LandscapeCommon`** — shared building blocks: file probes (loose files, BSAs/BA2s, MO2
  virtual file system), NIF helpers, the landscape folder detector, texture generation, Alternate
  Texture / TextureSet helpers.
- **`src/LandscapeFixer.Core`** — the orchestrator and the two passes (C#, built on
  [Mutagen](https://github.com/Mutagen-Modding/Mutagen) and
  [niflysharp](https://github.com/Aetherinox/niflysharp)).
- **`src/LandscapeFixer.NativeExport`** — the [DNNE](https://github.com/dotnet/dnne)-exported entry
  points the native shell calls.
- **`native/LandscapeFixer`** — the native wxWidgets shell (CMake + vcpkg + wxWidgets).
- **`native/LandscapeFixerTexTools`** — a small native library on top of
  [DirectXTex](https://github.com/microsoft/DirectXTex) that generates the missing textures.

## Installation

1. Install it like any other mod (MO2: as a regular mod; Vortex: extract into a mod folder).
2. **Disable the output mods of Snow Fixer and AutoBlend** (and uninstall those tools): their output
   would otherwise be scanned as if it were a real mod.
3. Launch it from your mod manager's tool list.
4. Pick your game (SE or LE), point it at your game folder (and your MO2 instance/profile if you use
   MO2), and choose an **empty, dedicated** output folder.
5. Hit Start, then enable the generated output.

Changed your load order? Just run it again — it always starts fresh from what's installed. Suggested
order: Landscape Fixer → AutoSeasons → PGPatcher → DynDOLOD (skip the ones you don't use).

## Building from source

Requirements: Windows, Visual Studio 2022 (MSVC) or the standalone Build Tools, vcpkg (manifest mode),
the .NET 9 SDK, CMake 3.31+ and Ninja.

```bash
cd native
cmake -B buildRelease -S . -G Ninja -DCMAKE_TOOLCHAIN_FILE=<path-to-vcpkg>/scripts/buildsystems/vcpkg.cmake -DCMAKE_BUILD_TYPE=Release
cmake --build buildRelease --config Release
python ../scripts/package.py 0.1.0   # optional: assemble publish/LandscapeFixer-0.1.0-win-x64.zip
```

The C# projects are built and published as part of this CMake build (via DNNE). The result ends up in
`native/buildRelease/bin/`. Run the build from a Developer Command Prompt (the texture tools copy
`vcomp140.dll` from the toolset's redist folder).

## Credits

- Lineage: Snow Fixer and [AutoBlend](https://github.com/Cl3mus33/AutoBlend), the latter adapted from
  [AutoSeasons](https://github.com/Kesta-Dev/AutoSeasons), which is built on
  [PGPatcher](https://github.com/hakasapl/PGPatcher) by hakasapl.
- [nifly](https://github.com/ousnius/nifly) by ousnius, and its C# binding
  [niflysharp](https://github.com/Aetherinox/niflysharp), for NIF file handling.
- [Mutagen](https://github.com/Mutagen-Modding/Mutagen) for reading and writing Bethesda plugin files,
  and [DirectXTex](https://github.com/microsoft/DirectXTex) for texture processing.
- The alpha-blending concept traces back to the Majestic Landscapes modding standard; "Hide Decal
  Shapes" is modeled on [Enhanced Rocks and Mountains - Blending Patch And Other
  Fixes](https://www.nexusmods.com/skyrimspecialedition/mods/131170).
- Thanks to [ra2phoenix](https://www.nexusmods.com/profile/ra2phoenix) for their contributions and
  ideas toward fixing snow-related visual issues.

## License

GPLv3 — see [LICENSE](LICENSE). `native/` shares its architecture with AutoBlend/AutoSeasons, themselves
derived from PGPatcher, all GPLv3-licensed.
