using DesktopDrawer.Helpers;
using DesktopDrawer.Models;
using System.IO;

namespace DesktopDrawer.Services;

public sealed class DesktopService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _debounceLock = new();
    private readonly System.Threading.Timer _debounceTimer;
    private readonly string[] _roots;
    private bool _disposed;

    public DesktopService()
    {
        string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        _roots = new[] { userDesktop, publicDesktop }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        PublicDesktopPath = publicDesktop;
        _debounceTimer = new System.Threading.Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        StartWatchers();
    }

    public event EventHandler? DesktopChanged;

    public string UserDesktopPath => _roots.FirstOrDefault() ??
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public string PublicDesktopPath { get; }

    public IReadOnlyList<string> Roots => _roots;

    public List<DesktopItem> Load(bool showHidden)
    {
        var items = new List<DesktopItem>();
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        };

        foreach (string root in _roots)
        {
            bool isPublic = string.Equals(root, PublicDesktopPath, StringComparison.OrdinalIgnoreCase);
            IEnumerable<FileSystemInfo> entries;

            try
            {
                entries = new DirectoryInfo(root).EnumerateFileSystemInfos("*", options);
                foreach (FileSystemInfo entry in entries)
                {
                    DesktopItem? item = CreateItem(entry, isPublic, showHidden);
                    if (item is not null)
                    {
                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"Could not enumerate {root}");
            }
        }

        return items;
    }

    private static DesktopItem? CreateItem(FileSystemInfo entry, bool isPublic, bool showHidden)
    {
        try
        {
            if (entry.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            bool hidden = (entry.Attributes & FileAttributes.Hidden) != 0 ||
                          (entry.Attributes & FileAttributes.System) != 0;

            if (hidden && !showHidden)
            {
                return null;
            }

            bool isFolder = (entry.Attributes & FileAttributes.Directory) != 0;
            long size = 0;

            if (!isFolder && entry is FileInfo fileInfo)
            {
                size = fileInfo.Length;
            }

            var item = new DesktopItem(entry.FullName, isPublic);
            string fallbackName = Path.GetFileName(entry.FullName);

            item.SetMetadata(
                name: fallbackName,
                displayName: ShellService.GetDisplayName(entry.FullName, fallbackName),
                typeName: isFolder ? "Folder" : ShellService.GetTypeName(entry.FullName),
                extension: isFolder ? string.Empty : entry.Extension,
                isFolder: isFolder,
                isHidden: hidden,
                modified: entry.LastWriteTime,
                size: size);

            return item;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Could not read desktop item {entry.FullName}");
            return null;
        }
    }

    private void StartWatchers()
    {
        foreach (string root in _roots)
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size |
                                   NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileSystemEvent;
                watcher.Deleted += OnFileSystemEvent;
                watcher.Changed += OnFileSystemEvent;
                watcher.Renamed += OnFileSystemEvent;
                watcher.Error += OnWatcherError;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"Could not watch {root}");
            }
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        lock (_debounceLock)
        {
            _debounceTimer.Change(700, Timeout.Infinite);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        AppLog.Write(e.GetException(), "Desktop watcher error");
        OnFileSystemEvent(sender, new FileSystemEventArgs(WatcherChangeTypes.All, string.Empty, string.Empty));
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            DesktopChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Desktop change handler failed");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _debounceTimer.Dispose();
    }
}
