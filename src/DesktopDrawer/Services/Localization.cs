using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace DesktopDrawer.Services;

/// <summary>
/// Loads the JSON string table for the selected language into the application
/// resources under the "Str." prefix. XAML reads them with DynamicResource, so
/// switching language updates the whole UI immediately.
/// </summary>
internal static class I18n
{
    public const string English = "en-US";
    public const string ChineseSimplified = "zh-CN";
    public const string ChineseHongKong = "zh-HK";
    public const string Japanese = "ja-JP";

    public static readonly string[] SupportedLanguages =
    {
        English,
        ChineseSimplified,
        ChineseHongKong,
        Japanese
    };

    private static Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
    private static string _current = English;

    public static string Current => _current;

    public static void Apply(string? language)
    {
        string code = Normalize(language);
        _fallback = Load(English);

        Dictionary<string, string> values = code == English ? new(StringComparer.Ordinal) : Load(code);
        var merged = new Dictionary<string, string>(_fallback, StringComparer.Ordinal);

        foreach ((string key, string value) in values)
        {
            merged[key] = value;
        }

        if (Application.Current is not null)
        {
            foreach ((string key, string value) in merged)
            {
                Application.Current.Resources["Str." + key] = value;
            }
        }

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch
        {
            // Keep the current culture if the requested one is unavailable.
        }

        _current = code;
    }

    public static string T(string key) =>
        Application.Current?.TryFindResource("Str." + key) as string ?? key;

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DetectSystemLanguage();
        }

        string value = language.Replace('_', '-');

        foreach (string code in SupportedLanguages)
        {
            if (string.Equals(code, value, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Hant", StringComparison.OrdinalIgnoreCase))
            {
                return ChineseHongKong;
            }

            return ChineseSimplified;
        }

        if (value.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return Japanese;
        }

        return English;
    }

    public static string DetectSystemLanguage()
    {
        string name = CultureInfo.CurrentUICulture.Name;

        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("MO", StringComparison.OrdinalIgnoreCase)
                ? ChineseHongKong
                : ChineseSimplified;
        }

        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return Japanese;
        }

        return English;
    }

    private static Dictionary<string, string> Load(string code)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Strings", $"Strings.{code}.json");
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
