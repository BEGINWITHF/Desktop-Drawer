namespace DesktopDrawer.Models;

/// <summary>
/// A ready-made palette. The key is used for the localized display name and the
/// dictionary stores the same colour tokens as the manual colour editor.
/// </summary>
public sealed record ThemePreset(string Key, IReadOnlyDictionary<string, string> Colors)
{
    public string LabelKey => "Style.Theme." + Key;
}

public static class ThemePresets
{
    public static readonly ThemePreset[] All =
    [
        new("Monochrome", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeColorTokens.Accent] = "#FFFFFF",
            [ThemeColorTokens.TextPrimary] = "#F0F0F0",
            [ThemeColorTokens.TextSecondary] = "#B0B0B0",
            [ThemeColorTokens.TextMuted] = "#707070",
            [ThemeColorTokens.Surface] = "#FFFFFF",
            [ThemeColorTokens.Panel] = "#0D0D0D",
            [ThemeColorTokens.Border] = "#FFFFFF",
            [ThemeColorTokens.Danger] = "#FF5A5A",
            [ThemeColorTokens.Shade] = "#000000"
        }),
        new("Graphite", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeColorTokens.Accent] = "#B8C0CC",
            [ThemeColorTokens.TextPrimary] = "#F2F4F7",
            [ThemeColorTokens.TextSecondary] = "#B4BAC4",
            [ThemeColorTokens.TextMuted] = "#7A828D",
            [ThemeColorTokens.Surface] = "#E6EAF0",
            [ThemeColorTokens.Panel] = "#17191C",
            [ThemeColorTokens.Border] = "#DDE2E8",
            [ThemeColorTokens.Danger] = "#FF6B6B",
            [ThemeColorTokens.Shade] = "#000000"
        }),
        new("Ocean", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeColorTokens.Accent] = "#4CC9F0",
            [ThemeColorTokens.TextPrimary] = "#EAF7FB",
            [ThemeColorTokens.TextSecondary] = "#A9C7D4",
            [ThemeColorTokens.TextMuted] = "#6E8B99",
            [ThemeColorTokens.Surface] = "#D9F1FA",
            [ThemeColorTokens.Panel] = "#0A1A22",
            [ThemeColorTokens.Border] = "#BFE6F2",
            [ThemeColorTokens.Danger] = "#FF6B6B",
            [ThemeColorTokens.Shade] = "#001018"
        }),
        new("Forest", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeColorTokens.Accent] = "#7BD389",
            [ThemeColorTokens.TextPrimary] = "#ECF7EE",
            [ThemeColorTokens.TextSecondary] = "#B6D3BC",
            [ThemeColorTokens.TextMuted] = "#7A9B82",
            [ThemeColorTokens.Surface] = "#E2F3E6",
            [ThemeColorTokens.Panel] = "#0D1A12",
            [ThemeColorTokens.Border] = "#CDE8D4",
            [ThemeColorTokens.Danger] = "#FF6B6B",
            [ThemeColorTokens.Shade] = "#001008"
        }),
        new("Sunset", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeColorTokens.Accent] = "#FFB703",
            [ThemeColorTokens.TextPrimary] = "#FFF5E6",
            [ThemeColorTokens.TextSecondary] = "#E8C9A0",
            [ThemeColorTokens.TextMuted] = "#A98A63",
            [ThemeColorTokens.Surface] = "#FFF0D9",
            [ThemeColorTokens.Panel] = "#1E140A",
            [ThemeColorTokens.Border] = "#F3D9B0",
            [ThemeColorTokens.Danger] = "#FF6B6B",
            [ThemeColorTokens.Shade] = "#120800"
        }),
        new("Nord", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeColorTokens.Accent] = "#88C0D0",
            [ThemeColorTokens.TextPrimary] = "#ECEFF4",
            [ThemeColorTokens.TextSecondary] = "#D8DEE9",
            [ThemeColorTokens.TextMuted] = "#81A1C1",
            [ThemeColorTokens.Surface] = "#ECEFF4",
            [ThemeColorTokens.Panel] = "#2E3440",
            [ThemeColorTokens.Border] = "#D8DEE9",
            [ThemeColorTokens.Danger] = "#BF616A",
            [ThemeColorTokens.Shade] = "#000000"
        }),
        new("Paper", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeColorTokens.Accent] = "#1F6FEB",
            [ThemeColorTokens.TextPrimary] = "#1B1B1B",
            [ThemeColorTokens.TextSecondary] = "#4A4A4A",
            [ThemeColorTokens.TextMuted] = "#7A7A7A",
            [ThemeColorTokens.Surface] = "#000000",
            [ThemeColorTokens.Panel] = "#F5F5F5",
            [ThemeColorTokens.Border] = "#000000",
            [ThemeColorTokens.Danger] = "#C62828",
            [ThemeColorTokens.Shade] = "#000000"
        })
    ];
}
