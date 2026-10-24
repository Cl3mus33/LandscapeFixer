using System.Drawing;
using System.Drawing.Drawing2D;

namespace LandscapeFixer.App.Ui;

// Check boxes and radio buttons painted by hand: the flat WinForms ones draw their tick in the text colour on a
// white box, which disappears on a dark window.
sealed class DarkCheck : CheckBox
{
    public DarkCheck()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        ForeColor = Theme.Text;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        return new Size(Dpi.P(26) + (Text.Length > 0 ? text.Width + Dpi.P(4) : 0), Math.Max(Dpi.P(20), text.Height + Dpi.P(4)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ToggleDrawing.Background(this, g);
        var box = new Rectangle(Dpi.P(2), (Height - Dpi.P(16)) / 2, Dpi.P(16), Dpi.P(16));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fill = new SolidBrush(Enabled ? Theme.Field : Theme.Panel)) g.FillRectangle(fill, box);
        using (var border = new Pen(!Enabled ? Theme.Grid : Checked || Focused || ClientRectangle.Contains(PointToClient(MousePosition)) ? Theme.Accent : Theme.Dim)) g.DrawRectangle(border, box);
        if (Checked)
        {
            using var tick = new Pen(Enabled ? Theme.Accent : Theme.Dim, Dpi.P(2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(tick, new[]
            {
                new PointF(box.Left + box.Width * 0.22f, box.Top + box.Height * 0.52f),
                new PointF(box.Left + box.Width * 0.43f, box.Top + box.Height * 0.74f),
                new PointF(box.Left + box.Width * 0.80f, box.Top + box.Height * 0.28f),
            });
        }
        ToggleDrawing.Label(this, g, Dpi.P(26));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}

sealed class DarkRadio : RadioButton
{
    public DarkRadio()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        ForeColor = Theme.Text;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        return new Size(Dpi.P(26) + text.Width + Dpi.P(4), Math.Max(Dpi.P(20), text.Height + Dpi.P(4)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ToggleDrawing.Background(this, g);
        var circle = new Rectangle(Dpi.P(2), (Height - Dpi.P(16)) / 2, Dpi.P(16), Dpi.P(16));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fill = new SolidBrush(Enabled ? Theme.Field : Theme.Panel)) g.FillEllipse(fill, circle);
        using (var border = new Pen(!Enabled ? Theme.Grid : Checked || Focused || ClientRectangle.Contains(PointToClient(MousePosition)) ? Theme.Accent : Theme.Dim)) g.DrawEllipse(border, circle);
        if (Checked)
        {
            using var dot = new SolidBrush(Enabled ? Theme.Accent : Theme.Dim);
            g.FillEllipse(dot, Rectangle.Inflate(circle, -Dpi.P(4), -Dpi.P(4)));
        }
        ToggleDrawing.Label(this, g, Dpi.P(26));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}

static class ToggleDrawing
{
    // what is behind a transparent control: the parent's own colour
    public static void Background(Control control, Graphics g)
    {
        var owner = control;
        while (owner.BackColor.A == 0 && owner.Parent != null) owner = owner.Parent;
        g.Clear(owner.BackColor.A == 0 ? Theme.Back : owner.BackColor);
    }

    public static void Label(Control control, Graphics g, int left)
    {
        var color = control.Enabled ? control.ForeColor : Theme.Dim;
        TextRenderer.DrawText(g, control.Text, control.Font, new Rectangle(left, 0, control.Width - left, control.Height), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}
