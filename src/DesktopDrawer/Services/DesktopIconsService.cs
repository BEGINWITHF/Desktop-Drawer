using Microsoft.Win32;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;

namespace DesktopDrawer.Services;

public sealed class DesktopIconsService
{
    private const string AdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const int ToggleDesktopIconsCommand = 0x7402;
    private const int ToggleAttempts = 3;

    /// <summary>
    /// Reads the live state of the desktop icons. The registry value alone can be
    /// out of sync (for example after the app was killed mid-toggle), while the
    /// desktop icon list window is what actually decides whether icons are drawn.
    /// </summary>
    public bool AreHidden()
    {
        IntPtr list = FindIconList();
        if (list != IntPtr.Zero)
        {
            return !NativeMethods.IsWindowVisible(list);
        }

        return ReadHiddenFromRegistry();
    }

    public bool SetHidden(bool hidden)
    {
        try
        {
            for (int attempt = 0; attempt < ToggleAttempts; attempt++)
            {
                if (AreHidden() == hidden)
                {
                    SyncRegistry(hidden);
                    return true;
                }

                if (!ToggleThroughExplorer())
                {
                    break;
                }
            }

            if (AreHidden() == hidden)
            {
                SyncRegistry(hidden);
                return true;
            }

            AppLog.Write($"Explorer did not apply the desktop icon change; storing HideIcons={(hidden ? 1 : 0)}.");
            ForceRegistry(hidden);
            return AreHidden() == hidden;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not change desktop icon visibility");
            throw new InvalidOperationException("Windows did not allow the desktop icon setting to change.", ex);
        }
    }

    private static IntPtr FindIconList()
    {
        IntPtr view = FindIconView();
        return view == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }

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

    private static bool ToggleThroughExplorer()
    {
        IntPtr view = FindIconView();
        if (view == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            NativeMethods.SendMessage(view, NativeMethods.WmCommand, (IntPtr)ToggleDesktopIconsCommand, IntPtr.Zero);
            Thread.Sleep(150);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not toggle desktop icons through Explorer");
            return false;
        }
    }

    private static bool ReadHiddenFromRegistry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AdvancedKeyPath);
            object? value = key?.GetValue("HideIcons");
            return value is not null && Convert.ToInt32(value) != 0;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not read desktop icon visibility");
            return false;
        }
    }

    private static void SyncRegistry(bool hidden)
    {
        if (ReadHiddenFromRegistry() != hidden)
        {
            ForceRegistry(hidden);
        }
    }

    private static void ForceRegistry(bool hidden)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(AdvancedKeyPath, writable: true);
        key.SetValue("HideIcons", hidden ? 1 : 0, RegistryValueKind.DWord);
        NativeMethods.SHChangeNotify(
            NativeMethods.ShcneAssocChanged,
            NativeMethods.ShcnfIdList,
            IntPtr.Zero,
            IntPtr.Zero);
    }
}
