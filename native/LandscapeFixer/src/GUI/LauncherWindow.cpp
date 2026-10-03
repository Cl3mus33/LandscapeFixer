#include "GUI/LauncherWindow.hpp"

#include "SFLocale.hpp"
#include "GUI/components/PGCustomListctrlChangedEvent.hpp"
#include "util/StringUtil.hpp"

#include <LandscapeFixer.NativeExportNE.h>
#include <nlohmann/json.hpp>

#include <wx/notebook.h>
#include <wx/scrolwin.h>
#include <wx/statline.h>

using namespace std;

namespace {
constexpr int BORDER_SIZE = 5;

// Alpine palette taken from the app icon: deep teal sky, mountain-lake teal accent.
const wxColour ACCENT_DARK(14, 42, 58); // header banner background
const wxColour ACCENT(40, 128, 138); // primary button, section label text
const wxColour ACCENT_SOFT(150, 196, 202); // header subtitle
const wxColour ACCENT_TEXT(255, 255, 255); // text on top of ACCENT_DARK/ACCENT

auto toIntPtr(const string& utf8) -> intptr_t { return reinterpret_cast<intptr_t>(utf8.c_str()); }
auto fromIntPtr(intptr_t ptr) -> string
{
    return ptr == 0 ? string {} : string(reinterpret_cast<const char*>(ptr));
}

auto makeSectionLabel(wxWindow* parent, const wxString& text) -> wxStaticText*
{
    auto* label = new wxStaticText(parent, wxID_ANY, text);
    wxFont font = label->GetFont();
    font.SetWeight(wxFONTWEIGHT_BOLD);
    label->SetFont(font);
    label->SetForegroundColour(ACCENT);
    return label;
}
}

namespace {
// Fills / reads one of the inline editable tables (each ends with an empty row to type into).
void fillListCtrl(PGModifiableListCtrl* ctrl, const vector<wstring>& values)
{
    ctrl->DeleteAllItems();
    long index = 0;
    for (const auto& value : values) {
        ctrl->InsertItem(index++, wxString(value));
    }
    ctrl->InsertItem(ctrl->GetItemCount(), "");
}

void readListCtrl(const PGModifiableListCtrl* ctrl, vector<wstring>& out)
{
    out.clear();
    long item = -1;
    while ((item = ctrl->GetNextItem(item)) != -1) {
        const wxString text = ctrl->GetItemText(item);
        if (!text.IsEmpty()) {
            out.push_back(text.ToStdWstring());
        }
    }
}

auto makeListCtrl(wxWindow* parent, const wxString& column, const vector<wstring>& values) -> PGModifiableListCtrl*
{
    auto* ctrl = new PGModifiableListCtrl(
        parent, wxID_ANY, wxDefaultPosition, wxDefaultSize, wxLC_REPORT | wxLC_EDIT_LABELS | wxLC_NO_HEADER);
    ctrl->AppendColumn(column, wxLIST_FORMAT_LEFT, wxLIST_AUTOSIZE_USEHEADER);
    ctrl->SetColumnWidth(0, wxLIST_AUTOSIZE_USEHEADER);
    fillListCtrl(ctrl, values);
    return ctrl;
}

void fitListColumn(PGModifiableListCtrl* ctrl)
{
    if (ctrl != nullptr && ctrl->GetColumnCount() > 0) {
        ctrl->SetColumnWidth(0, ctrl->GetClientSize().GetWidth());
    }
}
}

// A wxChoice under the mouse cursor swallows the mouse wheel and changes its own selection instead of
// letting the scrolled General tab scroll - so scrolling past Game Type silently flipped it to Legendary
// Edition (which then greyed out and unchecked "Generate PBR slots"), reported directly on Nexus. Every
// choice inside the scrolled panel hands the wheel to the panel instead; changing a value still works by
// clicking the dropdown.
static void forwardChoiceWheelToScrollPanel(wxWindow* root, wxScrolledWindow* scrollPanel)
{
    for (wxWindow* child : root->GetChildren()) {
        if (auto* choice = wxDynamicCast(child, wxChoice)) {
            choice->Bind(wxEVT_MOUSEWHEEL, [scrollPanel](wxMouseEvent& event) {
                scrollPanel->GetEventHandler()->ProcessEvent(event);
            });
        }
        forwardChoiceWheelToScrollPanel(child, scrollPanel);
    }
}

LauncherWindow::LauncherWindow(const SFParams& initParams, filesystem::path exePath)
    : wxDialog(nullptr, wxID_ANY, "Landscape Fixer", wxDefaultPosition, wxSize(760, 680), wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER)
    , m_exePath(std::move(exePath))
{
    const wxIcon appIcon(wxICON(IDI_ICON1));
    SetIcon(appIcon);

    auto* mainSizer = new wxBoxSizer(wxVERTICAL);

    // Header banner - same treatment as AutoBlend's own launcher, gives the dialog an identity of
    // its own at a glance.
    auto* headerPanel = new wxPanel(this);
    headerPanel->SetBackgroundColour(ACCENT_DARK);
    auto* headerSizer = new wxBoxSizer(wxHORIZONTAL);
    auto* headerIcon = new wxStaticBitmap(headerPanel, wxID_ANY, wxBitmap(appIcon).ConvertToImage().Scale(32, 32, wxIMAGE_QUALITY_HIGH));
    headerSizer->Add(headerIcon, 0, wxALL | wxALIGN_CENTER_VERTICAL, BORDER_SIZE * 2);
    auto* headerTextSizer = new wxBoxSizer(wxVERTICAL);
    auto* headerTitle = new wxStaticText(headerPanel, wxID_ANY, "Landscape Fixer");
    wxFont headerFont = headerTitle->GetFont();
    headerFont.SetPointSize(headerFont.GetPointSize() + 4);
    headerFont.SetWeight(wxFONTWEIGHT_BOLD);
    headerTitle->SetFont(headerFont);
    headerTitle->SetForegroundColour(ACCENT_TEXT);
    headerTextSizer->Add(headerTitle, 0);
    auto* headerSubtitle = new wxStaticText(headerPanel, wxID_ANY,
        SFTr("launcher.subtitle", "Snow and blend fixes for the game's landscape meshes"));
    headerSubtitle->SetForegroundColour(ACCENT_SOFT);
    headerTextSizer->Add(headerSubtitle, 0, wxTOP, 1);
    headerSizer->Add(headerTextSizer, 0, wxALIGN_CENTER_VERTICAL);
    headerPanel->SetSizerAndFit(headerSizer);
    mainSizer->Add(headerPanel, 0, wxEXPAND);

    auto* notebook = new wxNotebook(this, wxID_ANY);

    // "General" tab - the actual per-run settings, same split AutoBlend uses between per-run
    // settings and app-wide preferences. A plain wxPanel here used to mean the dialog itself grew
    // to fit however tall this tab's content got (SetSizerAndFit below) - fine while there were only
    // a handful of settings, but every feature added since (Config Profile, Game Type, Hide Decal
    // Shapes, Ice Snow Material, ...) made it taller, and reported
    // directly on Nexus: on a smaller display the resulting window is taller than the screen itself,
    // with no way to reach the controls (or even the Start button) below the fold - dragging the
    // window's own edges can't make it bigger than the screen. A wxScrolledWindow instead scrolls
    // its own content vertically, so the dialog itself can stay a fixed, always-on-screen size no
    // matter how many settings this tab ends up with.
    // Tabs, in the order a user works through them: Setup (where things are) -> Landscape (what gets
    // touched) -> Blend -> Snow -> Options. Every tab scrolls on its own, so the dialog itself can stay a
    // fixed, always-on-screen size however many settings a tab ends up with (reported directly on Nexus:
    // on a small display a taller-than-screen window left the Start button unreachable).
    constexpr int HELP_WRAP_PAIRED = 300;
    constexpr int HELP_WRAP_BLEND = 300;

    auto* setupPanel = new wxScrolledWindow(notebook);
    setupPanel->SetScrollRate(0, 30);
    auto* setupSizer = new wxBoxSizer(wxVERTICAL);

    auto* landscapePanel = new wxScrolledWindow(notebook);
    landscapePanel->SetScrollRate(0, 30);
    auto* landscapeSizer = new wxBoxSizer(wxVERTICAL);

    auto* snowPanel = new wxScrolledWindow(notebook);
    snowPanel->SetScrollRate(0, 30);
    auto* snowSizer = new wxBoxSizer(wxVERTICAL);

    auto* setupIntroText = new wxStaticText(setupPanel, wxID_ANY,
        SFTr("launcher.setup.intro", "Where your game, your mod manager and the output folder are."));
    setupIntroText->Wrap(500);
    setupSizer->Add(setupIntroText, 0, wxALL, BORDER_SIZE * 2);

    auto* landscapeIntroText = new wxStaticText(landscapePanel, wxID_ANY,
        SFTr("launcher.intro",
            "Fixes the landscape meshes of your load order: alpha test becomes alpha blend with derived "
            "\"blend\" TextureSets (Blend), and every snow record gets its own mesh copy with the snow "
            "fixes applied (Snow). Only meshes inside a landscape folder are touched, everything is written "
            "to one plugin, and PG Patcher is left to do the PBR conversion afterwards."));
    landscapeIntroText->Wrap(500);
    landscapeSizer->Add(landscapeIntroText, 0, wxALL, BORDER_SIZE * 2);

    auto* snowIntroText = new wxStaticText(snowPanel, wxID_ANY,
        SFTr("launcher.snow.intro",
            "Every record whose EditorID or mesh path says snow gets its own copy of the winning mesh, "
            "with the fixes below applied to the copy only. Vertex colors can also be cleared in place on "
            "every landscape mesh."));
    snowIntroText->Wrap(500);
    snowSizer->Add(snowIntroText, 0, wxALL, BORDER_SIZE * 2);

    // Config profile - a single install (e.g. shared outside any one modlist) can still keep
    // distinct settings per modlist by saving/loading separate JSON files here, instead of relying
    // on %APPDATA%\LandscapeFixer\settings.json (which only isolates settings when each modlist gets its
    // own copy of the exe) - requested directly after noticing several modlists sharing one exe all
    // fight over that one file. Mirrors AutoBlend's own identical Load/Save Config pattern.
    setupSizer->Add(makeSectionLabel(setupPanel, SFTr("launcher.configProfile.label", "Config Profile")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    auto* loadConfigButton = new wxButton(setupPanel, wxID_ANY, SFTr("launcher.configProfile.load", "Load Config..."));
    loadConfigButton->Bind(wxEVT_BUTTON, &LauncherWindow::onLoadConfig, this);
    auto* saveConfigButton = new wxButton(setupPanel, wxID_ANY, SFTr("launcher.configProfile.saveAs", "Save Config As..."));
    saveConfigButton->Bind(wxEVT_BUTTON, &LauncherWindow::onSaveConfigAs, this);

    auto* configProfileSizer = new wxBoxSizer(wxHORIZONTAL);
    configProfileSizer->Add(loadConfigButton, 0, wxALL, BORDER_SIZE);
    configProfileSizer->Add(saveConfigButton, 0, wxALL, BORDER_SIZE);
    setupSizer->Add(configProfileSizer, 0);

    // Game location
    setupSizer->Add(makeSectionLabel(setupPanel, SFTr("launcher.gameLocation.label", "Game Location")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_gameLocationTextbox = new wxTextCtrl(setupPanel, wxID_ANY, initParams.gameLocation);
    auto* gameBrowseButton = new wxButton(setupPanel, wxID_ANY, SFTr("common.browse", "Browse"));
    gameBrowseButton->Bind(wxEVT_BUTTON, &LauncherWindow::onBrowseGameLocation, this);

    auto* gameLocationSizer = new wxBoxSizer(wxHORIZONTAL);
    gameLocationSizer->Add(m_gameLocationTextbox, 1, wxEXPAND | wxALL, BORDER_SIZE);
    gameLocationSizer->Add(gameBrowseButton, 0, wxALL, BORDER_SIZE);
    setupSizer->Add(gameLocationSizer, 0, wxEXPAND);

    // Game type - LE support is new and far less battle-tested than SE (the vast majority of this
    // tool's own verification has been against real SE modlists).
    setupSizer->Add(makeSectionLabel(setupPanel, SFTr("launcher.gameType.label", "Game Type")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    wxArrayString gameTypeChoices;
    gameTypeChoices.Add(SFTr("launcher.gameType.se", "Skyrim Special Edition"));
    gameTypeChoices.Add(SFTr("launcher.gameType.le", "Skyrim Legendary Edition"));
    m_gameTypeChoice = new wxChoice(setupPanel, wxID_ANY, wxDefaultPosition, wxDefaultSize, gameTypeChoices);
    m_gameTypeChoice->SetSelection(initParams.gameType == SFGameType::SkyrimLE ? 1 : 0);
    m_gameTypeChoice->Bind(wxEVT_CHOICE, &LauncherWindow::onGameTypeChanged, this);
    setupSizer->Add(m_gameTypeChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

    // Output location
    setupSizer->Add(makeSectionLabel(setupPanel, SFTr("launcher.outputLocation.label", "Output Location")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_outputLocationTextbox = new wxTextCtrl(setupPanel, wxID_ANY, initParams.outputLocation);
    auto* outputBrowseButton = new wxButton(setupPanel, wxID_ANY, SFTr("common.browse", "Browse"));
    outputBrowseButton->Bind(wxEVT_BUTTON, &LauncherWindow::onBrowseOutputLocation, this);

    auto* outputLocationSizer = new wxBoxSizer(wxHORIZONTAL);
    outputLocationSizer->Add(m_outputLocationTextbox, 1, wxEXPAND | wxALL, BORDER_SIZE);
    outputLocationSizer->Add(outputBrowseButton, 0, wxALL, BORDER_SIZE);
    setupSizer->Add(outputLocationSizer, 0, wxEXPAND);

    // Mod manager - "None / Vortex" covers both: Vortex (in its default hardlink deployment mode)
    // writes mods straight into the real Data folder, so there's no separate virtual filesystem to
    // read, unlike MO2's own USVFS-backed setup.
    setupSizer->Add(makeSectionLabel(setupPanel, SFTr("launcher.modManager.label", "Mod Manager")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    wxArrayString modManagerChoices;
    modManagerChoices.Add(SFTr("launcher.modManager.none", "None / Vortex"));
    modManagerChoices.Add(SFTr("launcher.modManager.mo2", "Mod Organizer 2"));
    m_modManagerChoice = new wxChoice(setupPanel, wxID_ANY, wxDefaultPosition, wxDefaultSize, modManagerChoices);
    m_modManagerChoice->SetSelection(initParams.modManager == SFModManagerType::ModOrganizer2 ? 1 : 0);
    m_modManagerChoice->Bind(wxEVT_CHOICE, &LauncherWindow::onModManagerChanged, this);
    setupSizer->Add(m_modManagerChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

    // MO2 instance path - always present (rather than appearing/disappearing with the mod manager
    // choice above), just enabled/disabled - matches AutoBlend's own LauncherWindow, which found
    // that toggling visibility made users miss the field entirely.
    m_mo2InstancePathLabel = makeSectionLabel(setupPanel, SFTr("launcher.mo2InstancePath.label", "MO2 Instance Path"));
    setupSizer->Add(m_mo2InstancePathLabel, 0, wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_mo2InstancePathTextbox = new wxTextCtrl(setupPanel, wxID_ANY, initParams.mo2InstancePath);
    m_mo2InstancePathTextbox->Bind(wxEVT_TEXT, &LauncherWindow::onMo2InstancePathChanged, this);
    m_mo2InstanceBrowseButton = new wxButton(setupPanel, wxID_ANY, SFTr("common.browse", "Browse"));
    m_mo2InstanceBrowseButton->Bind(wxEVT_BUTTON, &LauncherWindow::onBrowseMo2Instance, this);

    auto* mo2InstanceSizer = new wxBoxSizer(wxHORIZONTAL);
    mo2InstanceSizer->Add(m_mo2InstancePathTextbox, 1, wxEXPAND | wxALL, BORDER_SIZE);
    mo2InstanceSizer->Add(m_mo2InstanceBrowseButton, 0, wxALL, BORDER_SIZE);
    setupSizer->Add(mo2InstanceSizer, 0, wxEXPAND);

    m_mo2ProfileLabel = makeSectionLabel(setupPanel, SFTr("launcher.mo2Profile.label", "MO2 Profile"));
    setupSizer->Add(m_mo2ProfileLabel, 0, wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);
    m_mo2ProfileChoice = new wxChoice(setupPanel, wxID_ANY);
    setupSizer->Add(m_mo2ProfileChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

    updateMo2FieldState();
    refreshMo2Profiles();
    if (!initParams.mo2ProfileName.empty()) {
        m_mo2ProfileChoice->SetStringSelection(wxString(initParams.mo2ProfileName));
    }

    // Passes and scope - which of the two passes run, and which meshes are in scope at all. The
    // scope is the game's own landscape folders (any "landscape" folder segment: Meshes\Landscape,
    // Meshes\DLC01\Landscape, ...); meshes outside them (architecture terrain bases, campfires,
    // furniture, ...) are left alone unless they match one of the additional-mesh wildcards.
    landscapeSizer->Add(makeSectionLabel(landscapePanel, SFTr("launcher.passes.label", "Passes")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_enableBlendCheckbox = new wxCheckBox(landscapePanel, wxID_ANY,
        SFTr("launcher.passes.blend", "Blend: alpha test to alpha blend on landscape meshes, with derived \"blend\" TextureSets"));
    m_enableBlendCheckbox->SetValue(initParams.enableBlend);
    landscapeSizer->Add(m_enableBlendCheckbox, 0, wxLEFT | wxTOP, BORDER_SIZE * 2);

    m_enableSnowCheckbox = new wxCheckBox(landscapePanel, wxID_ANY,
        SFTr("launcher.passes.snow", "Snow: a mesh copy and a plugin override for every snow record"));
    m_enableSnowCheckbox->SetValue(initParams.enableSnow);
    landscapeSizer->Add(m_enableSnowCheckbox, 0, wxLEFT | wxTOP, BORDER_SIZE * 2);

    landscapeSizer->Add(makeSectionLabel(landscapePanel, SFTr("launcher.scope.label", "Landscape Scope")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE * 2);

    m_restrictToLandscapeCheckbox = new wxCheckBox(landscapePanel, wxID_ANY,
        SFTr("launcher.scope.restrict", "Only process meshes inside a landscape folder"));
    m_restrictToLandscapeCheckbox->SetValue(initParams.restrictToLandscapeFolders);
    landscapeSizer->Add(m_restrictToLandscapeCheckbox, 0, wxLEFT | wxTOP, BORDER_SIZE * 2);

    auto* scopeHelpText = new wxStaticText(landscapePanel, wxID_ANY,
        SFTr("launcher.scope.help",
            "Any mesh whose path has a \"landscape\" folder (Meshes\\Landscape, Meshes\\DLC01\\Landscape, "
            "Meshes\\_ResourcePack\\Landscape, ...) is in scope. Terrain bases, campfires and other meshes "
            "outside those folders are skipped unless they match a pattern below."));
    scopeHelpText->Wrap(HELP_WRAP_PAIRED * 2);
    landscapeSizer->Add(scopeHelpText, 0, wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE * 2);

    landscapeSizer->Add(makeSectionLabel(landscapePanel, SFTr("launcher.meshAddList.label", "Additional Meshes")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE * 2);
    auto* meshAddListHelpText = new wxStaticText(landscapePanel, wxID_ANY,
        SFTr("launcher.meshAddList.help",
            "Meshes outside a landscape folder to process anyway. Wildcards (*) allowed, e.g. "
            "\"*\\whiterun\\wrterrain\\*\". Right click to add/remove rows."));
    meshAddListHelpText->Wrap(HELP_WRAP_PAIRED * 2);
    landscapeSizer->Add(meshAddListHelpText, 0, wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE * 2);

    m_meshAddListCtrl = makeListCtrl(landscapePanel, SFTr("launcher.meshAddList.column", "Pattern"), initParams.meshAddList);
    m_meshAddListCtrl->SetMinSize(wxSize(-1, 90));
    landscapeSizer->Add(m_meshAddListCtrl, 0, wxEXPAND | wxALL, BORDER_SIZE * 2);

    // Landscape vertex color mode - entirely separate feature from the mesh duplication above (LAND
    // terrain records have no NIF/Model.File at all), so it's its own section rather than tucked
    // under an unrelated one. Paired side-by-side with Mesh Vertex Colors below to keep the dialog
    // from growing too tall.
    auto* vertexColorsRowSizer = new wxBoxSizer(wxHORIZONTAL);
    auto* landscapeColumnSizer = new wxBoxSizer(wxVERTICAL);
    auto* meshVertexColumnSizer = new wxBoxSizer(wxVERTICAL);

    landscapeColumnSizer->Add(makeSectionLabel(snowPanel, SFTr("launcher.landscapeVertexColors.label", "Landscape Vertex Colors")), 0,
        wxTOP, BORDER_SIZE);

    auto* landscapeHelpText = new wxStaticText(snowPanel, wxID_ANY,
        SFTr("launcher.landscapeVertexColors.help",
            "Clears baked-in vertex color tinting on terrain (LAND) records, so they render "
            "standalone instead of carrying leftover color grading meant for a different look."));
    landscapeHelpText->Wrap(HELP_WRAP_PAIRED);
    landscapeColumnSizer->Add(landscapeHelpText, 0, wxTOP, BORDER_SIZE);

    m_landscapeModeNoneRadio = new wxRadioButton(snowPanel, wxID_ANY,
        SFTr("launcher.landscapeVertexColors.none", "Don't touch landscape vertex colors"), wxDefaultPosition, wxDefaultSize, wxRB_GROUP);
    m_landscapeModeAllRadio = new wxRadioButton(snowPanel, wxID_ANY,
        SFTr("launcher.landscapeVertexColors.all", "Clear on all terrain"));
    m_landscapeModeSnowRadio = new wxRadioButton(snowPanel, wxID_ANY,
        SFTr("launcher.landscapeVertexColors.snow", "Clear only where a snow-classified texture is used"));

    switch (initParams.landscapeVertexColorMode) {
    case SFLandscapeVertexColorMode::All:
        m_landscapeModeAllRadio->SetValue(true);
        break;
    case SFLandscapeVertexColorMode::SnowOnly:
        m_landscapeModeSnowRadio->SetValue(true);
        break;
    default:
        m_landscapeModeNoneRadio->SetValue(true);
        break;
    }

    landscapeColumnSizer->Add(m_landscapeModeNoneRadio, 0, wxTOP, BORDER_SIZE);
    landscapeColumnSizer->Add(m_landscapeModeAllRadio, 0, wxTOP, BORDER_SIZE);
    landscapeColumnSizer->Add(m_landscapeModeSnowRadio, 0, wxTOP, BORDER_SIZE);

    // Mesh vertex color mode - separate from the LAND-record mode above: this one clears vertex
    // colors on the landscape-folder MESH geometry itself (rocks, cliffs, icebergs, ...), not the
    // terrain records. "All" additionally duplicates non-snow landscape meshes purely to clear their
    // vertex colors too, so terrain and the object meshes sitting on it stay visually consistent
    // once the terrain's own vertex colors are cleared above.
    meshVertexColumnSizer->Add(makeSectionLabel(snowPanel, SFTr("launcher.meshVertexColors.label", "Mesh Vertex Colors")), 0,
        wxTOP, BORDER_SIZE);

    auto* meshVertexColorsHelpText = new wxStaticText(snowPanel, wxID_ANY,
        SFTr("launcher.meshVertexColors.help",
            "Clears baked-in vertex color tinting on landscape-folder meshes (rocks, cliffs, "
            "icebergs, ...), including DLC variants under DLC01\\Landscape\\, DLC02\\Landscape\\, "
            "etc."));
    meshVertexColorsHelpText->Wrap(HELP_WRAP_PAIRED);
    meshVertexColumnSizer->Add(meshVertexColorsHelpText, 0, wxTOP, BORDER_SIZE);

    m_meshVertexColorModeNoneRadio = new wxRadioButton(snowPanel, wxID_ANY,
        SFTr("launcher.meshVertexColors.none", "Don't touch mesh vertex colors"), wxDefaultPosition, wxDefaultSize, wxRB_GROUP);
    m_meshVertexColorModeSnowOnlyRadio = new wxRadioButton(snowPanel, wxID_ANY,
        SFTr("launcher.meshVertexColors.snowOnly", "Only on the generated snow meshes"));
    m_meshVertexColorModeAllRadio = new wxRadioButton(snowPanel, wxID_ANY,
        SFTr("launcher.meshVertexColors.all", "On all landscape meshes"));

    switch (initParams.meshVertexColorMode) {
    case SFMeshVertexColorMode::All:
        m_meshVertexColorModeAllRadio->SetValue(true);
        break;
    case SFMeshVertexColorMode::None:
        m_meshVertexColorModeNoneRadio->SetValue(true);
        break;
    default:
        m_meshVertexColorModeSnowOnlyRadio->SetValue(true);
        break;
    }

    meshVertexColumnSizer->Add(m_meshVertexColorModeNoneRadio, 0, wxTOP, BORDER_SIZE);
    meshVertexColumnSizer->Add(m_meshVertexColorModeSnowOnlyRadio, 0, wxTOP, BORDER_SIZE);
    meshVertexColumnSizer->Add(m_meshVertexColorModeAllRadio, 0, wxTOP, BORDER_SIZE);

    vertexColorsRowSizer->Add(landscapeColumnSizer, 1, wxEXPAND | wxLEFT | wxRIGHT, BORDER_SIZE);
    vertexColorsRowSizer->Add(meshVertexColumnSizer, 1, wxEXPAND | wxLEFT | wxRIGHT, BORDER_SIZE);
    snowSizer->Add(vertexColorsRowSizer, 0, wxEXPAND);

    // Collision material mode - remaps collision materials on the generated snow meshes toward
    // their snow equivalent (currently just DIRT/GRASS -> SNOW, kept deliberately conservative) so
    // footstep sounds match the now-snowy visual. No "All" option, unlike the vertex color mode
    // above - duplicating a non-snow mesh purely to fix its collision wasn't judged worth the extra
    // mesh. Off by default since this is a brand new, gameplay-audible capability. Paired side-by-side
    // with the decal/ice options below - same space-saving reasoning as the pair above.
    auto* collisionOptionsRowSizer = new wxBoxSizer(wxHORIZONTAL);
    auto* collisionColumnSizer = new wxBoxSizer(wxVERTICAL);
    auto* optionsColumnSizer = new wxBoxSizer(wxVERTICAL);

    collisionColumnSizer->Add(makeSectionLabel(snowPanel, SFTr("launcher.collisionMaterial.label", "Collision Material (Footstep Sounds)")), 0,
        wxTOP, BORDER_SIZE);

    auto* collisionMaterialHelpText = new wxStaticText(snowPanel, wxID_ANY,
        SFTr("launcher.collisionMaterial.help",
            "Remaps collision materials on the generated snow meshes toward their snow equivalent "
            "(currently just Dirt/Grass -> Snow) so footstep sounds match the snowy visual. Works "
            "per collision chunk, so unrelated materials in the same mesh are left alone."));
    collisionMaterialHelpText->Wrap(HELP_WRAP_PAIRED);
    collisionColumnSizer->Add(collisionMaterialHelpText, 0, wxTOP, BORDER_SIZE);

    m_collisionMaterialModeNoneRadio = new wxRadioButton(snowPanel, wxID_ANY,
        SFTr("launcher.collisionMaterial.none", "Don't touch collision materials"), wxDefaultPosition, wxDefaultSize, wxRB_GROUP);
    m_collisionMaterialModeSnowOnlyRadio = new wxRadioButton(snowPanel, wxID_ANY,
        SFTr("launcher.collisionMaterial.snowOnly", "Apply on the generated snow meshes"));

    switch (initParams.collisionMaterialMode) {
    case SFCollisionMaterialMode::SnowOnly:
        m_collisionMaterialModeSnowOnlyRadio->SetValue(true);
        break;
    default:
        m_collisionMaterialModeNoneRadio->SetValue(true);
        break;
    }

    collisionColumnSizer->Add(m_collisionMaterialModeNoneRadio, 0, wxTOP, BORDER_SIZE);
    collisionColumnSizer->Add(m_collisionMaterialModeSnowOnlyRadio, 0, wxTOP, BORDER_SIZE);

    // Hide Rocks01/SnowRocks01-textured shapes on mountain/rock/tundra meshes - Skyrim's own dynamic
    // snow shaders (Simplicity of Snow, BDS3, ...) render via the decal pipeline too, so these small
    // detail-rock shapes z-fight with it. NOT applied to DirtCliffs meshes - their own "Skirt" shape
    // must stay. Hides the
    // shape (NiAVObject Hidden flag) instead of deleting it, so block indices - and any plugin-side
    // AltTexture index - never move. Off by default. Modeled on "Enhanced Rocks and Mountains -
    // Blending Patch And Other Fixes" (nexusmods.com/skyrimspecialedition/mods/131170) - see credits.
    optionsColumnSizer->Add(makeSectionLabel(snowPanel, SFTr("launcher.hideDecalShapes.label", "Hide Decal Shapes")), 0,
        wxTOP, BORDER_SIZE * 2);

    m_hideDecalShapesCheckbox = new wxCheckBox(snowPanel, wxID_ANY,
        SFTr("launcher.hideDecalShapes.checkbox", "Hide Rocks01/SnowRocks01 decal shapes"));
    m_hideDecalShapesCheckbox->SetValue(initParams.hideDecalShapes);
    optionsColumnSizer->Add(m_hideDecalShapesCheckbox, 0, wxTOP, BORDER_SIZE);

    auto* hideDecalShapesHelpText = new wxStaticText(snowPanel, wxID_ANY,
        SFTr("launcher.hideDecalShapes.help",
            "Avoids z-fighting with Skyrim's own decal-based dynamic snow shaders by hiding any shape "
            "textured with \"Rocks01\"/\"SnowRocks01\", on mountain/rock/tundra meshes only. Hides the "
            "shape instead of deleting it, so no plugin changes are needed. Does not affect DirtCliffs "
            "meshes."));
    hideDecalShapesHelpText->Wrap(HELP_WRAP_PAIRED);
    optionsColumnSizer->Add(hideDecalShapesHelpText, 0, wxTOP, BORDER_SIZE);

    // Ice snow material removal - clears STAT.DNAM's Material link on every static that points at
    // SnowMaterialGlacier/SnowMaterialGlacierSlab, so no projected snow covers glaciers/ice.
    // Plugin-only change (no mesh is touched). Off by default.
    optionsColumnSizer->Add(makeSectionLabel(snowPanel, SFTr("launcher.removeIceSnowMaterial.label", "Ice Snow Material")), 0,
        wxTOP, BORDER_SIZE * 2);

    m_removeIceSnowMaterialCheckbox = new wxCheckBox(snowPanel, wxID_ANY,
        SFTr("launcher.removeIceSnowMaterial.checkbox", "Remove the glacier snow material from statics"));
    m_removeIceSnowMaterialCheckbox->SetValue(initParams.removeIceSnowMaterial);
    optionsColumnSizer->Add(m_removeIceSnowMaterialCheckbox, 0, wxTOP, BORDER_SIZE);

    auto* removeIceSnowMaterialHelpText = new wxStaticText(snowPanel, wxID_ANY,
        SFTr("launcher.removeIceSnowMaterial.help",
            "Clears the SnowMaterialGlacier / SnowMaterialGlacierSlab Material Object from every static "
            "that uses it, so no snow is projected on glaciers and ice. Only changes LandscapeFixer.esp - no "
            "mesh is modified, and the ice's own shader material is left alone."));
    removeIceSnowMaterialHelpText->Wrap(HELP_WRAP_PAIRED);
    optionsColumnSizer->Add(removeIceSnowMaterialHelpText, 0, wxTOP, BORDER_SIZE);

    collisionOptionsRowSizer->Add(collisionColumnSizer, 1, wxEXPAND | wxLEFT | wxRIGHT, BORDER_SIZE);
    collisionOptionsRowSizer->Add(optionsColumnSizer, 1, wxEXPAND | wxLEFT | wxRIGHT, BORDER_SIZE);
    snowSizer->Add(collisionOptionsRowSizer, 0, wxEXPAND | wxBOTTOM, BORDER_SIZE);

    // Mesh blacklist and EditorID blacklist keywords - inline editable tables, same pattern as
    // AutoBlend's own. Paired side-by-side - same space-saving reasoning as the pairs above.
    auto* blacklistRowSizer = new wxBoxSizer(wxHORIZONTAL);
    auto* meshBlacklistColumnSizer = new wxBoxSizer(wxVERTICAL);
    auto* editorIdKeywordsColumnSizer = new wxBoxSizer(wxVERTICAL);

    meshBlacklistColumnSizer->Add(makeSectionLabel(snowPanel, SFTr("launcher.meshBlacklist.label", "Snow Mesh Blacklist")), 0,
        wxTOP, BORDER_SIZE);

    auto* meshBlacklistHelpText = new wxStaticText(snowPanel, wxID_ANY,
        SFTr("launcher.meshBlacklist.help",
            "Meshes matching a rule here are never duplicated or patched. Wildcards (*) allowed, "
            "e.g. \"*\\trees\\*\". Right click to add/remove rows."));
    meshBlacklistHelpText->Wrap(HELP_WRAP_PAIRED);
    meshBlacklistColumnSizer->Add(meshBlacklistHelpText, 0, wxTOP, BORDER_SIZE);

    m_meshBlacklistCtrl = new PGModifiableListCtrl(
        snowPanel, wxID_ANY, wxDefaultPosition, wxDefaultSize, wxLC_REPORT | wxLC_EDIT_LABELS | wxLC_NO_HEADER);
    m_meshBlacklistCtrl->AppendColumn(SFTr("launcher.meshBlacklist.column", "Rule"), wxLIST_FORMAT_LEFT, wxLIST_AUTOSIZE_USEHEADER);
    m_meshBlacklistCtrl->SetColumnWidth(0, wxLIST_AUTOSIZE_USEHEADER);

    long meshBlacklistIndex = 0;
    for (const auto& rule : initParams.meshBlacklist) {
        m_meshBlacklistCtrl->InsertItem(meshBlacklistIndex++, wxString(rule));
    }
    m_meshBlacklistCtrl->InsertItem(m_meshBlacklistCtrl->GetItemCount(), "");

    meshBlacklistColumnSizer->Add(m_meshBlacklistCtrl, 1, wxEXPAND | wxTOP, BORDER_SIZE);

    // EditorID blacklist keywords - same inline editable table pattern.
    editorIdKeywordsColumnSizer->Add(makeSectionLabel(snowPanel, SFTr("launcher.editorIdKeywords.label", "Snow EditorID Blacklist Keywords")), 0,
        wxTOP, BORDER_SIZE);

    auto* editorIdKeywordsHelpText = new wxStaticText(snowPanel, wxID_ANY,
        SFTr("launcher.editorIdKeywords.help",
            "Records whose EditorID contains one of these words (case-insensitive) are skipped "
            "entirely - useful for excluding false-positive matches. Right click to add/remove rows."));
    editorIdKeywordsHelpText->Wrap(HELP_WRAP_PAIRED);
    editorIdKeywordsColumnSizer->Add(editorIdKeywordsHelpText, 0, wxTOP, BORDER_SIZE);

    m_editorIdKeywordsCtrl = new PGModifiableListCtrl(
        snowPanel, wxID_ANY, wxDefaultPosition, wxDefaultSize, wxLC_REPORT | wxLC_EDIT_LABELS | wxLC_NO_HEADER);
    m_editorIdKeywordsCtrl->AppendColumn(
        SFTr("launcher.editorIdKeywords.column", "Keyword"), wxLIST_FORMAT_LEFT, wxLIST_AUTOSIZE_USEHEADER);
    m_editorIdKeywordsCtrl->SetColumnWidth(0, wxLIST_AUTOSIZE_USEHEADER);

    long editorIdKeywordIndex = 0;
    for (const auto& keyword : initParams.editorIdBlacklistKeywords) {
        m_editorIdKeywordsCtrl->InsertItem(editorIdKeywordIndex++, wxString(keyword));
    }
    m_editorIdKeywordsCtrl->InsertItem(m_editorIdKeywordsCtrl->GetItemCount(), "");

    editorIdKeywordsColumnSizer->Add(m_editorIdKeywordsCtrl, 1, wxEXPAND | wxTOP, BORDER_SIZE);

    blacklistRowSizer->Add(meshBlacklistColumnSizer, 1, wxEXPAND | wxLEFT | wxRIGHT, BORDER_SIZE);
    blacklistRowSizer->Add(editorIdKeywordsColumnSizer, 1, wxEXPAND | wxLEFT | wxRIGHT, BORDER_SIZE);
    snowSizer->Add(blacklistRowSizer, 1, wxEXPAND | wxBOTTOM, BORDER_SIZE);

    for (const auto& panelAndSizer : { std::pair<wxScrolledWindow*, wxBoxSizer*> { setupPanel, setupSizer },
             std::pair<wxScrolledWindow*, wxBoxSizer*> { landscapePanel, landscapeSizer },
             std::pair<wxScrolledWindow*, wxBoxSizer*> { snowPanel, snowSizer } }) {
        panelAndSizer.first->SetSizer(panelAndSizer.second);
        panelAndSizer.first->FitInside();
        forwardChoiceWheelToScrollPanel(panelAndSizer.first, panelAndSizer.first);
        // Without a capped minimum size the dialog would ask the tab for its full unscrolled height
        // and defeat the scrolling just set up.
        panelAndSizer.first->SetMinSize(wxSize(-1, 200));
    }
    notebook->AddPage(setupPanel, SFTr("launcher.tab.setup", "Setup"));
    notebook->AddPage(landscapePanel, SFTr("launcher.tab.landscape", "Landscape"));

    // "Blend" tab - options of the blend pass (the former AutoBlend): which textures may get a
    // synthesized statics sibling, whether PBR slots are generated, and what the pass leaves alone.
    auto* blendPanel = new wxScrolledWindow(notebook);
    blendPanel->SetScrollRate(0, 30);
    auto* blendSizer = new wxBoxSizer(wxVERTICAL);

    auto* blendIntroText = new wxStaticText(blendPanel, wxID_ANY,
        SFTr("launcher.blend.intro",
            "The blend pass flips alpha test to alpha blend on landscape meshes whose textures have a "
            "statics/blend sibling, in place at the mesh's own path, and gives the records that show such a "
            "texture a derived TextureSet. Textures without a sibling can get one generated automatically."));
    blendIntroText->Wrap(500);
    blendSizer->Add(blendIntroText, 0, wxALL, BORDER_SIZE * 2);

    m_generatePbrSlotsCheckbox = new wxCheckBox(blendPanel, wxID_ANY,
        SFTr("launcher.generatePbrSlots.label", "Generate PBR slots"));
    m_generatePbrSlotsCheckbox->SetValue(initParams.generatePbrSlots);
    blendSizer->Add(m_generatePbrSlotsCheckbox, 0, wxALL, BORDER_SIZE * 2);

    blendSizer->Add(makeSectionLabel(blendPanel, SFTr("launcher.autoGenerateAllowlist.label", "Auto-Generate Allowlist")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);
    auto* editAllowlistButton = new wxButton(blendPanel, wxID_ANY,
        SFTr("launcher.autoGenerateAllowlist.editButton", "Edit Allowlist..."));
    editAllowlistButton->Bind(wxEVT_BUTTON, &LauncherWindow::onEditAllowlistButtonPressed, this);
    blendSizer->Add(editAllowlistButton, 0, wxALL, BORDER_SIZE);

    // Built once (not per click) so edits persist across repeated opens, exactly like every other field.
    m_autoGenerateAllowlistDialog = new wxDialog(this, wxID_ANY,
        SFTr("launcher.autoGenerateAllowlist.label", "Auto-Generate Allowlist"), wxDefaultPosition, wxSize(520, 520),
        wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER);
    auto* allowlistDialogSizer = new wxBoxSizer(wxVERTICAL);
    auto* allowlistHelpText = new wxStaticText(m_autoGenerateAllowlistDialog, wxID_ANY,
        SFTr("launcher.autoGenerateAllowlist.help",
            "Only these source diffuse textures (relative to Data, e.g. \"textures\\landscape\\dirt01.dds\") get a "
            "missing statics sibling synthesized automatically. Wildcards (*) allowed. Right click to add/remove rows."));
    allowlistHelpText->Wrap(480);
    allowlistDialogSizer->Add(allowlistHelpText, 0, wxEXPAND | wxALL, BORDER_SIZE);
    m_autoGenerateAllowlistCtrl = makeListCtrl(m_autoGenerateAllowlistDialog,
        SFTr("launcher.autoGenerateAllowlist.column", "Texture"), initParams.autoGenerateAllowlist);
    allowlistDialogSizer->Add(m_autoGenerateAllowlistCtrl, 1, wxEXPAND | wxALL, BORDER_SIZE);
    allowlistDialogSizer->Add(new wxButton(m_autoGenerateAllowlistDialog, wxID_OK, SFTr("common.close", "Close")), 0,
        wxALIGN_RIGHT | wxALL, BORDER_SIZE);
    m_autoGenerateAllowlistDialog->SetSizer(allowlistDialogSizer);
    m_autoGenerateAllowlistDialog->Bind(wxEVT_SIZE, [this](wxSizeEvent& event) -> void {
        fitListColumn(m_autoGenerateAllowlistCtrl);
        event.Skip();
    });

    auto* blendListsRowSizer = new wxBoxSizer(wxHORIZONTAL);
    auto* blendMeshColumnSizer = new wxBoxSizer(wxVERTICAL);
    auto* blendKeywordColumnSizer = new wxBoxSizer(wxVERTICAL);

    blendMeshColumnSizer->Add(makeSectionLabel(blendPanel, SFTr("launcher.blendMeshBlacklist.label", "Blend Mesh Blacklist")), 0, wxTOP, BORDER_SIZE);
    auto* blendMeshHelpText = new wxStaticText(blendPanel, wxID_ANY,
        SFTr("launcher.blendMeshBlacklist.help",
            "Meshes matching a rule here are never patched by the blend pass (glass, ice, ...). Wildcards (*) "
            "allowed. Right click to add/remove rows."));
    blendMeshHelpText->Wrap(HELP_WRAP_BLEND);
    blendMeshColumnSizer->Add(blendMeshHelpText, 0, wxTOP, BORDER_SIZE);
    m_blendMeshBlacklistCtrl = makeListCtrl(blendPanel, SFTr("launcher.meshBlacklist.column", "Rule"), initParams.blendMeshBlacklist);
    blendMeshColumnSizer->Add(m_blendMeshBlacklistCtrl, 1, wxEXPAND | wxTOP, BORDER_SIZE);

    blendKeywordColumnSizer->Add(makeSectionLabel(blendPanel, SFTr("launcher.blendEditorIdKeywords.label", "Blend EditorID Blacklist Keywords")), 0, wxTOP, BORDER_SIZE);
    auto* blendKeywordHelpText = new wxStaticText(blendPanel, wxID_ANY,
        SFTr("launcher.blendEditorIdKeywords.help",
            "Records whose EditorID contains one of these words (case-insensitive) are never touched by the blend "
            "pass. Right click to add/remove rows."));
    blendKeywordHelpText->Wrap(HELP_WRAP_BLEND);
    blendKeywordColumnSizer->Add(blendKeywordHelpText, 0, wxTOP, BORDER_SIZE);
    m_blendEditorIdKeywordsCtrl = makeListCtrl(blendPanel, SFTr("launcher.editorIdKeywords.column", "Keyword"),
        initParams.blendEditorIdBlacklistKeywords);
    blendKeywordColumnSizer->Add(m_blendEditorIdKeywordsCtrl, 1, wxEXPAND | wxTOP, BORDER_SIZE);

    blendListsRowSizer->Add(blendMeshColumnSizer, 1, wxEXPAND | wxLEFT | wxRIGHT, BORDER_SIZE);
    blendListsRowSizer->Add(blendKeywordColumnSizer, 1, wxEXPAND | wxLEFT | wxRIGHT, BORDER_SIZE);
    blendSizer->Add(blendListsRowSizer, 1, wxEXPAND | wxBOTTOM, BORDER_SIZE);

    blendPanel->SetSizer(blendSizer);
    blendPanel->FitInside();
    forwardChoiceWheelToScrollPanel(blendPanel, blendPanel);
    blendPanel->SetMinSize(wxSize(-1, 200));
    notebook->AddPage(blendPanel, SFTr("launcher.tab.blend", "Blend"));
    notebook->AddPage(snowPanel, SFTr("launcher.tab.snow", "Snow"));

    // "Options" tab - app-wide preferences (language, theme) rather than per-run settings, same
    // split AutoBlend uses.
    auto* optionsPanel = new wxPanel(notebook);
    auto* optionsSizer = new wxBoxSizer(wxVERTICAL);

    // Language selector - changing it immediately relaunches the window (see onLanguageChanged)
    // rather than requiring a separate settings dialog/OK click.
    optionsSizer->Add(makeSectionLabel(optionsPanel, SFTr("launcher.language.label", "Language")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    m_languages = SFLocale::getAvailableLanguages();
    wxArrayString languageChoices;
    int selectedLanguageIndex = 0;
    for (size_t i = 0; i < m_languages.size(); i++) {
        languageChoices.Add(m_languages.at(i).displayName);
        if (m_languages.at(i).code == SFLocale::getCurrentLanguage()) {
            selectedLanguageIndex = static_cast<int>(i);
        }
    }
    m_languageChoice = new wxChoice(optionsPanel, wxID_ANY, wxDefaultPosition, wxDefaultSize, languageChoices);
    if (!m_languages.empty()) {
        m_languageChoice->SetSelection(selectedLanguageIndex);
    }
    m_languageChoice->Bind(wxEVT_CHOICE, &LauncherWindow::onLanguageChanged, this);
    optionsSizer->Add(m_languageChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

    // Theme selector - same immediate-relaunch pattern as the language selector, since applying a
    // wxWidgets appearance change requires it to be set before the window it affects is created.
    optionsSizer->Add(makeSectionLabel(optionsPanel, SFTr("launcher.theme.label", "Theme")), 0,
        wxLEFT | wxRIGHT | wxTOP, BORDER_SIZE);

    wxArrayString themeChoices;
    themeChoices.Add(SFTr("launcher.theme.system", "System"));
    themeChoices.Add(SFTr("launcher.theme.light", "Light"));
    themeChoices.Add(SFTr("launcher.theme.dark", "Dark"));
    m_themeChoice = new wxChoice(optionsPanel, wxID_ANY, wxDefaultPosition, wxDefaultSize, themeChoices);
    int selectedThemeIndex = 0;
    if (initParams.uiTheme == "light") {
        selectedThemeIndex = 1;
    } else if (initParams.uiTheme == "dark") {
        selectedThemeIndex = 2;
    }
    m_themeChoice->SetSelection(selectedThemeIndex);
    m_themeChoice->Bind(wxEVT_CHOICE, &LauncherWindow::onThemeChanged, this);
    optionsSizer->Add(m_themeChoice, 0, wxEXPAND | wxALL, BORDER_SIZE);

    optionsPanel->SetSizer(optionsSizer);
    notebook->AddPage(optionsPanel, SFTr("launcher.tab.options", "Options"));

    // Developer aid: LANDSCAPEFIXER_TAB=<n> opens the launcher on that tab (used to take screenshots of
    // every tab without sending any input to the window).
    char* startTab = nullptr;
    size_t startTabLength = 0;
    if (_dupenv_s(&startTab, &startTabLength, "LANDSCAPEFIXER_TAB") == 0 && startTab != nullptr) {
        const int tab = atoi(startTab);
        if (tab >= 0 && tab < static_cast<int>(notebook->GetPageCount())) {
            notebook->SetSelection(static_cast<size_t>(tab));
        }
        free(startTab);
    }

    mainSizer->Add(notebook, 1, wxEXPAND | wxALL, BORDER_SIZE);

    Bind(wxEVT_SIZE, [this](wxSizeEvent& event) -> void {
        updateListColumnWidths();
        event.Skip();
    });
    m_meshBlacklistCtrl->Bind(pgEVT_LISTCTRL_CHANGED, [this](PGCustomListctrlChangedEvent& event) -> void {
        updateListColumnWidths();
        event.Skip();
    });
    m_editorIdKeywordsCtrl->Bind(pgEVT_LISTCTRL_CHANGED, [this](PGCustomListctrlChangedEvent& event) -> void {
        updateListColumnWidths();
        event.Skip();
    });
    for (auto* ctrl : { m_meshAddListCtrl, m_blendMeshBlacklistCtrl, m_blendEditorIdKeywordsCtrl, m_autoGenerateAllowlistCtrl }) {
        ctrl->Bind(pgEVT_LISTCTRL_CHANGED, [this](PGCustomListctrlChangedEvent& event) -> void {
            updateListColumnWidths();
            event.Skip();
        });
    }

    mainSizer->Add(new wxStaticLine(this, wxID_ANY), 0, wxEXPAND | wxALL, BORDER_SIZE);

    // Buttons
    auto* buttonSizer = new wxBoxSizer(wxHORIZONTAL);
    auto* cancelButton = new wxButton(this, wxID_CANCEL, SFTr("common.cancel", "Cancel"));
    m_okButton = new wxButton(this, wxID_ANY, SFTr("launcher.startButton", "Start"));
    m_okButton->SetBackgroundColour(ACCENT);
    m_okButton->SetForegroundColour(ACCENT_TEXT);
    m_okButton->Bind(wxEVT_BUTTON, &LauncherWindow::onOkButtonPressed, this);
    buttonSizer->AddStretchSpacer();
    buttonSizer->Add(cancelButton, 0, wxALL, BORDER_SIZE);
    buttonSizer->Add(m_okButton, 0, wxALL, BORDER_SIZE);
    mainSizer->Add(buttonSizer, 0, wxEXPAND);

    // Deliberately SetSizer, not SetSizerAndFit: Fit() would resize the dialog to the sizer's own
    // computed minimum size, which - even with each tab's min size capped above - can still
    // grow well past this constructor's own carefully-chosen initial wxSize(760, 620) as more
    // settings get added over time. Keeping the dialog at its authored initial size and letting
    // each tab's own scrollbar absorb whatever doesn't fit is what actually keeps this working
    // on smaller screens instead of merely delaying the same problem.
    SetSizer(mainSizer);
    updateGameTypeFieldState();
    updateListColumnWidths();
}

void LauncherWindow::getParams(SFParams& outParams) const
{
    outParams.uiLanguage = SFLocale::getCurrentLanguage();
    switch (m_themeChoice->GetSelection()) {
    case 1:
        outParams.uiTheme = "light";
        break;
    case 2:
        outParams.uiTheme = "dark";
        break;
    default:
        outParams.uiTheme = "system";
        break;
    }

    outParams.gameLocation = m_gameLocationTextbox->GetValue().ToStdWstring();
    outParams.gameType = m_gameTypeChoice->GetSelection() == 1 ? SFGameType::SkyrimLE : SFGameType::SkyrimSE;
    outParams.outputLocation = m_outputLocationTextbox->GetValue().ToStdWstring();

    outParams.modManager
        = m_modManagerChoice->GetSelection() == 1 ? SFModManagerType::ModOrganizer2 : SFModManagerType::None;
    outParams.mo2InstancePath = m_mo2InstancePathTextbox->GetValue().ToStdWstring();
    outParams.mo2ProfileName = m_mo2ProfileChoice->GetSelection() == wxNOT_FOUND
        ? wstring {}
        : m_mo2ProfileChoice->GetStringSelection().ToStdWstring();

    if (m_meshVertexColorModeAllRadio->GetValue()) {
        outParams.meshVertexColorMode = SFMeshVertexColorMode::All;
    } else if (m_meshVertexColorModeNoneRadio->GetValue()) {
        outParams.meshVertexColorMode = SFMeshVertexColorMode::None;
    } else {
        outParams.meshVertexColorMode = SFMeshVertexColorMode::SnowOnly;
    }

    if (m_collisionMaterialModeSnowOnlyRadio->GetValue()) {
        outParams.collisionMaterialMode = SFCollisionMaterialMode::SnowOnly;
    } else {
        outParams.collisionMaterialMode = SFCollisionMaterialMode::None;
    }

    if (m_landscapeModeAllRadio->GetValue()) {
        outParams.landscapeVertexColorMode = SFLandscapeVertexColorMode::All;
    } else if (m_landscapeModeSnowRadio->GetValue()) {
        outParams.landscapeVertexColorMode = SFLandscapeVertexColorMode::SnowOnly;
    } else {
        outParams.landscapeVertexColorMode = SFLandscapeVertexColorMode::None;
    }

    outParams.meshBlacklist.clear();
    long item = -1;
    while ((item = m_meshBlacklistCtrl->GetNextItem(item)) != -1) {
        const wxString text = m_meshBlacklistCtrl->GetItemText(item);
        if (!text.IsEmpty()) {
            outParams.meshBlacklist.push_back(text.ToStdWstring());
        }
    }

    outParams.editorIdBlacklistKeywords.clear();
    item = -1;
    while ((item = m_editorIdKeywordsCtrl->GetNextItem(item)) != -1) {
        const wxString text = m_editorIdKeywordsCtrl->GetItemText(item);
        if (!text.IsEmpty()) {
            outParams.editorIdBlacklistKeywords.push_back(text.ToStdWstring());
        }
    }

    outParams.hideDecalShapes = m_hideDecalShapesCheckbox->GetValue();
    outParams.removeIceSnowMaterial = m_removeIceSnowMaterialCheckbox->GetValue();

    outParams.enableBlend = m_enableBlendCheckbox->GetValue();
    outParams.enableSnow = m_enableSnowCheckbox->GetValue();
    outParams.restrictToLandscapeFolders = m_restrictToLandscapeCheckbox->GetValue();
    readListCtrl(m_meshAddListCtrl, outParams.meshAddList);
    readListCtrl(m_blendMeshBlacklistCtrl, outParams.blendMeshBlacklist);
    readListCtrl(m_blendEditorIdKeywordsCtrl, outParams.blendEditorIdBlacklistKeywords);
    outParams.generatePbrSlots = m_generatePbrSlotsCheckbox->GetValue();
    readListCtrl(m_autoGenerateAllowlistCtrl, outParams.autoGenerateAllowlist);
}

void LauncherWindow::onLanguageChanged([[maybe_unused]] wxCommandEvent& event)
{
    const int selection = m_languageChoice->GetSelection();
    if (selection == wxNOT_FOUND) {
        return;
    }

    const auto& selectedLang = m_languages.at(static_cast<size_t>(selection));
    if (selectedLang.code == SFLocale::getCurrentLanguage()) {
        return;
    }

    commitPendingListEdits();
    SFLocale::init(m_exePath / "LandscapeFixer_translations", selectedLang.code);
    EndModal(RESULT_RELAUNCH);
}

void LauncherWindow::onThemeChanged([[maybe_unused]] wxCommandEvent& event)
{
    // Unlike a language change, this needs a full process restart, not just an internal rebuild -
    // wx's MSW dark mode support is a one-way switch once enabled for a process. See main.cpp's
    // handling of RESULT_RESTART.
    commitPendingListEdits();
    EndModal(RESULT_RESTART);
}

void LauncherWindow::onBrowseGameLocation([[maybe_unused]] wxCommandEvent& event)
{
    wxDirDialog dialog(this, SFTr("launcher.gameLocation.dialogTitle", "Select Game Location"), m_gameLocationTextbox->GetValue());
    if (dialog.ShowModal() == wxID_OK) {
        m_gameLocationTextbox->SetValue(dialog.GetPath());
    }
}

void LauncherWindow::onBrowseOutputLocation([[maybe_unused]] wxCommandEvent& event)
{
    wxDirDialog dialog(
        this, SFTr("launcher.outputLocation.dialogTitle", "Select Output Location"), m_outputLocationTextbox->GetValue());
    if (dialog.ShowModal() == wxID_OK) {
        m_outputLocationTextbox->SetValue(dialog.GetPath());
    }
}

void LauncherWindow::onModManagerChanged([[maybe_unused]] wxCommandEvent& event)
{
    updateMo2FieldState();
    if (m_modManagerChoice->GetSelection() == 1) {
        refreshMo2Profiles();
    }
}

void LauncherWindow::updateMo2FieldState()
{
    const bool isMo2 = m_modManagerChoice->GetSelection() == 1;
    m_mo2InstancePathLabel->Enable(isMo2);
    m_mo2InstancePathTextbox->Enable(isMo2);
    m_mo2InstanceBrowseButton->Enable(isMo2);
    m_mo2ProfileLabel->Enable(isMo2);
    m_mo2ProfileChoice->Enable(isMo2);
}

void LauncherWindow::onBrowseMo2Instance([[maybe_unused]] wxCommandEvent& event)
{
    wxDirDialog dialog(
        this, SFTr("launcher.mo2InstancePath.dialogTitle", "Select MO2 Instance Folder"), m_mo2InstancePathTextbox->GetValue());
    if (dialog.ShowModal() == wxID_OK) {
        m_mo2InstancePathTextbox->SetValue(dialog.GetPath());
        refreshMo2Profiles();
    }
}

void LauncherWindow::onMo2InstancePathChanged([[maybe_unused]] wxCommandEvent& event) { refreshMo2Profiles(); }

void LauncherWindow::onLoadConfig([[maybe_unused]] wxCommandEvent& event)
{
    wxFileDialog dialog(this, SFTr("launcher.configProfile.loadDialogTitle", "Load Config"), wxEmptyString, wxEmptyString,
        SFTr("launcher.configProfile.fileFilter", "JSON files (*.json)|*.json|All files (*.*)|*.*"),
        wxFD_OPEN | wxFD_FILE_MUST_EXIST);
    if (dialog.ShowModal() != wxID_OK) {
        return;
    }

    applyLoadedParams(SFConfig::loadFrom(filesystem::path(dialog.GetPath().ToStdWstring())));
}

void LauncherWindow::onSaveConfigAs([[maybe_unused]] wxCommandEvent& event)
{
    wxFileDialog dialog(this, SFTr("launcher.configProfile.saveDialogTitle", "Save Config As"), wxEmptyString,
        "LandscapeFixer_config.json", SFTr("launcher.configProfile.fileFilter", "JSON files (*.json)|*.json|All files (*.*)|*.*"),
        wxFD_SAVE | wxFD_OVERWRITE_PROMPT);
    if (dialog.ShowModal() != wxID_OK) {
        return;
    }

    SFParams current;
    getParams(current);
    SFConfig::saveTo(filesystem::path(dialog.GetPath().ToStdWstring()), current);
}

void LauncherWindow::applyLoadedParams(const SFParams& params)
{
    // uiLanguage/uiTheme are deliberately left untouched - those are this user's own app-wide
    // preference for how the tool looks, not part of a per-job profile (which game/output/mod
    // manager to target), so loading a profile shouldn't change how the window you're looking at
    // right now is themed or translated.
    m_gameLocationTextbox->SetValue(params.gameLocation);
    m_gameTypeChoice->SetSelection(params.gameType == SFGameType::SkyrimLE ? 1 : 0);
    m_outputLocationTextbox->SetValue(params.outputLocation);

    m_modManagerChoice->SetSelection(params.modManager == SFModManagerType::ModOrganizer2 ? 1 : 0);
    m_mo2InstancePathTextbox->SetValue(params.mo2InstancePath);
    refreshMo2Profiles();
    if (!params.mo2ProfileName.empty()) {
        m_mo2ProfileChoice->SetStringSelection(wxString(params.mo2ProfileName));
    }

    switch (params.landscapeVertexColorMode) {
    case SFLandscapeVertexColorMode::All:
        m_landscapeModeAllRadio->SetValue(true);
        break;
    case SFLandscapeVertexColorMode::SnowOnly:
        m_landscapeModeSnowRadio->SetValue(true);
        break;
    default:
        m_landscapeModeNoneRadio->SetValue(true);
        break;
    }

    switch (params.meshVertexColorMode) {
    case SFMeshVertexColorMode::All:
        m_meshVertexColorModeAllRadio->SetValue(true);
        break;
    case SFMeshVertexColorMode::None:
        m_meshVertexColorModeNoneRadio->SetValue(true);
        break;
    default:
        m_meshVertexColorModeSnowOnlyRadio->SetValue(true);
        break;
    }

    if (params.collisionMaterialMode == SFCollisionMaterialMode::SnowOnly) {
        m_collisionMaterialModeSnowOnlyRadio->SetValue(true);
    } else {
        m_collisionMaterialModeNoneRadio->SetValue(true);
    }

    m_meshBlacklistCtrl->DeleteAllItems();
    long meshBlacklistIndex = 0;
    for (const auto& rule : params.meshBlacklist) {
        m_meshBlacklistCtrl->InsertItem(meshBlacklistIndex++, wxString(rule));
    }
    m_meshBlacklistCtrl->InsertItem(m_meshBlacklistCtrl->GetItemCount(), "");

    m_editorIdKeywordsCtrl->DeleteAllItems();
    long editorIdKeywordIndex = 0;
    for (const auto& keyword : params.editorIdBlacklistKeywords) {
        m_editorIdKeywordsCtrl->InsertItem(editorIdKeywordIndex++, wxString(keyword));
    }
    m_editorIdKeywordsCtrl->InsertItem(m_editorIdKeywordsCtrl->GetItemCount(), "");

    m_hideDecalShapesCheckbox->SetValue(params.hideDecalShapes);
    m_removeIceSnowMaterialCheckbox->SetValue(params.removeIceSnowMaterial);

    m_enableBlendCheckbox->SetValue(params.enableBlend);
    m_enableSnowCheckbox->SetValue(params.enableSnow);
    m_restrictToLandscapeCheckbox->SetValue(params.restrictToLandscapeFolders);
    fillListCtrl(m_meshAddListCtrl, params.meshAddList);
    fillListCtrl(m_blendMeshBlacklistCtrl, params.blendMeshBlacklist);
    fillListCtrl(m_blendEditorIdKeywordsCtrl, params.blendEditorIdBlacklistKeywords);
    m_generatePbrSlotsCheckbox->SetValue(params.generatePbrSlots);
    fillListCtrl(m_autoGenerateAllowlistCtrl, params.autoGenerateAllowlist);
    updateGameTypeFieldState();

    updateListColumnWidths();
}

void LauncherWindow::refreshMo2Profiles()
{
    if (m_mo2ProfileChoice == nullptr) {
        return;
    }

    const auto previousSelection = m_mo2ProfileChoice->GetStringSelection().ToStdWstring();
    const auto instancePath = m_mo2InstancePathTextbox->GetValue().ToStdWstring();
    m_mo2ProfileChoice->Clear();
    if (instancePath.empty()) {
        updateMo2FieldState();
        return;
    }

    const auto instancePathUtf8 = StringUtil::utf16toUTF8(instancePath);
    const auto rawPtr = get_mo2_profiles(toIntPtr(instancePathUtf8));
    const auto jsonText = fromIntPtr(rawPtr);
    if (rawPtr != 0) {
        free_string(rawPtr);
    }

    try {
        const auto response = nlohmann::json::parse(jsonText);
        if (response.contains("Profiles") && response["Profiles"].is_array()) {
            for (const auto& profile : response["Profiles"]) {
                m_mo2ProfileChoice->Append(wxString::FromUTF8(profile.get<string>()));
            }
        }

        string selected;
        if (response.contains("SelectedProfile") && !response["SelectedProfile"].is_null()) {
            selected = response["SelectedProfile"].get<string>();
        }
        const auto preferred = !previousSelection.empty() ? previousSelection : StringUtil::utf8toUTF16(selected);
        if (!preferred.empty() && m_mo2ProfileChoice->SetStringSelection(wxString(preferred))) {
            // Preserve the user's explicit choice when the textbox emits another change event.
        } else if (m_mo2ProfileChoice->GetCount() > 0) {
            m_mo2ProfileChoice->SetSelection(0);
        }
    } catch (const exception&) {
        // An invalid path simply leaves an empty picker; Start reports the actionable error.
    }
    updateMo2FieldState();
}

void LauncherWindow::updateListColumnWidths()
{
    fitListColumn(m_meshBlacklistCtrl);
    fitListColumn(m_editorIdKeywordsCtrl);
    fitListColumn(m_meshAddListCtrl);
    fitListColumn(m_blendMeshBlacklistCtrl);
    fitListColumn(m_blendEditorIdKeywordsCtrl);
    fitListColumn(m_autoGenerateAllowlistCtrl);
}

void LauncherWindow::onGameTypeChanged([[maybe_unused]] wxCommandEvent& event) { updateGameTypeFieldState(); }

// PBR slots rely on TruePBR (Community Shaders), which only exists on Skyrim SE - on Legendary Edition
// the option is unchecked and disabled instead of looking like a working option that does nothing.
void LauncherWindow::updateGameTypeFieldState()
{
    const bool isLe = m_gameTypeChoice->GetSelection() == 1;
    if (isLe) {
        m_generatePbrSlotsCheckbox->SetValue(false);
    }
    m_generatePbrSlotsCheckbox->Enable(!isLe);
}

void LauncherWindow::onEditAllowlistButtonPressed([[maybe_unused]] wxCommandEvent& event)
{
    m_autoGenerateAllowlistDialog->ShowModal();
}

void LauncherWindow::commitPendingListEdits()
{
    // If the user just typed into one of the modifiable list controls' own trailing blank row
    // (Mesh Blacklist / EditorID Keywords) and then triggered a modal close directly - without
    // pressing Enter or clicking elsewhere first to commit that in-place label edit - wxListCtrl
    // still has the edit control open, and GetItemText() on that row keeps returning the OLD
    // (empty) text until the edit is actually ended. getParams() would then silently drop whatever
    // was just typed, with no error. EndEditLabel(false) commits (does not cancel) any edit in
    // progress on each control; a no-op if that control has no edit open at all. Mirrors AutoBlend's
    // own LauncherWindow.cpp, which added this after a real report of exactly this data loss.
    m_meshBlacklistCtrl->EndEditLabel(false);
    m_editorIdKeywordsCtrl->EndEditLabel(false);
    m_meshAddListCtrl->EndEditLabel(false);
    m_blendMeshBlacklistCtrl->EndEditLabel(false);
    m_blendEditorIdKeywordsCtrl->EndEditLabel(false);
    m_autoGenerateAllowlistCtrl->EndEditLabel(false);
}

void LauncherWindow::onOkButtonPressed([[maybe_unused]] wxCommandEvent& event)
{
    commitPendingListEdits();

    if (m_gameLocationTextbox->GetValue().IsEmpty()) {
        wxMessageBox(SFTr("launcher.missingGameLocation.message", "Please select your game's install location."),
            SFTr("launcher.missingGameLocation.title", "Missing Game Location"), wxOK | wxICON_WARNING, this);
        return;
    }

    if (m_outputLocationTextbox->GetValue().IsEmpty()) {
        wxMessageBox(SFTr("launcher.missingOutputLocation.message", "Please select an output location."),
            SFTr("launcher.missingOutputLocation.title", "Missing Output Location"), wxOK | wxICON_WARNING, this);
        return;
    }

    if (m_modManagerChoice->GetSelection() == 1 && m_mo2InstancePathTextbox->GetValue().IsEmpty()) {
        wxMessageBox(SFTr("launcher.missingMo2Instance.message", "Please select your Mod Organizer 2 instance folder."),
            SFTr("launcher.missingMo2Instance.title", "Missing MO2 Instance"), wxOK | wxICON_WARNING, this);
        return;
    }

    if (m_modManagerChoice->GetSelection() == 1 && m_mo2ProfileChoice->GetSelection() == wxNOT_FOUND) {
        wxMessageBox(SFTr("launcher.missingMo2Profile.message",
                         "No MO2 profile is selected. Make sure the MO2 Instance Path points to your MO2 instance "
                         "(the folder containing ModOrganizer.ini, or the folder that holds your mods and profiles), "
                         "then pick a profile."),
            SFTr("launcher.missingMo2Profile.title", "Missing MO2 Profile"), wxOK | wxICON_WARNING, this);
        return;
    }

    EndModal(wxID_OK);
}
