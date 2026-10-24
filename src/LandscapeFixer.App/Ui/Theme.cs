using System.Drawing;
using System.Runtime.InteropServices;

namespace LandscapeFixer.App.Ui;

// The palette shared with TreeLODGen: dark blue-grey, one mint-green accent, one orange for warnings.
static class Theme
{
    public static readonly Color Back = Color.FromArgb(24, 27, 33);
    public static readonly Color Panel = Color.FromArgb(18, 20, 25);
    public static readonly Color Field = Color.FromArgb(34, 38, 47);
    public static readonly Color Header = Color.FromArgb(30, 34, 42);
    public static readonly Color HeaderHover = Color.FromArgb(38, 43, 53);
    public static readonly Color Grid = Color.FromArgb(32, 36, 44);
    public static readonly Color Text = Color.FromArgb(222, 227, 235);
    public static readonly Color Dim = Color.FromArgb(138, 148, 163);
    public static readonly Color Accent = Color.FromArgb(120, 226, 170);
    public static readonly Color Orange = Color.FromArgb(255, 170, 60);
}

static class Win
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr window, string appName, string? idList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, char[] name, ref uint size);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    /// <summary>Dark title bar, to match the rest of the window.</summary>
    public static void DarkTitleBar(IntPtr handle)
    {
        var dark = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    /// <summary>Dark scroll bars and edges on a control that scrolls (list boxes, text boxes, scroll panels).</summary>
    public static void DarkScroll(Control control)
    {
        void Apply() => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        if (control.IsHandleCreated) Apply();
        else control.HandleCreated += (_, _) => Apply();
    }

    /// <summary>The path of the running exe as the kernel knows it. Launched from Mod Organizer 2, the module
    /// path is the virtual one under the game's Data folder, while this is the real &lt;instance&gt;\mods\&lt;mod&gt; one.</summary>
    public static string? RealExePath()
    {
        try
        {
            var buffer = new char[4096];
            var size = (uint)buffer.Length;
            return QueryFullProcessImageNameW(GetCurrentProcess(), 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
