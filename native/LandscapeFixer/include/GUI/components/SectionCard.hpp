#pragma once

#include <wx/checkbox.h>
#include <wx/panel.h>
#include <wx/stattext.h>
#include <wx/wx.h>

#include <functional>

/**
 * @class SectionCard
 * @brief A collapsible "card": a clickable header (chevron, optional on/off checkbox, title, one-line
 * description) above a body panel that folds away. Ticking the header checkbox unfolds the card and
 * enables its body; unticking it folds the card and greys the body out - so a pass that is switched
 * off takes one line, and a pass that is switched on can be configured and folded back to save space.
 *
 * Children of the card's settings are created with body() as their parent and laid out with a sizer
 * set on body().
 */
class SectionCard : public wxPanel {
public:
    /**
     * @param hasToggle whether the header carries an on/off checkbox (a pass) or not (a plain section).
     * @param onLayoutChanged called after the card folds/unfolds, so the owner can re-fit its scroll area.
     */
    SectionCard(wxWindow* parent, const wxString& title, const wxString& description, bool hasToggle, bool toggleValue,
        bool expanded, std::function<void()> onLayoutChanged);

    [[nodiscard]] auto body() const -> wxPanel* { return m_body; }
    [[nodiscard]] auto toggle() const -> wxCheckBox* { return m_toggle; }
    [[nodiscard]] auto isExpanded() const -> bool { return m_expanded; }

    void setExpanded(bool expanded);

    /// Replaces the one-line description shown in the header (e.g. a summary of the current choices).
    void setDescription(const wxString& description);

    /// Re-applies the header checkbox to the card (enabled body, folded/unfolded) after its value was set from code.
    void syncFromToggle();

private:
    wxPanel* m_header = nullptr;
    wxPanel* m_body = nullptr;
    wxStaticText* m_chevron = nullptr;
    wxCheckBox* m_toggle = nullptr;
    wxStaticText* m_title = nullptr;
    wxStaticText* m_description = nullptr;
    bool m_expanded = false;
    std::function<void()> m_onLayoutChanged;

    void onHeaderClicked();
    void onToggleChanged();
    void applyColors(bool hovered);
};
