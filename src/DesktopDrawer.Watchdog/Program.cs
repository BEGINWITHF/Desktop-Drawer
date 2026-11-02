using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopDrawer.Watchdog;

/// <summary>
/// Waits for the Desktop Drawer process to disappear and then makes the Windows
/// desktop icons visible again. It is a separate executable on purpose: a forced
/// kill of the main app - from Task Manager or <c>taskkill</c> - leaves this
/// process alive to clean up.
/// </summary>
internal static class Program
{
    private const string AdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const int ToggleDesktopIconsCommand = 0x7402;
    private const int ToggleAttempts = 4;
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    private static int Main(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int processId))
        {
            return 1;
        }

        WaitForExit(processId);

        // Give Explorer a moment to finish tearing down the app's desktop window.
        Thread.Sleep(300);
        ShowDesktopIcons();
        return 0;
    }

    private static void WaitForExit(int processId)
    {
        try
        {
            using Process target = Process.GetProcessById(processId);
            target.WaitForExit();
        }
        catch (ArgumentException)
        {
            // The app process was already gone.
        }
        catch (Exception ex)
        {
            Log($"Watchdog could not wait for process {processId}: {ex.Message}");
        }
    }

    private static void ShowDesktopIcons()
    {
        try
        {
            for (int attempt = 0; attempt < ToggleAttempts; attempt++)
            {
                if (!AreIconsHidden())
                {
                    Log("Watchdog confirmed the desktop icons are visible.");
                    return;
                }

                if (!ToggleThroughExplorer())
                {
                    break;
                }
            }

            if (!AreIconsHidden())
            {
                Log("Watchdog restored desktop icons (visible).");
                return;
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AdvancedKeyPath, writable: true);
            key.SetValue("HideIcons", 0, RegistryValueKind.DWord);
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
            Log("Watchdog restored the HideIcons registry value; Explorer will show the icons after a refresh.");
        }
        catch (Exception ex)
        {
            Log($"Watchdog could not restore the desktop icons: {ex.Message}");
        }
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
            SendMessage(view, 0x0111, (IntPtr)ToggleDesktopIconsCommand, IntPtr.Zero);
            Thread.Sleep(200);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Watchdog could not toggle the icons through Explorer: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The registry can disagree with what the desktop actually draws, so the
    /// desktop icon list window is the source of truth when it exists.
    /// </summary>
    private static bool AreIconsHidden()
    {
        IntPtr list = FindIconList();
        if (list != IntPtr.Zero)
        {
            return !IsWindowVisible(list);
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AdvancedKeyPath);
            object? value = key?.GetValue("HideIcons");
            return value is not null && Convert.ToInt32(value) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr FindIconList()
    {
        IntPtr view = FindIconView();
        return view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }

    private static IntPtr FindIconView()
    {
        IntPtr progman = FindWindow("Progman", "Program Manager");
        if (progman != IntPtr.Zero)
        {
            IntPtr view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero)
            {
                return view;
            }
        }

        IntPtr found = IntPtr.Zero;
        EnumWindows(
            (top, _) =>
            {
                IntPtr candidate = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
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

    private static void Log(string message)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DesktopDrawer");
            Directory.CreateDirectory(directory);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, "desktop-drawer.log"), line);
        }
        catch
        {
            // Logging must never take the watchdog down.
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
