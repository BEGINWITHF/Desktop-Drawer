using System.Runtime.InteropServices;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Tracks whether fingers are physically on the touchpad. Windows delivers HID
/// digitizer reports (usage page 0x0D) continuously while the pad is touched, even
/// when the fingers are not moving, so their absence means the fingers were lifted.
/// This is what separates two swipes without waiting for a pause.
/// </summary>
internal static class TouchpadMonitor
{
    private const uint RidInputDeviceInfo = 0x2000000B;
    private const ushort DigitizerUsagePage = 0x0D;

    private static readonly HashSet<IntPtr> KnownDevices = new();
    private static readonly HashSet<IntPtr> Digitizers = new();
    private static readonly object Gate = new();

    private static DateTime _lastReport = DateTime.MinValue;
    private static int _liftCount;

    /// <summary>True once a real touchpad report has been seen.</summary>
    public static bool Supported { get; private set; }

    /// <summary>Increments every time the fingers are put back on the pad.</summary>
    public static int LiftCount => _liftCount;

    /// <summary>Called for every HID raw input report.</summary>
    public static void NoteReport(IntPtr device, uint reportSize)
    {
        if (device == IntPtr.Zero)
        {
            return;
        }

        lock (Gate)
        {
            if (!KnownDevices.Contains(device))
            {
                KnownDevices.Add(device);

                if (GetUsagePage(device) == DigitizerUsagePage)
                {
                    Digitizers.Add(device);
                }
            }

            if (!Digitizers.Contains(device))
            {
                return;
            }

            DateTime now = DateTime.UtcNow;

            if ((now - _lastReport).TotalMilliseconds > 60)
            {
                _liftCount++;
            }

            _lastReport = now;

            if (!Supported)
            {
                Supported = true;
                AppLog.Write("Touchpad contact tracking is available");
            }
        }
    }

    private static ushort GetUsagePage(IntPtr device)
    {
        try
        {
            uint size = 32;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);

            try
            {
                uint result = GetRawInputDeviceInfo(device, RidInputDeviceInfo, buffer, ref size);
                if (result == uint.MaxValue || size < 32)
                {
                    return 0;
                }

                // cbSize (4) + dwType (4) + vendor (4) + product (4) + version (4)
                return (ushort)Marshal.ReadInt16(buffer, 20);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
}
