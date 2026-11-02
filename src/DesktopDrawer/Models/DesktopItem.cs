using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DesktopDrawer.Models;

public sealed class DesktopItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _displayName = string.Empty;
    private string _typeName = string.Empty;
    private string _extension = string.Empty;
    private DateTime _modified;
    private long _size;
    private bool _isFolder;
    private bool _isHidden;
    private ImageSource? _icon;
    private bool _isRenaming;
    private string _editName = string.Empty;

    public DesktopItem(string fullPath, bool isPublic)
    {
        FullPath = fullPath;
        IsPublic = isPublic;
        _name = Path.GetFileName(fullPath);
        _displayName = _name;
    }

    public string FullPath { get; }
    public bool IsPublic { get; }
    public string FolderPath => Path.GetDirectoryName(FullPath) ?? string.Empty;

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public string DisplayName
    {
        get => _displayName;
        private set => Set(ref _displayName, value);
    }

    public string TypeName
    {
        get => _typeName;
        private set => Set(ref _typeName, value);
    }

    public string Extension
    {
        get => _extension;
        private set => Set(ref _extension, value);
    }

    public bool IsFolder
    {
        get => _isFolder;
        private set => Set(ref _isFolder, value);
    }

    public bool IsHidden
    {
        get => _isHidden;
        private set => Set(ref _isHidden, value);
    }

    public DateTime Modified
    {
        get => _modified;
        private set
        {
            if (Set(ref _modified, value))
            {
                OnPropertyChanged(nameof(ModifiedText));
            }
        }
    }

    public long Size
    {
        get => _size;
        private set
        {
            if (Set(ref _size, value))
            {
                OnPropertyChanged(nameof(SizeText));
            }
        }
    }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (Set(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon is not null;

    public bool IsRenaming
    {
        get => _isRenaming;
        set => Set(ref _isRenaming, value);
    }

    public string EditName
    {
        get => _editName;
        set => Set(ref _editName, value);
    }

    public string IconCacheKey => $"{FullPath}|{Modified.Ticks}|{IsFolder}";

    public string SizeText => IsFolder ? "Folder" : Helpers.FileSizeFormatter.Format(Size);

    public string ModifiedText => Modified == default ? string.Empty : Modified.ToString("yyyy-MM-dd HH:mm");

    public string TypeLabel
    {
        get
        {
            if (IsFolder)
            {
                return "Folder";
            }

            if (!string.IsNullOrWhiteSpace(TypeName))
            {
                return TypeName;
            }

            return string.IsNullOrWhiteSpace(Extension) ? "File" : Extension.TrimStart('.').ToUpperInvariant() + " file";
        }
    }

    public void SetMetadata(
        string name,
        string displayName,
        string typeName,
        string extension,
        bool isFolder,
        bool isHidden,
        DateTime modified,
        long size)
    {
        Name = name;
        DisplayName = displayName;
        TypeName = typeName;
        Extension = extension;
        IsFolder = isFolder;
        IsHidden = isHidden;
        Modified = modified;
        Size = size;
    }

    public void UpdateFrom(DesktopItem other)
    {
        if (!string.Equals(FullPath, other.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool iconNeedsReload =
            _modified != other.Modified ||
            _isFolder != other.IsFolder ||
            !string.Equals(_name, other.Name, StringComparison.Ordinal);

        Name = other.Name;
        DisplayName = other.DisplayName;
        TypeName = other.TypeName;
        Extension = other.Extension;
        IsFolder = other.IsFolder;
        IsHidden = other.IsHidden;
        Modified = other.Modified;
        Size = other.Size;

        if (iconNeedsReload)
        {
            Icon = null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
