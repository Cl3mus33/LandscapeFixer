# Changelog

All notable changes to this project are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

The history of the two tools this one replaces lives in their own repositories
(Snow Fixer up to 1.2.14, AutoBlend up to 1.2.12).

## [Unreleased]

### Changed
- **New window**, dark and flat like TreeLODGen's, in place of the wxWidgets launcher: Setup, Landscape, Blend
  and Snow are collapsible cards (Blend and Snow unfold when ticked), the help texts are tooltips, the panel
  on the right shows what the run changed (blend meshes, TextureSets, snow records and
  meshes, terrain and meshes cleaned, ...) next to the messages of the run. Language is under the gear.
  The tool is now one self-contained exe plus the texture tools - no .NET runtime folder, no native shell.
- **Settings are kept per Mod Organizer 2 instance.** The tool finds the instance it is installed in
  (`<instance>\mods\...`, from the real process path - the module path is the virtual one under MO2) and
  reads/writes `%APPDATA%\LandscapeFixer\instances\<instance>.json`, with that instance's MO2 path, profile,
  game folder and output folder. Before, one shared file carried the last modlist's paths, so a run started
  from one modlist could read another one's profile. Outside MO2 the shared `settings.json` is used as before.
- Generate PBR slots is switched off and explained (not just greyed) on Legendary Edition.

### Fixed
- **A run with a Game Location that doesn't hold the game** (no `Data\Skyrim.esm`) used to go on and wipe every
  reference to Skyrim.esm from the output - regions, locations, ingredients all null (reported on Discord).
  It now stops with a clear error, and the official plugins are never treated as missing.

### Added
- First merged version: the blend pass (AutoBlend 1.2.12) and the snow pass (Snow Fixer 1.2.14) in one
  run, one output folder and one ESL-flagged plugin, `LandscapeFixer.esp`.
- Landscape scope: only meshes with a `landscape` folder in their path are processed; an
  "Additional Meshes" list adds wildcard patterns for the rest.
- Passes can be switched on and off, and the blend pass has its own card (PBR slots, auto-generate
  allowlist, blacklists).

### Fixed
- **A straight seam along a cell border (one side lighter) when "Clear on all terrain" was on.** Since Snow
  Fixer 1.2.11 a terrain cell was skipped as "deliberately repainted by a mod" as soon as its vertex
  colors differed from vanilla by a single value - but a mod that merely re-saves a cell (a grass fix, a
  seam fix) nudges a few colors by a few levels out of 255. On the test load order 15 cells (all from one
  grass-fix mod, 1.1/255 on average, 8.2/255 at most) stayed tinted while every neighbour was cleared.
  Only a mean difference above 16/255 per channel now counts as a mod's own paint job and is kept.
- **`MissingModException` ("A referenced mod was not present on the load order being sorted against") when
  writing the plugin**, e.g. with a Creation Club plugin loaded through Skyrim.ccc rather than plugins.txt
  (reported on Nexus against Snow Fixer 1.2.14). Masters outside the active plugin list are slotted in right
  after the game's base masters and the write is retried; if sorting is still impossible the plugin is
  written with the default master order instead.

### Changed
- A snow record only gets a mesh copy (and a plugin override) when one of the snow fixes actually
  changed something in its mesh; otherwise the copy is dropped and the record is left alone. On the
  test load order with collision remapping and decal hiding on, 126 copies instead of 232.
- The default mesh blacklists were trimmed to what exists under the landscape folders (blend: trees,
  ice, icebergs, glaciers, roads, plants; snow: trees, ice, icebergs, glaciers, wet, lod).
  The blend EditorID keywords lose `glass`, `cave`, `mine`, `unique` and `wet` (ice, frozen, road remain).
- "Clear vertex colors on all landscape meshes" is now done in place, once per mesh, on the winning
  mesh at its own vanilla path - no more one copy per record (1166 fewer meshes on the test load
  order). "Only on the generated snow meshes" still uses the snow copies.
- A snow copy is taken from the blend-patched mesh when the blend pass patched it, so both fixes end up
  in the same file.
- The default snow mesh blacklist no longer lists `clutter`, `architecture` and `dungeons`: the
  landscape scope replaces them.

### Verified
- Blend pass alone, scope unrestricted: byte-identical meshes and textures to AutoBlend 1.2.12 on a
  real load order. Snow pass alone: byte-identical meshes to Snow Fixer 1.2.14.
