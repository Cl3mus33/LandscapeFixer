using System.Drawing;

namespace LandscapeFixer.App.Ui;

// A collapsible section: a clickable header (chevron, optional on/off box, title, short description) and a
// body that holds the controls. Cards with an on/off box unfold when it is ticked and fold when it isn't.
sealed class Card : Panel
{
    readonly Panel _header = new();
    readonly Label _chevron = new(), _title = new(), _description = new();
    int _bodyHeight;
    bool _expanded;

    public Panel Body { get; } = new();
    public CheckBox? Toggle { get; }
    public event Action? LayoutChanged;

    public Card(string title, string description, bool hasToggle, bool isOn, bool expanded)
    {
        BackColor = Theme.Back;
        _header.Height = Dpi.P(44);
        _header.Dock = DockStyle.Top;
        _header.BackColor = Theme.Header;
        _header.Cursor = Cursors.Hand;

        _chevron.AutoSize = true;
        _chevron.Font = Dpi.Font("Segoe UI Symbol", 9f);
        _chevron.ForeColor = Theme.Dim;
        _chevron.Location = Dpi.P(14, 13);

        var x = 40;
        if (hasToggle)
        {
            Toggle = new DarkCheck { Checked = isOn, BackColor = Theme.Header, Location = Dpi.P(38, 12), Text = "" };
            Toggle.CheckedChanged += (_, _) => ToggleChanged();
            _header.Controls.Add(Toggle);
            x = 68;
        }

        _title.AutoSize = true;
        _title.Text = title;
        _title.Font = Dpi.Font("Segoe UI Semibold", 11f);
        _title.ForeColor = Theme.Text;
        _title.Location = Dpi.P(x, 10);

        _description.AutoSize = true;
        _description.Text = description;
        _description.ForeColor = Theme.Dim;

        _header.Controls.AddRange(new Control[] { _chevron, _title, _description });
        foreach (var part in new Control[] { _header, _chevron, _title, _description })
        {
            part.Cursor = Cursors.Hand;
            part.Click += (_, _) => Expanded = !Expanded;
            part.MouseEnter += (_, _) => Highlight(true);
            part.MouseLeave += (_, _) => Highlight(false);
        }

        Body.BackColor = Theme.Back;
        Body.Dock = DockStyle.Fill;
        Controls.Add(Body);
        Controls.Add(_header);

        _expanded = expanded;
        Body.Enabled = !hasToggle || isOn;
        Apply();
        _title.TextChanged += (_, _) => PlaceDescription();
        PlaceDescription();
    }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            Apply();
            LayoutChanged?.Invoke();
        }
    }

    /// <summary>The height of the body's content, in pixels already scaled.</summary>
    public int BodyHeight
    {
        get => _bodyHeight;
        set
        {
            _bodyHeight = value;
            Apply();
        }
    }

    public void SetDescription(string text)
    {
        _description.Text = text;
        PlaceDescription();
    }

    void ToggleChanged()
    {
        Body.Enabled = Toggle!.Checked;
        Expanded = Toggle.Checked;
    }

    void Apply()
    {
        _chevron.Text = _expanded ? "▾" : "▸";
        Body.Visible = _expanded;
        Height = _header.Height + (_expanded ? _bodyHeight : 0);
    }

    void PlaceDescription() => _description.Location = new Point(_title.Right + Dpi.P(14), _title.Top + Dpi.P(3));

    void Highlight(bool hover)
    {
        var color = hover ? Theme.HeaderHover : Theme.Header;
        _header.BackColor = color;
        _chevron.BackColor = color;
        _title.BackColor = color;
        _description.BackColor = color;
        if (Toggle != null) Toggle.BackColor = color;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Grid);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Invalidate();
    }
}

// Stacks controls top to bottom inside a card body, in 100 %-scale pixels.
sealed class Stack
{
    readonly Control _host;
    readonly HelpTip _tip;
    int _y;

    public int Width { get; }

    public Stack(Control host, int width, HelpTip tip, int top = 12)
    {
        _host = host;
        Width = width;
        _tip = tip;
        _y = top;
    }

    /// <summary>The height the body needs, scaled.</summary>
    public int Height => Dpi.P(_y + 12);

    public void Gap(int pixels) => _y += pixels;

    // A dim caption; the help text, when there is one, is its tooltip and the caption gets a small "i".
    public Label Caption(string text, string? help = null)
    {
        var label = Widgets.Caption(help == null ? text : text + "  ⓘ");
        label.Font = Dpi.Font("Segoe UI Semibold", 9f);
        label.ForeColor = Theme.Accent;
        label.Location = Dpi.P(14, _y);
        _host.Controls.Add(label);
        if (help != null) _tip.SetToolTip(label, help);
        _y += 22;
        return label;
    }

    public T Add<T>(T control, int height, int width = 0, int x = 14) where T : Control
    {
        control.Location = Dpi.P(x, _y);
        if (width > 0) control.Width = Dpi.P(width);
        if (height > 0) control.Height = Dpi.P(height);
        _host.Controls.Add(control);
        _y += height + 8;
        return control;
    }

    public (TextBox, Button) PathRow(string browseTitle, Action<string>? changed = null)
    {
        var box = Widgets.Field();
        var button = Widgets.Flat("...", false);
        var y = _y;
        box.Location = Dpi.P(14, y);
        box.Width = Dpi.P(Width - 14 - 14 - 48);
        button.Location = Dpi.P(Width - 14 - 42, y - 1);
        button.Size = Dpi.S(42, 25);
        _host.Controls.Add(box);
        _host.Controls.Add(button);
        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = browseTitle, UseDescriptionForTitle = true };
            if (Directory.Exists(box.Text)) dialog.SelectedPath = box.Text;
            if (dialog.ShowDialog(_host.FindForm()) != DialogResult.OK) return;
            box.Text = dialog.SelectedPath;
            changed?.Invoke(dialog.SelectedPath);
        };
        _y += 34;
        return (box, button);
    }

    public Panel Group(int height)
    {
        var panel = new Panel { BackColor = Color.Transparent };
        return Add(panel, height, Width - 28);
    }
}
