using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// PowerToys Peek is the tool behind `Win + grave`. Instead of faking the key
/// stroke (which every other keyboard hook also sees, and which makes window
/// switchers jump in), Peek's own wake-up event is signalled after the desktop
/// selection has been updated. The preview then opens instantly and nothing else
/// reacts to the shortcut.
/// </summary>
internal static class PeekIntegration
{
    private const string ShowEventName = "Local\\ShowPeekEvent";
    private const uint EventModifyState = 0x0002;

    private static readonly object Gate = new();
    private static Shortcut? _cached;
    private static DateTime _cachedAt;

    private readonly record struct Shortcut(bool Win, bool Ctrl, bool Alt, bool Shift, int Code);

    /// <summary>True when the chord is exactly the one Peek is configured with.</summary>
    public static bool IsPeekHotkey(bool control, bool shift, bool alt, bool win, byte key)
    {
        Shortcut? shortcut = ReadShortcut();

        return shortcut is { } value &&
               value.Win == win &&
               value.Ctrl == control &&
               value.Alt == alt &&
               value.Shift == shift &&
               value.Code == key;
    }

    /// <summary>Signals Peek's shared wake-up event; false when Peek is not running.</summary>
    public static bool TryShow()
    {
        IntPtr handle = OpenEvent(EventModifyState, false, ShowEventName);

        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return SetEvent(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static Shortcut? ReadShortcut()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "PowerToys",
            "Peek",
            "settings.json");

        lock (Gate)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    _cached = null;
                    return null;
                }

                if (_cached is not null && info.LastWriteTimeUtc == _cachedAt)
                {
                    return _cached;
                }

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement shortcut = document.RootElement
                    .GetProperty("properties")
                    .GetProperty("ActivationShortcut");

                var value = new Shortcut(
                    shortcut.GetProperty("win").GetBoolean(),
                    shortcut.GetProperty("ctrl").GetBoolean(),
                    shortcut.GetProperty("alt").GetBoolean(),
                    shortcut.GetProperty("shift").GetBoolean(),
                    (int)shortcut.GetProperty("code").GetDouble());

                _cached = value;
                _cachedAt = info.LastWriteTimeUtc;
                return value;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Could not read the Peek shortcut");
                _cached = null;
                return null;
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenEvent(uint access, bool inherit, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
