namespace DesktopDrawer.Models;

/// <summary>
/// A user-defined virtual category. Items are stored as lower-cased full paths
/// so they can be matched without moving or renaming anything on disk.
/// </summary>
public sealed class CustomGroup
{
    public string Name { get; set; } = string.Empty;
    public List<string> Items { get; set; } = [];
}
