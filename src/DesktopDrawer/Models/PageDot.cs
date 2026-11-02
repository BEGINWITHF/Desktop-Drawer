namespace DesktopDrawer.Models;

public sealed class PageDot
{
    public required int Index { get; init; }
    public required string Label { get; init; }
    public required bool IsCurrent { get; init; }
    public required string Tooltip { get; init; }
}
