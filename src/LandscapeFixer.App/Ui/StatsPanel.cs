using System.Drawing;
using System.Drawing.Drawing2D;

namespace LandscapeFixer.App.Ui;

// The right-hand panel: a slowly turning wireframe patch of terrain (the way a 3D editor shows an object's
// edges, as in TreeLODGen), and under it what the last run changed.
sealed class StatsPanel : Panel
{
    public sealed record Tile(string Label, string Value, bool Warn = false);

    const int Grid = 22;
    readonly float[,] _height = new float[Grid, Grid];
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    float _angle = 0.6f;
    bool _busy;
    List<Tile> _tiles = new();
    string _hint = "";

    public StatsPanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        // a fixed, rolling landscape: a few overlapping waves and one ridge
        for (var x = 0; x < Grid; x++)
        {
            for (var z = 0; z < Grid; z++)
            {
                var fx = x / (float)(Grid - 1) * 2f - 1f;
                var fz = z / (float)(Grid - 1) * 2f - 1f;
                var ridge = MathF.Exp(-((fx - 0.25f) * (fx - 0.25f) * 3f + (fz + 0.1f) * (fz + 0.1f) * 9f)) * 0.9f;
                _height[x, z] = 0.22f * MathF.Sin(fx * 3.1f) * MathF.Cos(fz * 2.3f) + 0.12f * MathF.Sin(fx * 7f + fz * 5f) + ridge - 0.25f;
            }
        }
        _timer.Tick += (_, _) => { _angle += _busy ? 0.022f : 0.008f; if (Visible) Invalidate(); };
        _timer.Start();
    }

    public bool Busy { set => _busy = value; }

    public void Show(IEnumerable<Tile> tiles, string hint = "")
    {
        _tiles = tiles.ToList();
        _hint = hint;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tilesTop = _tiles.Count > 0 || _hint != "" ? Height - Dpi.P(150) : Height;
        DrawGrid(g, tilesTop);
        DrawTerrain(g, tilesTop);
        DrawTiles(g, tilesTop);
    }

    void DrawGrid(Graphics g, int bottom)
    {
        using var pen = new Pen(Theme.Grid);
        var step = Dpi.P(40);
        for (var x = 0; x < Width; x += step) g.DrawLine(pen, x, 0, x, bottom);
        for (var y = 0; y < bottom; y += step) g.DrawLine(pen, 0, y, Width, y);
    }

    void DrawTerrain(Graphics g, int bottom)
    {
        var cos = MathF.Cos(_angle);
        var sin = MathF.Sin(_angle);
        var scale = Math.Min(Width, bottom) * 0.42f;
        var cx = Width / 2f;
        var cy = bottom * 0.58f;
        var points = new PointF[Grid, Grid];
        var depth = new float[Grid, Grid];
        for (var x = 0; x < Grid; x++)
        {
            for (var z = 0; z < Grid; z++)
            {
                var px = (x / (float)(Grid - 1) * 2f - 1f);
                var pz = (z / (float)(Grid - 1) * 2f - 1f);
                var rx = px * cos - pz * sin;
                var rz = px * sin + pz * cos;
                points[x, z] = new PointF(cx + rx * scale * 1.05f, cy + rz * scale * 0.38f - _height[x, z] * scale * 0.75f);
                depth[x, z] = rz;
            }
        }

        for (var x = 0; x < Grid; x++)
        {
            for (var z = 0; z < Grid; z++)
            {
                if (x + 1 < Grid) Line(g, points[x, z], points[x + 1, z], (depth[x, z] + depth[x + 1, z]) / 2f, _height[x, z]);
                if (z + 1 < Grid) Line(g, points[x, z], points[x, z + 1], (depth[x, z] + depth[x, z + 1]) / 2f, _height[x, z]);
            }
        }
    }

    // nearer edges brighter, higher ground whiter (snow on the ridge), the rest in the accent colour
    static void Line(Graphics g, PointF a, PointF b, float depth, float height)
    {
        var alpha = (int)Math.Clamp(150 - depth * 90, 40, 220);
        var snow = Math.Clamp((height - 0.35f) * 3f, 0f, 1f);
        var color = Color.FromArgb(alpha,
            (int)(Theme.Accent.R + (235 - Theme.Accent.R) * snow),
            (int)(Theme.Accent.G + (245 - Theme.Accent.G) * snow),
            (int)(Theme.Accent.B + (250 - Theme.Accent.B) * snow));
        using var pen = new Pen(color, Dpi.P(1f));
        g.DrawLine(pen, a, b);
    }

    void DrawTiles(Graphics g, int top)
    {
        if (_tiles.Count == 0 && _hint == "") return;
        using var line = new Pen(Theme.Grid);
        g.DrawLine(line, 0, top, Width, top);

        if (_tiles.Count == 0)
        {
            TextRenderer.DrawText(g, _hint, Font, new Rectangle(Dpi.P(20), top + Dpi.P(20), Width - Dpi.P(40), Dpi.P(110)), Theme.Dim, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var columns = 4;
        var tileWidth = (Width - Dpi.P(24)) / columns;
        using var valueFont = Dpi.Font("Segoe UI Semibold", 15f);
        using var labelFont = Dpi.Font("Segoe UI", 8f);
        for (var i = 0; i < _tiles.Count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var x = Dpi.P(12) + column * tileWidth;
            var y = top + Dpi.P(14) + row * Dpi.P(66);
            TextRenderer.DrawText(g, _tiles[i].Value, valueFont, new Rectangle(x, y, tileWidth, Dpi.P(28)), _tiles[i].Warn ? Theme.Orange : Theme.Accent, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _tiles[i].Label, labelFont, new Rectangle(x, y + Dpi.P(30), tileWidth - Dpi.P(4), Dpi.P(30)), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
