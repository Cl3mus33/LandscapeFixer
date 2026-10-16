#include "SFConfig.hpp"

#include "util/FileUtil.hpp"
#include "util/Logger.hpp"
#include "util/StringUtil.hpp"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cwctype>
#include <fstream>
#include <optional>

// <windows.h> before <shlobj.h> so knownfolders.h's FOLDERID_* GUID definitions see properly
// set-up COM/GUID macros (DECLSPEC_SELECTANY etc.) - matches AutoBlend's own ABConfig.cpp fix for
// the same class of "included a um/ header standalone" issue.
#include <windows.h>

#include <shlobj.h>

using namespace std;

namespace {
auto appDataDir() -> filesystem::path
{
    PWSTR rawPath = nullptr;
    filesystem::path result;
    if (SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &rawPath) == S_OK) {
        result = filesystem::path(rawPath);
    }
    if (rawPath != nullptr) {
        CoTaskMemFree(rawPath);
    }
    return result;
}
}

namespace {
std::optional<SFConfig::Mo2Instance> g_instance;

auto knownFolder(const KNOWNFOLDERID& id) -> filesystem::path
{
    PWSTR rawPath = nullptr;
    filesystem::path result;
    if (SHGetKnownFolderPath(id, 0, nullptr, &rawPath) == S_OK) {
        result = filesystem::path(rawPath);
    }
    if (rawPath != nullptr) {
        CoTaskMemFree(rawPath);
    }
    return result;
}

// Case-insensitive, slash-agnostic form of a directory path, for comparing ModOrganizer.ini's
// base_directory (Qt writes it with doubled backslashes or forward slashes) with a real path.
auto normalizePath(wstring path) -> wstring
{
    wstring collapsed;
    for (auto ch : path) {
        if (ch == L'/') {
            ch = L'\\';
        }
        ch = static_cast<wchar_t>(towlower(ch));
        if (ch == L'\\' && !collapsed.empty() && collapsed.back() == L'\\') {
            continue;
        }
        collapsed.push_back(ch);
    }
    while (!collapsed.empty() && collapsed.back() == L'\\') {
        collapsed.pop_back();
    }
    return collapsed;
}

// Value of "<key>=..." in a Qt ini, unwrapped from "@ByteArray(...)" / quotes, with "\\" turned into "\".
auto readIniValue(const filesystem::path& iniPath, const string& key) -> wstring
{
    ifstream file(iniPath);
    string line;
    const auto prefix = key + "=";
    while (getline(file, line)) {
        if (line.rfind(prefix, 0) != 0) {
            continue;
        }
        auto value = line.substr(prefix.size());
        while (!value.empty() && (value.back() == '\r' || value.back() == '\n')) {
            value.pop_back();
        }
        if (value.rfind("@ByteArray(", 0) == 0 && value.size() > 12 && value.back() == ')') {
            value = value.substr(11, value.size() - 12);
        }
        if (value.size() >= 2 && value.front() == '"' && value.back() == '"') {
            value = value.substr(1, value.size() - 2);
        }
        const auto wide = StringUtil::utf8toUTF16(value);
        wstring unescaped;
        for (size_t i = 0; i < wide.size(); i++) {
            unescaped.push_back(wide[i]);
            if (wide[i] == L'\\' && i + 1 < wide.size() && wide[i + 1] == L'\\') {
                i++;
            }
        }
        return unescaped;
    }
    return {};
}

auto fillInstance(const filesystem::path& iniPath, const wstring& name, const wstring& instancePath) -> SFConfig::Mo2Instance
{
    SFConfig::Mo2Instance instance;
    instance.name = name;
    instance.path = instancePath;
    instance.baseDirectory = readIniValue(iniPath, "base_directory");
    instance.gamePath = readIniValue(iniPath, "gamePath");
    instance.selectedProfile = readIniValue(iniPath, "selected_profile");
    return instance;
}

auto sanitizeFileName(wstring name) -> wstring
{
    for (auto& ch : name) {
        if (wcschr(L"<>:\"/\\|?*", ch) != nullptr) {
            ch = L'_';
        }
    }
    return name;
}
}

auto SFConfig::detectMo2Instance(const filesystem::path& exePath) -> optional<Mo2Instance>
{
    // Installed as a regular MO2 mod: <base>\mods\LandscapeFixer\LandscapeFixer.exe.
    const auto modsDir = exePath.parent_path();
    if (modsDir.empty() || normalizePath(modsDir.filename().wstring()) != L"mods") {
        return nullopt;
    }
    const auto baseDir = modsDir.parent_path();
    const auto base = normalizePath(baseDir.wstring());
    if (base.empty()) {
        return nullopt;
    }

    error_code ec;
    // A portable instance keeps its ModOrganizer.ini right in the base folder.
    const auto portableIni = baseDir / "ModOrganizer.ini";
    if (filesystem::exists(portableIni, ec)) {
        return fillInstance(portableIni, baseDir.filename().wstring(), baseDir.wstring());
    }

    const auto instancesRoot = knownFolder(FOLDERID_LocalAppData) / "ModOrganizer";
    if (!filesystem::is_directory(instancesRoot, ec)) {
        return nullopt;
    }
    for (const auto& entry : filesystem::directory_iterator(instancesRoot, ec)) {
        const auto ini = entry.path() / "ModOrganizer.ini";
        if (!entry.is_directory(ec) || !filesystem::exists(ini, ec)) {
            continue;
        }
        auto instance = fillInstance(ini, entry.path().filename().wstring(), entry.path().wstring());
        if (normalizePath(instance.baseDirectory) == base) {
            return instance;
        }
    }
    return nullopt;
}

void SFConfig::selectInstanceFor(const filesystem::path& exePath)
{
    try {
        g_instance = detectMo2Instance(exePath);
    } catch (const exception& e) {
        Logger::warn("Could not work out the MO2 instance: {}", e.what());
        g_instance.reset();
    }
}

auto SFConfig::getConfigPath() -> filesystem::path
{
    if (g_instance.has_value()) {
        return appDataDir() / "LandscapeFixer" / "instances" / (sanitizeFileName(g_instance->name) + L".json");
    }
    return appDataDir() / "LandscapeFixer" / "settings.json";
}

auto SFConfig::load() -> SFParams
{
    if (!g_instance.has_value()) {
        return loadFrom(getConfigPath());
    }

    // Per-instance settings. The first time an instance is seen, it starts from the old shared file (so
    // lists and options carry over) but with this instance's own MO2 path, profile, game folder and output
    // folder - the shared file carried the last modlist's, which is how a run could end up reading one
    // modlist's profile while another one was open.
    const auto instanceFile = getConfigPath();
    error_code ec;
    const bool firstTime = !filesystem::exists(instanceFile, ec);
    auto params = loadFrom(firstTime ? appDataDir() / "LandscapeFixer" / "settings.json" : instanceFile);

    const auto& instance = *g_instance;
    if (firstTime) {
        params.modManager = SFModManagerType::ModOrganizer2;
        params.mo2InstancePath = instance.path;
        if (!instance.selectedProfile.empty()) {
            params.mo2ProfileName = instance.selectedProfile;
        }
        if (!instance.gamePath.empty()) {
            params.gameLocation = instance.gamePath;
        }
        const auto outputUnderBase = !instance.baseDirectory.empty()
            && normalizePath(params.outputLocation).rfind(normalizePath(instance.baseDirectory) + L"\\", 0) == 0;
        if (!instance.baseDirectory.empty() && !outputUnderBase) {
            params.outputLocation = (filesystem::path(instance.baseDirectory) / "mods" / "LandscapeFixer Output").wstring();
        }
    } else if (params.mo2InstancePath.empty() || !filesystem::exists(filesystem::path(params.mo2InstancePath), ec)) {
        params.modManager = SFModManagerType::ModOrganizer2;
        params.mo2InstancePath = instance.path;
    }
    return params;
}

void SFConfig::save(const SFParams& params) { saveTo(getConfigPath(), params); }

auto SFConfig::loadFrom(const filesystem::path& configFilePath) -> SFParams
{
    SFParams params;

    nlohmann::json configJ;
    if (!FileUtil::getJSON(configFilePath, configJ)) {
        return params;
    }

    try {
        if (configJ.contains("UiLanguage")) {
            params.uiLanguage = configJ["UiLanguage"].get<string>();
        }
        if (configJ.contains("UiTheme")) {
            params.uiTheme = configJ["UiTheme"].get<string>();
        }
        if (configJ.contains("GameLocation")) {
            params.gameLocation = StringUtil::utf8toUTF16(configJ["GameLocation"].get<string>());
        }
        if (configJ.contains("GameType")) {
            params.gameType = static_cast<SFGameType>(configJ["GameType"].get<int>());
        }
        if (configJ.contains("OutputLocation")) {
            params.outputLocation = StringUtil::utf8toUTF16(configJ["OutputLocation"].get<string>());
        }
        if (configJ.contains("LandscapeVertexColorMode")) {
            params.landscapeVertexColorMode = static_cast<SFLandscapeVertexColorMode>(configJ["LandscapeVertexColorMode"].get<int>());
        }
        if (configJ.contains("MeshVertexColorMode")) {
            params.meshVertexColorMode = static_cast<SFMeshVertexColorMode>(configJ["MeshVertexColorMode"].get<int>());
        }
        if (configJ.contains("CollisionMaterialMode")) {
            params.collisionMaterialMode = static_cast<SFCollisionMaterialMode>(configJ["CollisionMaterialMode"].get<int>());
        }
        if (configJ.contains("ModManager")) {
            params.modManager = static_cast<SFModManagerType>(configJ["ModManager"].get<int>());
        }
        if (configJ.contains("Mo2InstancePath")) {
            params.mo2InstancePath = StringUtil::utf8toUTF16(configJ["Mo2InstancePath"].get<string>());
        }
        if (configJ.contains("Mo2ProfileName")) {
            params.mo2ProfileName = StringUtil::utf8toUTF16(configJ["Mo2ProfileName"].get<string>());
        }
        if (configJ.contains("MeshBlacklist")) {
            params.meshBlacklist.clear();
            for (const auto& item : configJ["MeshBlacklist"]) {
                params.meshBlacklist.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }
        }
        if (configJ.contains("EditorIdBlacklistKeywords")) {
            params.editorIdBlacklistKeywords.clear();
            for (const auto& item : configJ["EditorIdBlacklistKeywords"]) {
                params.editorIdBlacklistKeywords.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }
        }
        if (configJ.contains("HideDecalShapes")) {
            params.hideDecalShapes = configJ["HideDecalShapes"].get<bool>();
        }
        if (configJ.contains("RemoveIceSnowMaterial")) {
            params.removeIceSnowMaterial = configJ["RemoveIceSnowMaterial"].get<bool>();
        }
        if (configJ.contains("EnableBlend")) {
            params.enableBlend = configJ["EnableBlend"].get<bool>();
        }
        if (configJ.contains("EnableSnow")) {
            params.enableSnow = configJ["EnableSnow"].get<bool>();
        }
        if (configJ.contains("RestrictToLandscapeFolders")) {
            params.restrictToLandscapeFolders = configJ["RestrictToLandscapeFolders"].get<bool>();
        }
        if (configJ.contains("MeshAddList")) {
            params.meshAddList.clear();
            for (const auto& item : configJ["MeshAddList"]) {
                params.meshAddList.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }
        }
        if (configJ.contains("BlendMeshBlacklist")) {
            params.blendMeshBlacklist.clear();
            for (const auto& item : configJ["BlendMeshBlacklist"]) {
                params.blendMeshBlacklist.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }
        }
        if (configJ.contains("BlendEditorIdBlacklistKeywords")) {
            params.blendEditorIdBlacklistKeywords.clear();
            for (const auto& item : configJ["BlendEditorIdBlacklistKeywords"]) {
                params.blendEditorIdBlacklistKeywords.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }
        }
        if (configJ.contains("GeneratePbrSlots")) {
            params.generatePbrSlots = configJ["GeneratePbrSlots"].get<bool>();
        }
        if (configJ.contains("AutoGenerateAllowlist")) {
            params.autoGenerateAllowlist.clear();
            for (const auto& item : configJ["AutoGenerateAllowlist"]) {
                params.autoGenerateAllowlist.push_back(StringUtil::utf8toUTF16(item.get<string>()));
            }
        }
    } catch (const exception& e) {
        Logger::warn("Failed to parse settings file, using defaults: {}", e.what());
        return SFParams {};
    }

    return params;
}

auto SFConfig::toJson(const SFParams& params) -> nlohmann::json
{
    nlohmann::json configJ;

    configJ["UiLanguage"] = params.uiLanguage;
    configJ["UiTheme"] = params.uiTheme;
    configJ["GameLocation"] = StringUtil::utf16toUTF8(params.gameLocation);
    configJ["GameType"] = static_cast<int>(params.gameType);
    configJ["OutputLocation"] = StringUtil::utf16toUTF8(params.outputLocation);
    configJ["LandscapeVertexColorMode"] = static_cast<int>(params.landscapeVertexColorMode);
    configJ["MeshVertexColorMode"] = static_cast<int>(params.meshVertexColorMode);
    configJ["CollisionMaterialMode"] = static_cast<int>(params.collisionMaterialMode);
    configJ["ModManager"] = static_cast<int>(params.modManager);
    configJ["Mo2InstancePath"] = StringUtil::utf16toUTF8(params.mo2InstancePath);
    configJ["Mo2ProfileName"] = StringUtil::utf16toUTF8(params.mo2ProfileName);

    configJ["MeshBlacklist"] = nlohmann::json::array();
    for (const auto& item : params.meshBlacklist) {
        configJ["MeshBlacklist"].push_back(StringUtil::utf16toUTF8(item));
    }

    configJ["EditorIdBlacklistKeywords"] = nlohmann::json::array();
    for (const auto& item : params.editorIdBlacklistKeywords) {
        configJ["EditorIdBlacklistKeywords"].push_back(StringUtil::utf16toUTF8(item));
    }

    configJ["HideDecalShapes"] = params.hideDecalShapes;
    configJ["RemoveIceSnowMaterial"] = params.removeIceSnowMaterial;

    configJ["EnableBlend"] = params.enableBlend;
    configJ["EnableSnow"] = params.enableSnow;
    configJ["RestrictToLandscapeFolders"] = params.restrictToLandscapeFolders;

    configJ["MeshAddList"] = nlohmann::json::array();
    for (const auto& item : params.meshAddList) {
        configJ["MeshAddList"].push_back(StringUtil::utf16toUTF8(item));
    }

    configJ["BlendMeshBlacklist"] = nlohmann::json::array();
    for (const auto& item : params.blendMeshBlacklist) {
        configJ["BlendMeshBlacklist"].push_back(StringUtil::utf16toUTF8(item));
    }

    configJ["BlendEditorIdBlacklistKeywords"] = nlohmann::json::array();
    for (const auto& item : params.blendEditorIdBlacklistKeywords) {
        configJ["BlendEditorIdBlacklistKeywords"].push_back(StringUtil::utf16toUTF8(item));
    }

    configJ["GeneratePbrSlots"] = params.generatePbrSlots;

    configJ["AutoGenerateAllowlist"] = nlohmann::json::array();
    for (const auto& item : params.autoGenerateAllowlist) {
        configJ["AutoGenerateAllowlist"].push_back(StringUtil::utf16toUTF8(item));
    }

    return configJ;
}

void SFConfig::saveTo(const filesystem::path& configFilePath, const SFParams& params)
{
    try {
        const auto configJ = toJson(params);
        error_code ec;
        filesystem::create_directories(configFilePath.parent_path(), ec);

        ofstream file(configFilePath);
        if (!file.is_open()) {
            Logger::warn("Failed to save settings file");
            return;
        }

        file << configJ.dump(2);
    } catch (const exception& e) {
        // Mirrors loadFrom()'s own try/catch - a failure here (e.g. dump() rejecting invalid
        // UTF-8) should cost the user their settings for this run, not crash the app on "Start".
        Logger::warn("Failed to save settings file: {}", e.what());
    }
}
