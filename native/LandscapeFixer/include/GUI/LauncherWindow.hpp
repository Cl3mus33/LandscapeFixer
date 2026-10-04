#pragma once

#include "SFConfig.hpp"
#include "SFLocale.hpp"
#include "GUI/components/PGModifiableListCtrl.hpp"
#include "GUI/components/SectionCard.hpp"

#include <wx/wx.h>

#include <filesystem>
#include <vector>

/**
 * @class LauncherWindow
 * @brief The main settings dialog - General tab (Game Location, Output Location, Mod Manager/MO2
 * Instance Path/Profile, Landscape Vertex Color mode, Mesh Vertex Color mode, Mesh Blacklist, EditorID
 * Blacklist Keywords) and Options tab (Language, Theme). Trimmed down from AutoBlend's own
 * LauncherWindow: no auto-generate allowlist, no PBR checkbox - neither exists in
 * LandscapeFixer.Core.Configuration.ExtractSettings.
 */
class LauncherWindow : public wxDialog {
public:
    constexpr static int RESULT_RELAUNCH = wxID_HIGHEST + 1;
    constexpr static int RESULT_RESTART = wxID_HIGHEST + 2;

    LauncherWindow(const SFParams& initParams, std::filesystem::path exePath);

    void getParams(SFParams& outParams) const;

private:
    std::filesystem::path m_exePath;
    std::vector<SFLocale::Language> m_languages;

    int m_themeSelection = 0; // 0 system, 1 light, 2 dark
    wxTextCtrl* m_gameLocationTextbox = nullptr;
    wxChoice* m_gameTypeChoice = nullptr;
    wxTextCtrl* m_outputLocationTextbox = nullptr;
    wxChoice* m_modManagerChoice = nullptr;
    wxStaticText* m_mo2InstancePathLabel = nullptr;
    wxTextCtrl* m_mo2InstancePathTextbox = nullptr;
    wxButton* m_mo2InstanceBrowseButton = nullptr;
    wxStaticText* m_mo2ProfileLabel = nullptr;
    wxChoice* m_mo2ProfileChoice = nullptr;
    wxRadioButton* m_landscapeModeNoneRadio = nullptr;
    wxRadioButton* m_landscapeModeAllRadio = nullptr;
    wxRadioButton* m_landscapeModeSnowRadio = nullptr;
    wxRadioButton* m_meshVertexColorModeNoneRadio = nullptr;
    wxRadioButton* m_meshVertexColorModeSnowOnlyRadio = nullptr;
    wxRadioButton* m_meshVertexColorModeAllRadio = nullptr;
    wxRadioButton* m_collisionMaterialModeNoneRadio = nullptr;
    wxRadioButton* m_collisionMaterialModeSnowOnlyRadio = nullptr;
    PGModifiableListCtrl* m_meshBlacklistCtrl = nullptr;
    PGModifiableListCtrl* m_editorIdKeywordsCtrl = nullptr;
    wxCheckBox* m_hideDecalShapesCheckbox = nullptr;
    wxCheckBox* m_removeIceSnowMaterialCheckbox = nullptr;
    wxCheckBox* m_enableBlendCheckbox = nullptr;
    wxCheckBox* m_enableSnowCheckbox = nullptr;
    wxCheckBox* m_restrictToLandscapeCheckbox = nullptr;
    PGModifiableListCtrl* m_meshAddListCtrl = nullptr;
    PGModifiableListCtrl* m_blendMeshBlacklistCtrl = nullptr;
    PGModifiableListCtrl* m_blendEditorIdKeywordsCtrl = nullptr;
    PGModifiableListCtrl* m_autoGenerateAllowlistCtrl = nullptr;
    wxDialog* m_autoGenerateAllowlistDialog = nullptr;
    wxCheckBox* m_generatePbrSlotsCheckbox = nullptr;
    wxButton* m_okButton = nullptr;
    SectionCard* m_blendCard = nullptr;
    SectionCard* m_snowCard = nullptr;

    void onPreferences(wxCommandEvent&);
    void onLoadConfig(wxCommandEvent&);
    void onSaveConfigAs(wxCommandEvent&);
    void applyLoadedParams(const SFParams& params);
    void onBrowseGameLocation(wxCommandEvent&);
    void onBrowseOutputLocation(wxCommandEvent&);
    void onModManagerChanged(wxCommandEvent&);
    void onGameTypeChanged(wxCommandEvent&);
    void updateGameTypeFieldState();
    void onEditAllowlistButtonPressed(wxCommandEvent&);
    void onBrowseMo2Instance(wxCommandEvent&);
    void onMo2InstancePathChanged(wxCommandEvent&);
    void refreshMo2Profiles();
    void updateMo2FieldState();
    void commitPendingListEdits();
    void updateListColumnWidths();
    void onOkButtonPressed(wxCommandEvent&);
};
