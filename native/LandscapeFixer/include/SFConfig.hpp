#pragma once

#include <nlohmann/json_fwd.hpp>

#include <filesystem>
#include <string>
#include <vector>

/**
 * @brief Landscape (LAND record) vertex color handling mode. Values (and their on-wire order) must
 * stay in lockstep with LandscapeFixer.Core.Configuration.LandscapeVertexColorMode - both sides
 * serialize this as a plain integer.
 */
enum class SFLandscapeVertexColorMode : int { None = 0, All = 1, SnowOnly = 2 };

/**
 * @brief Landscape-folder MESH vertex color handling mode - entirely separate from
 * SFLandscapeVertexColorMode, which clears LAND (terrain) records instead of mesh geometry. Values
 * (and their on-wire order) must stay in lockstep with
 * LandscapeFixer.Core.Configuration.MeshVertexColorMode - both sides serialize this as a plain integer.
 */
enum class SFMeshVertexColorMode : int { None = 0, SnowOnly = 1, All = 2 };

/**
 * @brief Landscape-folder MESH collision material handling mode (footstep sounds/impact effects) -
 * entirely separate from SFMeshVertexColorMode, which clears vertex color tinting instead of
 * touching collision. No "All" option - duplicating a non-snow mesh purely to fix its collision
 * material wasn't judged worth the extra mesh, so this only ever applies to the snow-matched
 * duplicates the tool already generates. Values (and their on-wire order) must stay in lockstep with
 * LandscapeFixer.Core.Configuration.CollisionMaterialMode - both sides serialize this as a plain integer.
 */
enum class SFCollisionMaterialMode : int { None = 0, SnowOnly = 1 };

/// @brief Mirrors LandscapeFixer.Core.Configuration.ModManagerType (same integer values).
enum class SFModManagerType : int { None = 0, ModOrganizer2 = 1 };

/// @brief Mirrors LandscapeFixer.Core.Configuration.GameType (same integer values). LE support is new
/// and far less battle-tested than SE.
enum class SFGameType : int { SkyrimSE = 0, SkyrimLE = 1 };

/**
 * @brief User-configurable parameters for LandscapeFixer, persisted as JSON. Field set and JSON
 * key names mirror LandscapeFixer.Core.Configuration.ExtractSettings exactly (PascalCase keys,
 * default System.Text.Json serialization - no naming policy) so both this native shell and a
 * future shell read/write the very same %APPDATA%\LandscapeFixer\settings.json without any
 * translation layer. Mirrors AutoBlend's own ABParams, trimmed to this tool's much smaller settings
 * surface - no auto-generate allowlist, no PBR toggle.
 */
struct SFParams {
    /// @brief GUI language code (LandscapeFixer_translations/&lt;code&gt;.json); English if missing.
    std::string uiLanguage = "en";
    /// @brief GUI color theme: "system", "light", or "dark".
    std::string uiTheme = "system";

    /// @brief Skyrim install root (the folder containing "Data").
    std::wstring gameLocation;
    /// @brief Skyrim Special Edition or Legendary Edition. LE support is new and far less
    /// battle-tested than SE.
    SFGameType gameType = SFGameType::SkyrimSE;
    /// @brief Folder the extraction writes its generated mod (meshes + LandscapeFixer.esp + log) into.
    std::wstring outputLocation;

    SFLandscapeVertexColorMode landscapeVertexColorMode = SFLandscapeVertexColorMode::None;
    /// @brief Defaults to SnowOnly, matching this tool's behavior before this setting existed.
    SFMeshVertexColorMode meshVertexColorMode = SFMeshVertexColorMode::SnowOnly;
    /// @brief Defaults to None - a brand new capability with no established prior behavior to
    /// preserve, and it affects gameplay-audible footstep sounds, so it starts opt-in.
    SFCollisionMaterialMode collisionMaterialMode = SFCollisionMaterialMode::None;

    /// @brief None/Vortex (scan the raw Data folder directly - Vortex deploys mods straight into
    /// it via hardlinks, so nothing extra is needed there) or ModOrganizer2 (requires
    /// mo2InstancePath to reconstruct MO2's own virtual file system).
    SFModManagerType modManager = SFModManagerType::None;
    std::wstring mo2InstancePath;
    /// @brief MO2 profile whose modlist.txt/plugins.txt should be layered. The launcher populates
    /// this from the instance's valid profiles; an empty value lets the managed backend fall back
    /// to MO2's selected_profile setting for older callers/config files.
    std::wstring mo2ProfileName;

    /// @brief Meshes matching any wildcard rule here are never duplicated/patched. Defaults
    /// refined through real use against a real modlist - see
    /// LandscapeFixer.Core.Configuration.ExtractSettings.MeshBlacklist for the reasoning behind each
    /// entry; both sides must stay in lockstep.
    std::vector<std::wstring> meshBlacklist {
        LR"(*\trees\*)",   LR"(*\ice\*)",     LR"(*\effects\*)",
        LR"(*\weapons\*)", LR"(*\trophy\*)",  LR"(*\snowelfruins\*)",
        LR"(*\wet\*)",     LR"(*\lod\*)",     LR"(*\glaciers\*)",
    };
    /// @brief Records whose EditorID contains one of these words (case-insensitive) are skipped
    /// entirely. Defaults refined through real use - see
    /// LandscapeFixer.Core.Configuration.ExtractSettings.EditorIdBlacklistKeywords for the reasoning;
    /// both sides must stay in lockstep.
    std::vector<std::wstring> editorIdBlacklistKeywords {
        L"marker", L"glacier", L"lod", L"ice", L"frozen", L"icicle",
    };

    /// @brief Hides (rather than deletes) any shape flagged Decal/Dynamic_Decal, so it no longer
    /// z-fights with Skyrim's own decal-based dynamic snow shaders (Simplicity of Snow, BDS3, ...) -
    /// needs no plugin-side changes since the shape stays in the mesh. Off by default.
    bool hideDecalShapes = false;

    /// @brief Clears the snow Material Object (SnowMaterialGlacier / SnowMaterialGlacierSlab) link on
    /// every static that uses it, so no projected snow covers glaciers/ice. Off by default.
    bool removeIceSnowMaterial = false;

    /// @brief Runs the blend pass (alpha test -> alpha blend on landscape meshes, derived "blend"
    /// TextureSets, PBRNifPatcher / PBRTextureSets json).
    bool enableBlend = true;
    /// @brief Runs the snow pass (a mesh copy and a plugin override for every snow record).
    bool enableSnow = true;

    /// @brief Only meshes inside a landscape folder (any "landscape" folder segment) are processed;
    /// meshAddList lists wildcard patterns of meshes outside those folders to process anyway.
    bool restrictToLandscapeFolders = true;
    std::vector<std::wstring> meshAddList;

    /// @brief Meshes matching these are never touched by the blend pass. See
    /// LandscapeFixer.Core.Configuration.ExtractSettings.BlendMeshBlacklist - both sides must stay in lockstep.
    std::vector<std::wstring> blendMeshBlacklist {
        LR"(*\glass\*)", LR"(*\ice\*)",     LR"(*\trees\*)", LR"(*\actors\*)",
        LR"(*\roads\*)", LR"(*\effects\*)", LR"(*\magic\*)", LR"(*\weapons\*)",
    };
    /// @brief Records whose EditorID contains one of these are never touched by the blend pass.
    std::vector<std::wstring> blendEditorIdBlacklistKeywords {
        L"ice", L"frozen", L"glass", L"unique", L"wet", L"road", L"cave", L"mine",
    };

    /// @brief Also generates a PBR sibling (Height/RMAOS) for landscape textures that have none.
    bool generatePbrSlots = false;

    /// @brief Source diffuse textures a missing statics/blend sibling may be synthesized for. Mirrors
    /// LandscapeCommon.Configuration.AutoGenerateAllowlistDefaults - both sides must stay in lockstep.
    std::vector<std::wstring> autoGenerateAllowlist {
        LR"(textures\_resourcepack\landscape\desertrocks01_d.dds)",
        LR"(textures\_resourcepack\landscape\sand01_d.dds)",
        LR"(textures\_resourcepack\landscape\sand02_d.dds)",
        LR"(textures\_resourcepack\landscape\volcaniccracked01_d.dds)",
        LR"(textures\_resourcepack\landscape\volcanicrocks01_d.dds)",
        LR"(textures\bscyrodiil\landscape\colovianrocks01revised.dds)",
        LR"(textures\bscyrodiil\landscape\dirt01.dds)",
        LR"(textures\bscyrodiil\landscape\oakforestgrass01.dds)",
        LR"(textures\bscyrodiil\landscape\oakforestleaves01.dds)",
        LR"(textures\bstamriel\landscape\westweald\rockswestweald.dds)",
        LR"(textures\bstamriel\landscape\westweald\wwlemoss01.dds)",
        LR"(textures\dlc02\landscape\volcanic_ash_01.dds)",
        LR"(textures\dlc02\landscape\volcanic_ash_rocks_01_d.dds)",
        LR"(textures\dlc02\landscape\volcanic_ash_tundra_02.dds)",
        LR"(textures\dlc02\landscape\volcanic_ash_tundra_04.dds)",
        LR"(textures\landscape\coastbeach01.dds)",
        LR"(textures\landscape\dirt02.dds)",
        LR"(textures\landscape\fallforestdirt01.dds)",
        LR"(textures\landscape\fallforestgrass01.dds)",
        LR"(textures\landscape\fallforestleaves01.dds)",
        LR"(textures\landscape\fallforestrocks01.dds)",
        LR"(textures\landscape\fieldgrass01.dds)",
        LR"(textures\landscape\fieldgrass02.dds)",
        LR"(textures\landscape\frozenmarshice01.dds)",
        LR"(textures\landscape\mineralpoolterrace.dds)",
        LR"(textures\landscape\reachmossyrocks01.dds)",
        LR"(textures\landscape\riverbededge.dds)",
        LR"(textures\landscape\rocks01.dds)",
        LR"(textures\landscape\snow01.dds)",
        LR"(textures\landscape\snowrocks01.dds)",
        LR"(textures\landscape\tundra01.dds)",
        LR"(textures\landscape\tundrarocks01.dds)",
        LR"(textures\landscape\volcanictundradirt01.dds)",
        LR"(textures\landscape\volcanictundragravel01.dds)",
        LR"(textures\landscape\volcanictundraminerals01.dds)",
        LR"(textures\landscape\volcanictundrarocks01.dds)",
    };
};

/**
 * @class SFConfig
 * @brief Loads/saves SFParams to the same settings.json LandscapeFixer.Core's Exports.cs
 * (de)serializes into an ExtractSettings, so settings persist between runs. Mirrors AutoBlend's own
 * ABConfig (same file I/O pattern) under this project's own name.
 */
class SFConfig {
public:
    static auto load() -> SFParams;
    static void save(const SFParams& params);

    static auto loadFrom(const std::filesystem::path& configFilePath) -> SFParams;
    static void saveTo(const std::filesystem::path& configFilePath, const SFParams& params);

    /// @brief Serializes params to the exact JSON shape
    /// LandscapeFixer.Core.Configuration.ExtractSettings (de)serializes - used both by
    /// save()/saveTo() and to build the payload start_extract_run expects.
    static auto toJson(const SFParams& params) -> nlohmann::json;

private:
    static auto getConfigPath() -> std::filesystem::path;
};
