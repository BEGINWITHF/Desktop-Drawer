using System.IO;
using Microsoft.Win32;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopDrawer";

    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not read startup setting");
            return false;
        }
    }

    public bool EnsureRegistered()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is not string current || string.IsNullOrWhiteSpace(current))
            {
                return true;
            }

            string? executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return true;
            }

            if (string.Equals(current.Trim('"'), executable, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return SetEnabled(true);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not verify the startup path");
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            string? executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) ||
                string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Write("Startup was not enabled because the app is not running from its own executable.");
                return false;
            }

            key.SetValue(ValueName, $"\"{executable}\"");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not update startup setting");
            return false;
        }
    }
}
