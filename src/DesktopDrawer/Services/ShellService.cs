using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;
using DesktopDrawer.Models;

namespace DesktopDrawer.Services;

public sealed class ShellService
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public void Open(DesktopItem item)
    {
        Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
    }

    public void Open(IEnumerable<DesktopItem> items)
    {
        foreach (DesktopItem item in items)
        {
            Open(item);
        }
    }

    public void OpenWith(DesktopItem item)
    {
        var startInfo = new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{item.FullPath}\"")
        {
            UseShellExecute = false
        };
        Process.Start(startInfo);
    }

    public void ShowInExplorer(DesktopItem item)
    {
        if (item.IsFolder)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.FullPath}\"") { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
    }

    public void ShowProperties(DesktopItem item)
    {
        Process.Start(new ProcessStartInfo(item.FullPath)
        {
            UseShellExecute = true,
            Verb = "properties"
        });
    }

    public void CopyPaths(IEnumerable<DesktopItem> items)
    {
        string text = string.Join(Environment.NewLine, items.Select(item => item.FullPath));
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    public void DeleteToRecycleBin(IEnumerable<DesktopItem> items)
    {
        var errors = new List<string>();

        foreach (DesktopItem item in items)
        {
            try
            {
                if (Directory.Exists(item.FullPath))
                {
                    FileSystem.DeleteDirectory(
                        item.FullPath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                }
                else if (File.Exists(item.FullPath))
                {
                    FileSystem.DeleteFile(
                        item.FullPath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"Could not recycle {item.FullPath}");
                errors.Add(item.DisplayName);
            }
        }

        if (errors.Count > 0)
        {
            throw new ShellOperationException(
                errors.Count == 1
                    ? $"Could not move \"{errors[0]}\" to the Recycle Bin."
                    : $"Could not move {errors.Count} items to the Recycle Bin.");
        }
    }

    public string GetRenameSeed(DesktopItem item)
    {
        return item.DisplayName;
    }

    public string Rename(DesktopItem item, string newDisplayName)
    {
        string trimmed = newDisplayName.Trim();
        if (string.Equals(trimmed, item.DisplayName, StringComparison.Ordinal))
        {
            return item.FullPath;
        }

        ValidateFileName(trimmed);

        string targetName = trimmed;
        bool extensionsHidden = AreFileExtensionsHidden();

        if (!item.IsFolder &&
            extensionsHidden &&
            !string.IsNullOrEmpty(Path.GetExtension(item.Name)) &&
            !string.Equals(item.DisplayName, item.Name, StringComparison.Ordinal))
        {
            string originalExtension = Path.GetExtension(item.Name);
            if (!targetName.EndsWith(originalExtension, StringComparison.OrdinalIgnoreCase))
            {
                targetName += originalExtension;
            }
        }

        string destination = Path.Combine(item.FolderPath, targetName);
        if (string.Equals(destination, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return item.FullPath;
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new ShellOperationException($"\"{targetName}\" already exists.");
        }

        if (item.IsFolder)
        {
            Directory.Move(item.FullPath, destination);
        }
        else
        {
            File.Move(item.FullPath, destination);
        }

        return destination;
    }

    public string CreateNewFolder(string parentDirectory)
    {
        string name = "New folder";
        string path = Path.Combine(parentDirectory, name);
        int suffix = 2;

        while (Directory.Exists(path))
        {
            name = $"New folder ({suffix++})";
            path = Path.Combine(parentDirectory, name);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetDisplayName(string path, string fallback)
    {
        try
        {
            var info = new NativeMethods.ShFileInfo();
            uint result = (uint)NativeMethods.SHGetFileInfo(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
                NativeMethods.ShgfiDisplayName | NativeMethods.ShgfiTypeName);

            if (result != 0)
            {
                string displayName = info.szDisplayName;
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Could not read display name for {path}");
        }

        return fallback;
    }

    public static string GetTypeName(string path)
    {
        try
        {
            var info = new NativeMethods.ShFileInfo();
            uint result = (uint)NativeMethods.SHGetFileInfo(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
                NativeMethods.ShgfiTypeName);

            if (result != 0 && !string.IsNullOrWhiteSpace(info.szTypeName))
            {
                return info.szTypeName;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Could not read type name for {path}");
        }

        return string.Empty;
    }

    private static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ShellOperationException("The name cannot be empty.");
        }

        if (name is "." or "..")
        {
            throw new ShellOperationException("That name is reserved.");
        }

        if (name.Length > 255)
        {
            throw new ShellOperationException("The name is too long.");
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ShellOperationException("The name contains a character that is not allowed.");
        }

        if (name.EndsWith(' ') || name.EndsWith('.'))
        {
            throw new ShellOperationException("A name cannot end with a space or a period.");
        }

        string baseName = name.Split('.')[0];
        if (ReservedNames.Contains(baseName))
        {
            throw new ShellOperationException("That name is reserved by Windows.");
        }
    }

    private static bool AreFileExtensionsHidden()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            object? value = key?.GetValue("HideFileExt");
            return value is null || Convert.ToInt32(value) != 0;
        }
        catch
        {
            return true;
        }
    }
}

public sealed class ShellOperationException : Exception
{
    public ShellOperationException(string message)
        : base(message)
    {
    }
}
