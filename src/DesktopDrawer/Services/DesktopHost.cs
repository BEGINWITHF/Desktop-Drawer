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

    /// <summary>
    /// Keeps a top-level window right above the desktop so "show desktop" does not
    /// hide it behind Progman, while still letting normal windows cover it.
    /// </summary>
    public static void RaiseAboveDesktop(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }

        IntPtr desktop = FindDesktopAnchor();
        if (desktop == IntPtr.Zero)
        {
            return;
        }

        // Touchpad and task-view gestures can put the overlay into the topmost
        // band. Remove that flag before inserting it back into the normal band.
        NativeMethods.SetWindowPos(
            window,
            NativeMethods.HwndNotTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpNoOwnerZOrder);

        // SetWindowPos inserts the window *below* hWndInsertAfter. Walk upward
        // from the desktop and use the first window that accepts the insertion:
        // shell/IME windows often reject being used as an insertion anchor.
        IntPtr candidate = NativeMethods.GetWindow(desktop, NativeMethods.GwHwndPrev);

        for (int i = 0; i < 64 && candidate != IntPtr.Zero; i++)
        {
            if (candidate != window)
            {
                bool moved = NativeMethods.SetWindowPos(
                    window,
                    candidate,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove |
                    NativeMethods.SwpNoSize |
                    NativeMethods.SwpNoActivate |
                    NativeMethods.SwpNoOwnerZOrder);

                if (moved)
                {
                    return;
                }
            }

            candidate = NativeMethods.GetWindow(candidate, NativeMethods.GwHwndPrev);
        }
    }

    private static IntPtr FindDesktopAnchor()
    {
        IntPtr withIconView = IntPtr.Zero;
        IntPtr fallback = IntPtr.Zero;

        NativeMethods.EnumWindows(
            (window, _) =>
            {
                string? className = GetClassName(window);
                if (className is not ("Progman" or "WorkerW"))
                {
                    return true;
                }

                if (NativeMethods.FindWindowEx(
                        window,
                        IntPtr.Zero,
                        "SHELLDLL_DefView",
                        null) != IntPtr.Zero)
                {
                    withIconView = window;
                    return false;
                }

                if (className == "Progman")
                {
                    fallback = window;
                }

                return true;
            },
            IntPtr.Zero);

        return withIconView != IntPtr.Zero ? withIconView : fallback;
    }

    public static bool IsDesktopWindowAt(int x, int y)
    {
        var point = new NativeMethods.Point { X = x, Y = y };
        IntPtr window = NativeMethods.WindowFromPoint(point);
        if (window == IntPtr.Zero)
        {
            return false;
        }

        string? className = GetClassName(window);
        return className is "Progman" or "SHELLDLL_DefView" or "SysListView32" or "WorkerW";
    }

    private static string? GetClassName(IntPtr window)
    {
        var buffer = new System.Text.StringBuilder(128);
        return NativeMethods.GetClassName(window, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : null;
    }

    private static IntPtr FindIconView()
    {
        IntPtr progman = FindDesktopAnchor();

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
