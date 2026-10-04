#include "GUI/components/SectionCard.hpp"

#include <wx/settings.h>

namespace {
constexpr int PAD = 10;

auto cardHeaderColour(bool hovered) -> wxColour
{
    const wxColour base = wxSystemSettings::GetColour(wxSYS_COLOUR_WINDOW);
    const bool dark = wxSystemSettings::GetAppearance().IsDark();
    // A card header one step away from the window colour: lighter on a dark theme, darker on a light one.
    const int level = dark ? (hovered ? 135 : 118) : (hovered ? 90 : 95);
    return base.ChangeLightness(level);
}
}

SectionCard::SectionCard(wxWindow* parent, const wxString& title, const wxString& description, bool hasToggle,
    bool toggleValue, bool expanded, std::function<void()> onLayoutChanged)
    : wxPanel(parent, wxID_ANY)
    , m_expanded(expanded)
    , m_onLayoutChanged(std::move(onLayoutChanged))
{
    auto* cardSizer = new wxBoxSizer(wxVERTICAL);

    // ---- header ----
    m_header = new wxPanel(this, wxID_ANY);
    auto* headerSizer = new wxBoxSizer(wxHORIZONTAL);

    m_chevron = new wxStaticText(m_header, wxID_ANY, m_expanded ? wxString(wxUniChar(0x25BE)) : wxString(wxUniChar(0x25B8)));
    wxFont chevronFont = m_chevron->GetFont();
    chevronFont.SetPointSize(chevronFont.GetPointSize() + 3);
    m_chevron->SetFont(chevronFont);
    headerSizer->Add(m_chevron, 0, wxLEFT | wxRIGHT | wxALIGN_CENTER_VERTICAL, PAD);

    if (hasToggle) {
        m_toggle = new wxCheckBox(m_header, wxID_ANY, "");
        m_toggle->SetValue(toggleValue);
        headerSizer->Add(m_toggle, 0, wxRIGHT | wxALIGN_CENTER_VERTICAL, 4);
    }

    m_title = new wxStaticText(m_header, wxID_ANY, title);
    wxFont titleFont = m_title->GetFont();
    titleFont.SetPointSize(titleFont.GetPointSize() + 2);
    titleFont.SetWeight(wxFONTWEIGHT_BOLD);
    m_title->SetFont(titleFont);
    headerSizer->Add(m_title, 0, wxALIGN_CENTER_VERTICAL);

    m_description = new wxStaticText(m_header, wxID_ANY, description);
    // Dimmer than the title but still readable on both themes (GRAYTEXT was too faint on the dark one).
    m_description->SetForegroundColour(wxSystemSettings::GetColour(wxSYS_COLOUR_WINDOWTEXT).ChangeLightness(wxSystemSettings::GetAppearance().IsDark() ? 68 : 140));
    headerSizer->Add(m_description, 1, wxLEFT | wxRIGHT | wxALIGN_CENTER_VERTICAL, PAD * 2);

    auto* headerOuter = new wxBoxSizer(wxVERTICAL);
    headerOuter->Add(headerSizer, 1, wxEXPAND | wxTOP | wxBOTTOM, PAD);
    m_header->SetSizer(headerOuter);
    cardSizer->Add(m_header, 0, wxEXPAND);

    // ---- body ----
    m_body = new wxPanel(this, wxID_ANY);
    cardSizer->Add(m_body, 0, wxEXPAND | wxLEFT | wxRIGHT | wxBOTTOM, PAD);
    m_body->Show(m_expanded);
    if (m_toggle != nullptr) {
        m_body->Enable(m_toggle->GetValue());
    }

    SetSizer(cardSizer);

    // ---- interaction ----
    for (wxWindow* clickable : { static_cast<wxWindow*>(m_header), static_cast<wxWindow*>(m_chevron),
             static_cast<wxWindow*>(m_title), static_cast<wxWindow*>(m_description) }) {
        clickable->SetCursor(wxCursor(wxCURSOR_HAND));
        clickable->Bind(wxEVT_LEFT_DOWN, [this](wxMouseEvent&) { onHeaderClicked(); });
        clickable->Bind(wxEVT_ENTER_WINDOW, [this](wxMouseEvent& event) {
            applyColors(true);
            event.Skip();
        });
        clickable->Bind(wxEVT_LEAVE_WINDOW, [this](wxMouseEvent& event) {
            applyColors(false);
            event.Skip();
        });
    }

    if (m_toggle != nullptr) {
        m_toggle->Bind(wxEVT_CHECKBOX, [this](wxCommandEvent&) { onToggleChanged(); });
    }

    applyColors(false);
}

void SectionCard::applyColors(bool hovered)
{
    const wxColour colour = cardHeaderColour(hovered);
    m_header->SetBackgroundColour(colour);
    for (wxWindow* child : { static_cast<wxWindow*>(m_chevron), static_cast<wxWindow*>(m_title),
             static_cast<wxWindow*>(m_description) }) {
        child->SetBackgroundColour(colour);
    }
    if (m_toggle != nullptr) {
        m_toggle->SetBackgroundColour(colour);
    }
    m_header->Refresh();
}

void SectionCard::setExpanded(bool expanded)
{
    if (m_expanded == expanded) {
        return;
    }

    m_expanded = expanded;
    m_chevron->SetLabel(m_expanded ? wxString(wxUniChar(0x25BE)) : wxString(wxUniChar(0x25B8)));
    m_body->Show(m_expanded);
    if (wxWindow* parent = GetParent(); parent != nullptr) {
        parent->Layout();
    }
    if (m_onLayoutChanged) {
        m_onLayoutChanged();
    }
}

void SectionCard::setDescription(const wxString& description)
{
    m_description->SetLabel(description);
    m_header->Layout();
}

void SectionCard::syncFromToggle()
{
    if (m_toggle != nullptr) {
        onToggleChanged();
    }
}

void SectionCard::onHeaderClicked() { setExpanded(!m_expanded); }

void SectionCard::onToggleChanged()
{
    const bool on = m_toggle->GetValue();
    m_body->Enable(on);
    // Switching a pass on opens its settings; switching it off folds the card away.
    setExpanded(on);
}
