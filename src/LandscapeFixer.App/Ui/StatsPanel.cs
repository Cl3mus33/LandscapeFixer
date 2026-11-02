using System.Drawing;

namespace LandscapeFixer.App.Ui;

// The right-hand panel: what the last run changed, as a grid of figures (or a short hint while there is none).
sealed class StatsPanel : Panel
{
    public sealed record Tile(string Label, string Value, bool Warn = false);

    const int Columns = 4;

    List<Tile> _tiles = new();
    string _hint = "";

    public StatsPanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
    }

    public void Show(IEnumerable<Tile> tiles, string hint = "")
    {
        _tiles = tiles.ToList();
        _hint = hint;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_tiles.Count == 0)
        {
            TextRenderer.DrawText(g, _hint, Font, new Rectangle(Dpi.P(20), 0, Width - Dpi.P(40), Height), Theme.Dim, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var tileWidth = (Width - Dpi.P(24)) / Columns;
        var rows = (_tiles.Count + Columns - 1) / Columns;
        var top = Math.Max(Dpi.P(14), (Height - rows * Dpi.P(66)) / 2);
        using var valueFont = Dpi.Font("Segoe UI Semibold", 15f);
        using var labelFont = Dpi.Font("Segoe UI", 8f);
        for (var i = 0; i < _tiles.Count; i++)
        {
            var column = i % Columns;
            var row = i / Columns;
            var x = Dpi.P(12) + column * tileWidth;
            var y = top + row * Dpi.P(66);
            TextRenderer.DrawText(g, _tiles[i].Value, valueFont, new Rectangle(x, y, tileWidth, Dpi.P(28)), _tiles[i].Warn ? Theme.Orange : Theme.Accent, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _tiles[i].Label, labelFont, new Rectangle(x, y + Dpi.P(30), tileWidth - Dpi.P(4), Dpi.P(30)), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }
}
