namespace DesktopDrawer.Models;

/// <summary>
/// One user-editable colour in the overlay palette. The key is what gets stored
/// in settings.json; the label and hint are shown in the colour list.
/// </summary>
public sealed record ThemeColorToken(string Key, string Label, string Hint, string DefaultValue)
{
    /// <summary>Resource key for the localized label, derived from the token key.</summary>
    public string LabelKey => "ColorToken." + char.ToUpperInvariant(Key[0]) + Key[1..];

    /// <summary>Resource key for the localized hint shown under the picker title.</summary>
    public string HintKey => LabelKey + "Hint";
}

public static class ThemeColorTokens
{
    public const string Accent = "accent";
    public const string TextPrimary = "textPrimary";
    public const string TextSecondary = "textSecondary";
    public const string TextMuted = "textMuted";
    public const string Surface = "surface";
    public const string Panel = "panel";
    public const string Border = "border";
    public const string Danger = "danger";
    public const string Shade = "shade";

    /// <summary>
    /// Colours in the order they are listed in the settings panel. The defaults
    /// are the original look of the app.
    /// </summary>
    public static readonly ThemeColorToken[] All =
    [
        new(Accent, "Accent", "Selection, highlights and the pager.", "#FFFFFF"),
        new(TextPrimary, "Primary text", "Icon names, titles and control labels.", "#F0F0F0"),
        new(TextSecondary, "Secondary text", "Descriptions, summaries and hover labels.", "#B0B0B0"),
        new(TextMuted, "Muted text", "Captions, placeholders and section labels.", "#707070"),
        new(Surface, "Surfaces", "Hover cards, fields and pressed controls.", "#FFFFFF"),
        new(Panel, "Panels", "Settings, menus, toasts, the pager and badges.", "#0D0D0D"),
        new(Border, "Borders", "Hairlines, outlines and scroll thumbs.", "#FFFFFF"),
        new(Danger, "Danger", "Destructive actions such as Exit.", "#FF5A5A"),
        new(Shade, "Shade", "Dimming behind the overlay and the wallpaper.", "#000000")
    ];

    public static ThemeColorToken? Find(string key) =>
        All.FirstOrDefault(token => string.Equals(token.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string DefaultFor(string key) =>
        Find(key)?.DefaultValue ?? "#FFFFFF";
}
