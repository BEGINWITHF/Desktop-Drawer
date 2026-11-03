using System.Runtime.InteropServices;

namespace DesktopDrawer.Helpers;

/// <summary>
/// Asks Windows to draw its classic Win32 menus (including shell context menus)
/// with the dark theme, so the native menu does not clash with the app.
/// These are the undocumented uxtheme entry points used by dark-mode apps.
/// </summary>
internal static class DarkMode
{
    private const int AllowDark = 2;

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
    private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    private static extern void FlushMenuThemes();

    public static void Enable(IntPtr window)
    {
        try
        {
            SetPreferredAppMode(AllowDark);

            if (window != IntPtr.Zero)
            {
                AllowDarkModeForWindow(window, true);
            }

            FlushMenuThemes();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not enable dark menus");
        }
    }

    public static void AllowWindow(IntPtr window)
    {
        try
        {
            if (window != IntPtr.Zero)
            {
                AllowDarkModeForWindow(window, true);
            }
        }
        catch
        {
            // Older Windows builds do not export the entry point.
        }
    }
}
