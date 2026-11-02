using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopDrawer.Watchdog;

/// <summary>
/// Waits for the Desktop Drawer process to disappear and then puts the Windows
/// desktop icon visibility back to the state it had before Desktop Drawer started.
/// It is a separate executable on purpose: a forced kill of the main app - from
/// Task Manager or <c>taskkill</c> - leaves this process alive to clean up.
/// </summary>
internal static class Program
{
    private const string AdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const int ToggleDesktopIconsCommand = 0x7402;
    private const uint GwChild = 5;
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    private static int Main(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int processId))
        {
            return 1;
        }

        WaitForExit(processId);
        RestoreIconVisibility();
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

    private static void RestoreIconVisibility()
    {
        try
        {
            if (!AreIconsHidden())
            {
                return;
            }

            ToggleThroughExplorer();

            if (!AreIconsHidden())
            {
                Log("Watchdog restored desktop icons (visible).");
                return;
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AdvancedKeyPath, writable: true);
            key.SetValue("HideIcons", 0, RegistryValueKind.DWord);
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
            Log("Watchdog restored desktop icons through the registry (visible).");
        }
        catch (Exception ex)
        {
            Log($"Watchdog could not restore the desktop icons: {ex.Message}");
        }
    }

    private static void ToggleThroughExplorer()
    {
        try
        {
            IntPtr progman = FindWindow("Progman", "Program Manager");
            if (progman == IntPtr.Zero)
            {
                return;
            }

            IntPtr view = GetWindow(progman, GwChild);
            IntPtr target = view != IntPtr.Zero ? view : progman;
            SendMessage(target, 0x0111, (IntPtr)ToggleDesktopIconsCommand, IntPtr.Zero);
            Thread.Sleep(150);
        }
        catch (Exception ex)
        {
            Log($"Watchdog could not toggle the icons through Explorer: {ex.Message}");
        }
    }

    private static bool AreIconsHidden()
    {
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
