using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DesktopDrawer.Helpers;

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
        return Registry.GetValue(
            @"HKEY_CURRENT_USER\Control Panel\Desktop",
            "Wallpaper",
            null) as string;
    }

    public static ImageSource? CreateBlurredBackdrop(int width, int height)
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
            bitmap.DecodePixelWidth = width;
            bitmap.EndInit();
            bitmap.Freeze();

            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                var brush = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            }

            visual.Effect = new BlurEffect
            {
                Radius = 44,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Performance
            };

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not create wallpaper backdrop");
            return null;
        }
    }
}
