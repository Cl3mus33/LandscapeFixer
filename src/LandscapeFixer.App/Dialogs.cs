using System.Drawing;
using LandscapeFixer.App.Ui;

namespace LandscapeFixer.App;

// Language, the only app-wide preference left (the window is dark, like TreeLODGen's).
sealed class PreferencesForm : Form
{
    readonly DarkSelect _language;
    readonly List<Loc.Language> _languages = Loc.Available();

    public string SelectedLanguage => _languages[Math.Max(0, _language.SelectedIndex)].Code;

    public PreferencesForm()
    {
        Text = Loc.T("launcher.preferences.title", "Preferences");
        ClientSize = Dpi.S(340, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Dpi.Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;

        var caption = Widgets.Caption(Loc.T("launcher.language.label", "Language"));
        caption.Font = Dpi.Font("Segoe UI Semibold", 9f);
        caption.ForeColor = Theme.Accent;
        caption.Location = Dpi.P(20, 18);
        Controls.Add(caption);

        _language = Widgets.Combo(_languages.Select(l => l.Name).ToArray());
        _language.Location = Dpi.P(20, 44);
        _language.Width = Dpi.P(300);
        _language.SelectedIndex = Math.Max(0, _languages.FindIndex(l => l.Code == Loc.Code));
        Controls.Add(_language);

        var ok = Widgets.Flat(Loc.T("common.ok", "OK"), true);
        ok.Location = Dpi.P(130, 96);
        ok.Size = Dpi.S(90, 34);
        ok.DialogResult = DialogResult.OK;
        var cancel = Widgets.Flat(Loc.T("common.cancel", "Cancel"), false);
        cancel.Location = Dpi.P(230, 96);
        cancel.Size = Dpi.S(90, 34);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.AddRange(new Control[] { ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win.DarkTitleBar(Handle);
    }
}

// A list of one entry per line in a window of its own (the auto-generate allowlist is too long for a card).
sealed class TextListForm : Form
{
    readonly TextBox _box;

    public List<string> Lines => Widgets.ReadLines(_box);

    public TextListForm(string title, IEnumerable<string> lines)
    {
        Text = title;
        ClientSize = Dpi.S(640, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Dpi.Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.None;

        _box = Widgets.Lines(lines);
        _box.Location = Dpi.P(16, 16);
        _box.Size = Dpi.S(608, 440);
        Controls.Add(_box);

        var ok = Widgets.Flat(Loc.T("common.ok", "OK"), true);
        ok.Location = Dpi.P(434, 470);
        ok.Size = Dpi.S(90, 34);
        ok.DialogResult = DialogResult.OK;
        var cancel = Widgets.Flat(Loc.T("common.cancel", "Cancel"), false);
        cancel.Location = Dpi.P(534, 470);
        cancel.Size = Dpi.S(90, 34);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.AddRange(new Control[] { ok, cancel });
        CancelButton = cancel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win.DarkTitleBar(Handle);
    }
}
