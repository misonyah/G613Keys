using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace G613Keys;

/// <summary>
/// Light/dark palette that follows the Windows "app theme" setting, plus the
/// immersive dark-title-bar interop so the form's caption matches. WinForms does
/// not theme itself, so the form applies <see cref="Palette"/> to its controls.
/// </summary>
internal static class Theme
{
    internal readonly record struct Colors(
        Color Window, Color Surface, Color Text, Color SubtleText,
        Color Border, Color Accent, Color Error, Color FieldBack);

    private static readonly Colors Dark = new(
        Window: Color.FromArgb(32, 34, 37),
        Surface: Color.FromArgb(43, 45, 49),
        Text: Color.FromArgb(230, 232, 235),
        SubtleText: Color.FromArgb(150, 154, 160),
        Border: Color.FromArgb(60, 63, 68),
        Accent: Color.FromArgb(94, 170, 255),
        Error: Color.FromArgb(255, 120, 120),
        FieldBack: Color.FromArgb(24, 25, 28));

    private static readonly Colors Light = new(
        Window: Color.FromArgb(246, 247, 249),
        Surface: Color.White,
        Text: Color.FromArgb(28, 30, 33),
        SubtleText: Color.FromArgb(110, 114, 120),
        Border: Color.FromArgb(214, 217, 222),
        Accent: Color.FromArgb(0, 102, 204),
        Error: Color.FromArgb(197, 48, 48),
        FieldBack: Color.White);

    public static bool IsDark => ReadAppsUseLightTheme() == 0;

    public static Colors Palette => IsDark ? Dark : Light;

    private static int ReadAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v ? v : 1;
        }
        catch { return 1; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Match the form's title bar to the current theme (Windows 10 2004+).</summary>
    public static void ApplyTitleBar(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20; older builds used 19.
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }
}
