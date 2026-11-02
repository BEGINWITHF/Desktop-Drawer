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

public enum GroupMode
{
    None,
    Type,
    Name,
    Date
}

/// <summary>
/// Normalized position (0..1) of a chrome element's top-left corner.
/// </summary>
public sealed class ChromePosition
{
    public double? X { get; set; }
    public double? Y { get; set; }
}

public sealed class AppSettings
{
    public TileDensity Density { get; set; } = TileDensity.Cozy;
    public SortMode SortMode { get; set; } = SortMode.Name;
    public GroupMode GroupMode { get; set; } = GroupMode.None;
    public bool FoldersFirst { get; set; } = true;
    public bool ShowHidden { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool HideIconsOnLaunch { get; set; } = true;
    public Dictionary<string, ChromePosition> Layout { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int SettingsVersion { get; set; }
    public bool HasLaunched { get; set; }
}
