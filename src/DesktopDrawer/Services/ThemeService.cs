using System.Windows;
using System.Windows.Media;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Applies the user's accent colour to the shared brushes at runtime, so every
/// highlight, focus ring and selection follows the chosen theme immediately.
/// </summary>
internal static class ThemeService
{
    public const string DefaultAccent = "#7C9CFF";

    public static Color CurrentAccent { get; private set; } = Parse(DefaultAccent);

    public static Color ApplyAccent(string? hex)
    {
        Color accent = TryParse(hex, out Color parsed) ? parsed : Parse(DefaultAccent);
        CurrentAccent = accent;

        SetBrush("AccentBrush", accent);
        SetBrush("AccentLightBrush", Blend(accent, Colors.White, 0.38));
        SetBrush("AccentSoftBrush", WithAlpha(accent, 0x2E));
        SetBrush("AccentBorderBrush", WithAlpha(accent, 0x66));

        return accent;
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

        // The brush is frozen (or missing); replace the resource and hope the
        // consumers read it dynamically.
        var replacement = new SolidColorBrush(color);
        Application.Current.Resources[key] = replacement;
        AppLog.Write($"Theme: replaced the frozen brush \"{key}\"");
    }
}
