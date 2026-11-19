using System.IO;

namespace DesktopDrawer.Helpers;

public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopDrawer");

    public static string LogPath => Path.Combine(LogDirectory, "desktop-drawer.log");

    /// <summary>
    /// Diagnostic logging is opt-in (Settings > Behavior) and off by default.
    /// Nothing is written while this is false, including handled exceptions.
    /// </summary>
    public static bool Enabled { get; set; }

    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);

                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                {
                    File.Delete(LogPath);
                }

                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public static void Write(Exception exception, string context)
    {
        Write($"{context}: {exception}");
    }
}
