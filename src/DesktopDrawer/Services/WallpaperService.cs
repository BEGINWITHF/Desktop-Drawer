using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;

namespace DesktopDrawer.Services;

public static class WallpaperService
{
    public static ImageSource? LoadWallpaper()
    {
        try
        {
            string? wallpaperPath = GetWallpaperPath();
            if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(wallpaperPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not load the wallpaper");
            return null;
        }
    }

    public static string? GetWallpaperPath()
    {
        try
        {
            var buffer = new StringBuilder(1024);
            if (NativeMethods.SystemParametersInfo(NativeMethods.SpiGetDesktopWallpaper, (uint)buffer.Capacity, buffer, 0) &&
                buffer.Length > 0)
            {
                return buffer.ToString();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not read the wallpaper path");
        }

        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Control Panel\Desktop",
                "Wallpaper",
                null) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Identifies the currently visible wallpaper, including the current frame of
    /// a slideshow, so the overlay can reload it when it changes.
    /// </summary>
    public static (string? Path, DateTime Stamp) GetWallpaperStamp()
    {
        string? path = GetWallpaperPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return (null, DateTime.MinValue);
        }

        try
        {
            return File.Exists(path)
                ? (path, File.GetLastWriteTimeUtc(path))
                : (path, DateTime.MinValue);
        }
        catch
        {
            return (path, DateTime.MinValue);
        }
    }
}
