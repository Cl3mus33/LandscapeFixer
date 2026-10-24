using System.Drawing;

namespace LandscapeFixer.App.Ui;

// The window is laid out in pixels at 100 % display scaling and scaled here by hand, fonts included
// (in pixels, not points), instead of through WinForms' automatic scaling: one factor for everything,
// so the layout is the same at any scaling. LANDSCAPEFIXER_UI_SCALE overrides the factor, to check a
// layout without changing Windows' settings.
static class Dpi
{
    public static float Scale { get; private set; } = 1f;

    public static void Init()
    {
        if (float.TryParse(Environment.GetEnvironmentVariable("LANDSCAPEFIXER_UI_SCALE"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var forced) && forced > 0.5f)
        {
            Scale = forced;
            return;
        }

        using var g = Graphics.FromHwnd(IntPtr.Zero);
        Scale = Math.Max(1f, g.DpiX / 96f);
    }

    public static int P(int pixels) => (int)Math.Round(pixels * Scale);
    public static float P(float pixels) => pixels * Scale;
    public static Point P(int x, int y) => new(P(x), P(y));
    public static Size S(int width, int height) => new(P(width), P(height));

    // a size in points at 100 %, as WinForms fonts are usually given
    public static Font Font(string family, float points, FontStyle style = FontStyle.Regular) =>
        new(family, points * 96f / 72f * Scale, style, GraphicsUnit.Pixel);
}
