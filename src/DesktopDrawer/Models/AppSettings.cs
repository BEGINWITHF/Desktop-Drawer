namespace DesktopDrawer.Models;

public enum TileDensity
{
    Compact,
    Cozy,
    Large
}

public enum SortMode
{
    Name,
    DateModified,
    Type,
    Size
}

public sealed class AppSettings
{
    public TileDensity Density { get; set; } = TileDensity.Cozy;
    public SortMode SortMode { get; set; } = SortMode.Name;
    public bool FoldersFirst { get; set; } = true;
    public bool ShowHidden { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool HideIconsOnLaunch { get; set; } = true;
    public int SettingsVersion { get; set; }
    public bool HasLaunched { get; set; }
}
