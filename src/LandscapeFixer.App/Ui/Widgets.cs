using System.Drawing;

namespace LandscapeFixer.App.Ui;

// Small flat, dark controls (same look as TreeLODGen's), and the tooltip that carries the help texts.
static class Widgets
{
    public static TextBox Field() => new() { BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };

    public static Label Caption(string text) => new() { Text = text, ForeColor = Theme.Dim, AutoSize = true, BackColor = Color.Transparent };

    public static CheckBox Check(string text) => new DarkCheck { Text = text };

    public static RadioButton Radio(string text) => new DarkRadio { Text = text };

    public static DarkSelect Combo(params string[] items)
    {
        var select = new DarkSelect();
        select.Items.AddRange(items);
        return select;
    }

    public static Button Flat(string text, bool primary)
    {
        var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Theme.Grid;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(150, 236, 190) : Theme.HeaderHover;
        button.BackColor = primary ? Theme.Accent : Theme.Field;
        button.ForeColor = primary ? Theme.Back : Theme.Text;
        if (primary) button.Font = Dpi.Font("Segoe UI Semibold", 10.5f);
        return button;
    }

    /// <summary>One entry per line, for the blacklists and other pattern lists.</summary>
    public static TextBox Lines(IEnumerable<string> items)
    {
        var box = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Dpi.Font("Consolas", 9f),
            Text = string.Join("\r\n", items),
        };
        Win.DarkScroll(box);
        return box;
    }

    public static List<string> ReadLines(TextBox box) =>
        box.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public static void SetLines(TextBox box, IEnumerable<string> items) => box.Text = string.Join("\r\n", items);
}

/// <summary>A dark tooltip, so the help text doesn't flash a light box in the middle of a dark window.</summary>
sealed class HelpTip : ToolTip
{
    public HelpTip()
    {
        OwnerDraw = true;
        AutoPopDelay = 30000;
        InitialDelay = 250;
        ReshowDelay = 100;
        Popup += (_, e) =>
        {
            var text = GetToolTip(e.AssociatedControl);
            var size = TextRenderer.MeasureText(text, e.AssociatedControl!.Font, new Size(Dpi.P(360), 0), TextFormatFlags.WordBreak);
            e.ToolTipSize = new Size(size.Width + Dpi.P(20), size.Height + Dpi.P(16));
        };
        Draw += (_, e) =>
        {
            using var back = new SolidBrush(Theme.Header);
            e.Graphics.FillRectangle(back, e.Bounds);
            using var border = new Pen(Theme.Accent);
            e.Graphics.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, Rectangle.Inflate(e.Bounds, -Dpi.P(10), -Dpi.P(8)), Theme.Text,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
        };
    }
}
