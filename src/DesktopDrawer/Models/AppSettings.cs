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

    /// <summary>Uniform scale of the chrome element. Null/1 = its natural size.</summary>
    public double? Scale { get; set; }

    /// <summary>Independent horizontal and vertical scale (override <see cref="Scale"/>).</summary>
    public double? ScaleX { get; set; }
    public double? ScaleY { get; set; }

    /// <summary>Explicit layout size in device-independent pixels.</summary>
    public double? Width { get; set; }
    public double? Height { get; set; }
}

public sealed class AppSettings
{
    public TileDensity Density { get; set; } = TileDensity.Cozy;
    public int IconSize { get; set; }          // 0 = use the density preset
    public int GridColumns { get; set; }       // 0 = fit to the window
    public int GridRows { get; set; }          // 0 = fit to the window
    public SortMode SortMode { get; set; } = SortMode.Name;
    public GroupMode GroupMode { get; set; } = GroupMode.None;
    public bool FoldersFirst { get; set; } = true;
    public bool ShowHidden { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool HideIconsOnLaunch { get; set; } = true;
    public string AccentColor { get; set; } = string.Empty;   // legacy, migrated into Colors
    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int FontScale { get; set; } = 100;                  // percent, 80..140
    public int WallpaperDim { get; set; }                      // 0..60 percent
    public bool AlwaysShowChrome { get; set; }
    public Dictionary<string, ChromePosition> Layout { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int SettingsVersion { get; set; }
    public bool HasLaunched { get; set; }
}
