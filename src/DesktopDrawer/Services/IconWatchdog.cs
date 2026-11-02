using System.Diagnostics;
using System.IO;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Starts a background process that outlives Desktop Drawer. When the app
/// disappears - including a forced kill from Task Manager - the watchdog makes
/// the Windows desktop icons visible again.
/// </summary>
public static class IconWatchdog
{
    private const string SwitchName = "--restore-desktop-icons";
    private const string WatchdogFileName = "DesktopDrawer.Watchdog.exe";

    private static Process? _watchdog;

    public static void Start()
    {
        try
        {
            string? appExecutable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(appExecutable) ||
                string.Equals(Path.GetFileNameWithoutExtension(appExecutable), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Write("Icon watchdog skipped because the app is not running from its own executable.");
                return;
            }

            string standalone = Path.Combine(AppContext.BaseDirectory, WatchdogFileName);
            bool useStandalone = File.Exists(standalone);
            string executable = useStandalone ? standalone : appExecutable;

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            };

            if (!useStandalone)
            {
                startInfo.ArgumentList.Add(SwitchName);
            }

            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

            Process? watchdog = Process.Start(startInfo);
            _watchdog = watchdog;
            AppLog.Write(watchdog is null
                ? "Could not start the desktop icon watchdog."
                : $"Desktop icon watchdog started (pid {watchdog.Id}).");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not start the desktop icon watchdog");
        }
    }

    /// <summary>
    /// Restarts the watchdog if it disappeared while the app is still running.
    /// </summary>
    public static void EnsureRunning()
    {
        try
        {
            if (_watchdog is { HasExited: false })
            {
                return;
            }
        }
        catch
        {
            // Fall through and start a fresh watchdog.
        }

        Start();
    }

    /// <summary>
    /// Fallback mode for builds without the standalone watchdog executable.
    /// Returns true when the arguments asked for it, in which case the caller
    /// must exit without creating any window.
    /// </summary>
    public static bool TryRun(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], SwitchName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(args[1], out int processId))
        {
            return true;
        }

        try
        {
            using Process target = Process.GetProcessById(processId);
            target.WaitForExit();
        }
        catch (ArgumentException)
        {
            // The main process was already gone.
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "The icon watchdog could not watch the app process");
        }

        try
        {
            var icons = new DesktopIconsService();
            if (icons.AreHidden())
            {
                icons.SetHidden(false);
                AppLog.Write("Watchdog restored desktop icons (visible)");
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "The icon watchdog could not restore the desktop icons");
        }

        return true;
    }
}
