using System.Drawing;
using System.Drawing.Drawing2D;

namespace LandscapeFixer.App.Ui;

// A drop-down list painted by hand: the flat WinForms combo box keeps a white arrow button and a white frame
// when disabled, which is the one thing left that breaks the dark window.
sealed class DarkSelect : Control
{
    int _selected = -1;
    bool _hover;

    public List<string> Items { get; } = new();
    public event EventHandler? SelectedIndexChanged;

    public DarkSelect()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = Dpi.P(26);
        ForeColor = Theme.Text;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var next = value >= 0 && value < Items.Count ? value : -1;
            if (next == _selected) return;
            _selected = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SelectedItem => _selected >= 0 && _selected < Items.Count ? Items[_selected] : null;

    public void SetItems(IEnumerable<string> items)
    {
        Items.Clear();
        Items.AddRange(items);
        _selected = -1;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Back);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var fill = new SolidBrush(Enabled ? Theme.Field : Theme.Panel)) g.FillRectangle(fill, rect);
        using (var border = new Pen(!Enabled ? Theme.Grid : _hover || Focused ? Theme.Accent : Theme.Grid)) g.DrawRectangle(border, rect);

        var textColor = Enabled ? ForeColor : Theme.Dim;
        TextRenderer.DrawText(g, SelectedItem ?? "", Font, new Rectangle(Dpi.P(6), 0, Width - Dpi.P(28), Height), textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var cx = Width - Dpi.P(14);
        var cy = Height / 2f;
        using var arrow = new Pen(Enabled ? Theme.Accent : Theme.Dim, Dpi.P(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(arrow, new[] { new PointF(cx - Dpi.P(4), cy - Dpi.P(2)), new PointF(cx, cy + Dpi.P(2)), new PointF(cx + Dpi.P(4), cy - Dpi.P(2)) });
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left) Open();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Down && _selected < Items.Count - 1) SelectedIndex = _selected + 1;
        else if (e.KeyCode == Keys.Up && _selected > 0) SelectedIndex = _selected - 1;
        else if (e.KeyCode is Keys.Space or Keys.Enter && e.Alt == false) Open();
    }

    void Open()
    {
        if (Items.Count == 0) return;
        var menu = new ToolStripDropDown { Renderer = new DarkRenderer(), BackColor = Theme.Field, AutoSize = true, DropShadowEnabled = false };
        for (var i = 0; i < Items.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(Items[i]) { ForeColor = Theme.Text, BackColor = Theme.Field, Checked = false, AutoSize = false, Font = Font };
            item.Width = Math.Max(Width, TextRenderer.MeasureText(Items[i], Font).Width + Dpi.P(24));
            item.Height = Dpi.P(26);
            if (i == _selected) item.ForeColor = Theme.Accent;
            item.Click += (_, _) => SelectedIndex = index;
            menu.Items.Add(item);
        }
        menu.Show(this, new Point(0, Height));
    }

    sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Theme.Accent);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using var brush = new SolidBrush(e.Item.Selected ? Theme.HeaderHover : Theme.Field);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.ForeColor;
            base.OnRenderItemText(e);
        }
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Field;
        public override Color ImageMarginGradientBegin => Theme.Field;
        public override Color ImageMarginGradientMiddle => Theme.Field;
        public override Color ImageMarginGradientEnd => Theme.Field;
        public override Color MenuBorder => Theme.Accent;
        public override Color MenuItemBorder => Theme.HeaderHover;
        public override Color MenuItemSelected => Theme.HeaderHover;
    }
}
