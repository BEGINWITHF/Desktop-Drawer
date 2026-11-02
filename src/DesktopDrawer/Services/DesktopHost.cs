using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;

namespace DesktopDrawer.Services;

/// <summary>
/// Reparents the main window into the Windows desktop icon view so it behaves
/// like part of the wallpaper instead of a normal application window: no
/// taskbar entry, no Alt+Tab entry, and it survives "show desktop".
/// </summary>
public static class DesktopHost
{
    public static bool TryEmbed(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr host = FindIconView();
        if (host == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            long style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlStyle).ToInt64();
            style &= ~(NativeMethods.WsPopup |
                       NativeMethods.WsCaption |
                       NativeMethods.WsThickFrame |
                       NativeMethods.WsMinimizeBox |
                       NativeMethods.WsMaximizeBox |
                       NativeMethods.WsSysMenu);
            style |= NativeMethods.WsChild | NativeMethods.WsVisible;
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlStyle, new IntPtr(style));

            long exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            exStyle = (exStyle | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(exStyle));

            NativeMethods.SetParent(handle, host);

            double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            int x = (int)Math.Round(window.Left * scale);
            int y = (int)Math.Round(window.Top * scale);
            int width = (int)Math.Round(window.Width * scale);
            int height = (int)Math.Round(window.Height * scale);

            NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                x,
                y,
                width,
                height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

            AppLog.Write($"Embedded in the desktop (host 0x{host:X}, dpi {NativeMethods.GetDpiForWindow(handle)}, {width}x{height} at {x},{y}).");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not embed the window in the desktop");
            return false;
        }
    }

    public static bool IsAlive(IntPtr host) => host != IntPtr.Zero && NativeMethods.IsWindow(host);

    private static IntPtr FindIconView()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);

        if (progman != IntPtr.Zero)
        {
            IntPtr view = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero)
            {
                return view;
            }
        }

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows(
            (top, _) =>
            {
                IntPtr candidate = NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (candidate == IntPtr.Zero)
                {
                    return true;
                }

                found = candidate;
                return false;
            },
            IntPtr.Zero);

        return found;
    }
}
