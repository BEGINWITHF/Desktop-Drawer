using System.IO;
using System.Text;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Short, explicit recording sessions for input problems: the user starts a
/// recording, reproduces the gesture, and everything the system reports about
/// wheels and raw mouse input is written to a log file that can be inspected.
/// </summary>
internal static class InputDiagnostics
{
    private static readonly object Gate = new();
    private static DateTime _until = DateTime.MinValue;

    public static bool IsRecording => DateTime.UtcNow < _until;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopDrawer",
        "input-diagnostic.log");

    public static void Start(int seconds)
    {
        lock (Gate)
        {
            _until = DateTime.UtcNow.AddSeconds(seconds);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

                var header = new StringBuilder();
                header.AppendLine($"# Desktop Drawer input diagnostic, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                header.AppendLine($"# recording {seconds} s");
                File.WriteAllText(LogPath, header.ToString());
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Could not start the input diagnostic");
            }
        }

        AppLog.Write($"Input diagnostic started for {seconds} s");
        Write("session start");
    }

    public static void Stop()
    {
        if (!IsRecording)
        {
            return;
        }

        Write("session end");
        _until = DateTime.MinValue;
    }

    public static void Write(string message)
    {
        if (!IsRecording)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never break input handling.
        }
    }
}
