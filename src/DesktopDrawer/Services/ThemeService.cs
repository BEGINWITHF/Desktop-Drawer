using System.Windows;
using System.Windows.Media;
using DesktopDrawer.Helpers;
using DesktopDrawer.Models;

namespace DesktopDrawer.Services;

/// <summary>
/// Applies the user's colour palette to the shared brushes at runtime. Every
/// brush is replaced in the application resources, so controls that reference
/// them with DynamicResource update the moment a colour changes.
/// </summary>
internal static class ThemeService
{
    public static Color CurrentAccent { get; private set; } = Parse(ThemeColorTokens.DefaultFor(ThemeColorTokens.Accent));
    public static Color CurrentShade { get; private set; } = Parse(ThemeColorTokens.DefaultFor(ThemeColorTokens.Shade));

    private static readonly (string Key, double BaseSize)[] FontSizes =
    {
        ("FontSize95", 9.5),
        ("FontSize10", 10),
        ("FontSize105", 10.5),
        ("FontSize11", 11),
        ("FontSize115", 11.5),
        ("FontSize12", 12),
        ("FontSize125", 12.5),
        ("FontSize13", 13),
        ("FontSize14", 14),
        ("FontSize15", 15),
        ("FontSize17", 17),
        ("FontSize18", 18),
        ("FontSize52", 52)
    };

    /// <summary>Rescales every font-size resource used by the theme.</summary>
    public static void ApplyFontScale(int percent)
    {
        if (Application.Current is null)
        {
            return;
        }

        double scale = Math.Clamp(percent, 80, 140) / 100.0;

        foreach ((string key, double baseSize) in FontSizes)
        {
            Application.Current.Resources[key] = baseSize * scale;
        }
    }

    public static void Apply(IReadOnlyDictionary<string, string>? colors)
    {
        Color accent = Read(colors, ThemeColorTokens.Accent);
        Color textPrimary = Read(colors, ThemeColorTokens.TextPrimary);
        Color textSecondary = Read(colors, ThemeColorTokens.TextSecondary);
        Color textMuted = Read(colors, ThemeColorTokens.TextMuted);
        Color surface = Read(colors, ThemeColorTokens.Surface);
        Color panel = Read(colors, ThemeColorTokens.Panel);
        Color border = Read(colors, ThemeColorTokens.Border);
        Color danger = Read(colors, ThemeColorTokens.Danger);
        Color shade = Read(colors, ThemeColorTokens.Shade);

        CurrentAccent = accent;
        CurrentShade = shade;

        // Text.
        SetBrush("TextPrimaryBrush", textPrimary);
        SetBrush("TextSecondaryBrush", textSecondary);
        SetBrush("TextMutedBrush", textMuted);
        SetBrush("WindowTextBrush", textPrimary);
        SetBrush("EmptyGlyphBrush", WithAlpha(textPrimary, 0x2E));
        SetBrush("IconPlaceholderBrush", WithAlpha(textPrimary, 0x66));

        // Accent and contrast pairs.
        SetBrush("AccentBrush", accent);
        SetBrush("AccentLightBrush", Blend(accent, Colors.White, 0.38));
        SetBrush("AccentSoftBrush", WithAlpha(accent, 0x2E));
        SetBrush("AccentBorderBrush", WithAlpha(accent, 0x66));
        SetBrush("AccentHoverBrush", Blend(accent, Colors.White, 0.22));
        SetBrush("OnAccentBrush", IsLight(accent) ? Parse("#0B0B0B") : Colors.White);
        SetBrush("SelectionBorderBrush", accent);
        SetBrush("SelectionFillBrush", WithAlpha(accent, 0x33));
        SetBrush("DangerBrush", danger);

        // Subtle surfaces (hover cards, fields, pressed buttons).
        SetBrush("NavSurfaceBrush", WithAlpha(surface, 0x0B));
        SetBrush("BadgeSurfaceBrush", WithAlpha(surface, 0x0E));
        SetBrush("SegmentSurfaceBrush", WithAlpha(surface, 0x0F));
        SetBrush("TileHoverBrush", WithAlpha(surface, 0x10));
        SetBrush("GhostSurfaceBrush", WithAlpha(surface, 0x10));
        SetBrush("PanelBrush", WithAlpha(surface, 0x12));
        SetBrush("ButtonHoverBrush", WithAlpha(surface, 0x14));
        SetBrush("PanelHoverBrush", WithAlpha(surface, 0x16));
        SetBrush("GhostHoverBrush", WithAlpha(surface, 0x1C));
        SetBrush("ButtonPressedBrush", WithAlpha(surface, 0x20));
        SetBrush("ButtonHoverStrongBrush", WithAlpha(surface, 0x22));
        SetBrush("PageDotPressedBrush", WithAlpha(surface, 0x24));
        SetBrush("RenameSurfaceBrush", WithAlpha(surface, 0x26));
        SetBrush("TrackBrush", WithAlpha(surface, 0x26));
        SetBrush("GhostPressedBrush", WithAlpha(surface, 0x26));
        SetBrush("NavPressedBrush", WithAlpha(surface, 0x2A));

        // Strong panels (settings, menus, toasts, the pager and badges).
        SetBrush("SettingsPanelBrush", WithAlpha(panel, 0xFF));
        SetBrush("PanelStrongBrush", WithAlpha(panel, 0xFF));
        SetBrush("MenuSurfaceBrush", WithAlpha(panel, 0xFF));
        SetBrush("ToastSurfaceBrush", WithAlpha(panel, 0xFF));
        SetBrush("PagerSurfaceBrush", WithAlpha(Blend(panel, shade, 0.45), 0xA6));
        SetBrush("BadgePanelBrush", WithAlpha(Blend(panel, shade, 0.45), 0xB3));
        SetBrush("CloseSurfaceBrush", WithAlpha(Blend(panel, Colors.White, 0.16), 0xD9));
        SetBrush("CloseHoverBrush", WithAlpha(Blend(panel, Colors.White, 0.22), 0xF0));
        SetBrush("SearchSurfaceBrush", WithAlpha(panel, 0x8C));
        SetBrush("SearchFocusSurfaceBrush", WithAlpha(panel, 0xB3));

        // Borders, dividers and thumbs.
        SetBrush("DividerBrush", WithAlpha(border, 0x12));
        SetBrush("MenuDividerBrush", WithAlpha(border, 0x16));
        SetBrush("TileHoverBorderBrush", WithAlpha(border, 0x18));
        SetBrush("BadgeBorderBrush", WithAlpha(border, 0x18));
        SetBrush("DividerStrongBrush", WithAlpha(border, 0x1A));
        SetBrush("StrokeBrush", WithAlpha(border, 0x1C));
        SetBrush("SettingsPanelBorderBrush", WithAlpha(border, 0x22));
        SetBrush("MenuBorderBrush", WithAlpha(border, 0x2A));
        SetBrush("ToastBorderBrush", WithAlpha(border, 0x2A));
        SetBrush("TrackBorderBrush", WithAlpha(border, 0x2E));
        SetBrush("ScrollThumbBrush", WithAlpha(border, 0x2E));
        SetBrush("StrokeStrongBrush", WithAlpha(border, 0x33));
        SetBrush("PagerBorderBrush", WithAlpha(border, 0x3D));
        SetBrush("SearchBorderBrush", WithAlpha(border, 0x3D));
        SetBrush("ScrollThumbHoverBrush", WithAlpha(border, 0x4D));

        // Dimming behind overlays and the wallpaper.
        SetBrush("ScrimBrush", WithAlpha(shade, 0x8C));
        SetBrush("PickerScrimBrush", WithAlpha(shade, 0xB3));
    }

    public static bool TryParse(string? hex, out Color color)
    {
        color = Colors.Transparent;

        string text = (hex ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        try
        {
            if (text.Length is not (4 or 7 or 9))
            {
                return false;
            }

            color = (Color)ColorConverter.ConvertFromString(text)!;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns the colour as an uppercase #RRGGBB string, or null.</summary>
    public static string? ToHex(string? hex) =>
        TryParse(hex, out Color color) ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : null;

    public static Color Read(IReadOnlyDictionary<string, string>? colors, string key)
    {
        if (colors is not null &&
            colors.TryGetValue(key, out string? value) &&
            TryParse(value, out Color parsed))
        {
            return parsed;
        }

        return Parse(ThemeColorTokens.DefaultFor(key));
    }

    public static bool IsLight(Color color) =>
        (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B) > 150;

    private static Color Parse(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex)!;

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        return Color.FromRgb(
            (byte)(from.R + ((to.R - from.R) * amount)),
            (byte)(from.G + ((to.G - from.G) * amount)),
            (byte)(from.B + ((to.B - from.B) * amount)));
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static void SetBrush(string key, Color color)
    {
        if (Application.Current is null)
        {
            return;
        }

        if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        // A frozen brush cannot be changed, and replacing it only helps controls
        // that use DynamicResource - which is why every themed consumer does.
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }
}
