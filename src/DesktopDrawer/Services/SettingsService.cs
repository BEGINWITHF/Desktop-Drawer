using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopDrawer.Helpers;
using DesktopDrawer.Models;

namespace DesktopDrawer.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsService()
    {
        SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopDrawer");
        SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    }

    public string SettingsDirectory { get; }
    public string SettingsPath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                AppSettings fresh = new() { SettingsVersion = CurrentSettingsVersion };
                Save(fresh);
                return fresh;
            }

            string json = File.ReadAllText(SettingsPath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Colors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (settings.SettingsVersion < CurrentSettingsVersion)
            {
                Migrate(settings);
                Save(settings);
            }

            return settings;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not read settings");
            return new AppSettings { SettingsVersion = CurrentSettingsVersion };
        }
    }

    private const int CurrentSettingsVersion = 4;

    private static void Migrate(AppSettings settings)
    {
        if (settings.SettingsVersion < 2)
        {
            settings.StartWithWindows = true;
            settings.HideIconsOnLaunch = true;
            settings.SettingsVersion = 2;
        }

        if (settings.SettingsVersion < 3)
        {
            settings.StartWithWindows = true;
            settings.SettingsVersion = 3;
        }

        if (settings.SettingsVersion < 4)
        {
            // The single accent colour became a full palette. Keep the old choice.
            if (!string.IsNullOrWhiteSpace(settings.AccentColor) &&
                !settings.Colors.ContainsKey(ThemeColorTokens.Accent))
            {
                settings.Colors[ThemeColorTokens.Accent] = settings.AccentColor;
            }

            settings.AccentColor = string.Empty;
            settings.SettingsVersion = 4;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not save settings");
        }
    }
}
