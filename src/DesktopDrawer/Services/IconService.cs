using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;
using DesktopDrawer.Models;

namespace DesktopDrawer.Services;

public sealed class IconService : IDisposable
{
    private const int IconPixels = 192;

    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(4, 4);
    private bool _disposed;

    public Task<ImageSource?> GetAsync(DesktopItem item)
    {
        if (_disposed)
        {
            return Task.FromResult<ImageSource?>(null);
        }

        string key = item.IconCacheKey;
        if (_cache.TryGetValue(key, out ImageSource? cached))
        {
            return Task.FromResult<ImageSource?>(cached);
        }

        return _pending.GetOrAdd(key, _ => LoadAndCacheAsync(key, item.FullPath));
    }

    public async Task PrefetchAsync(IEnumerable<DesktopItem> items, CancellationToken cancellationToken)
    {
        foreach (DesktopItem item in items)
        {
            if (cancellationToken.IsCancellationRequested || _disposed)
            {
                return;
            }

            await GetAsync(item).ConfigureAwait(false);
        }
    }

    public void Clear()
    {
        _cache.Clear();
        _pending.Clear();
    }

    private async Task<ImageSource?> LoadAndCacheAsync(string key, string path)
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ImageSource? image = await Task.Run(() => LoadImage(path)).ConfigureAwait(false);
                if (image is not null)
                {
                    _cache[key] = image;
                }

                return image;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Could not load icon for {path}");
            return null;
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    private static ImageSource? LoadImage(string path)
    {
        IntPtr bitmapHandle = IntPtr.Zero;

        try
        {
            Guid iid = typeof(IShellItemImageFactory).GUID;
            NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out IShellItemImageFactory factory);

            try
            {
                var size = new NativeMethods.NativeSize(IconPixels, IconPixels);
                int result = factory.GetImage(
                    size,
                    SIIGBF.BiggerSizeOk | SIIGBF.ResizeToFit,
                    out bitmapHandle);

                if (result == 0 && bitmapHandle != IntPtr.Zero)
                {
                    BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                        bitmapHandle,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            finally
            {
                if (factory is not null)
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
        }
        catch
        {
            // A shell icon fallback below handles files without thumbnails.
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmapHandle);
            }
        }

        return LoadShellIcon(path);
    }

    private static ImageSource? LoadShellIcon(string path)
    {
        var info = new NativeMethods.ShFileInfo();
        IntPtr iconHandle = IntPtr.Zero;

        try
        {
            uint result = (uint)NativeMethods.SHGetFileInfo(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
                NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon);

            iconHandle = info.hIcon;
            if (result == 0 || iconHandle == IntPtr.Zero)
            {
                return null;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Could not load fallback icon for {path}");
            return null;
        }
        finally
        {
            if (iconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(iconHandle);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        _cache.Clear();
        _pending.Clear();
    }
}
