using System.Runtime.InteropServices;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;

namespace DesktopDrawer.Services;

/// <summary>
/// Tracks whether fingers are physically on the precision touchpad, using the
/// Windows 11 touchpad-capable window API.
///
/// Registering the window as touchpad capable makes two finger pans arrive as
/// WM_POINTER messages, which carry a real down and a real up for every contact.
/// That is the reliable gesture boundary: the drawer no longer has to guess the
/// end of a swipe from a pause in the wheel stream, and the inertia tail after
/// the fingers leave the pad can never turn another page.
///
/// This is deliberately not HID raw input. Registering the digitizer collection
/// as raw input made the precision touchpad driver emit a feedback beep on this
/// machine, so that route stays removed.
/// </summary>
internal sealed class TouchpadContactTracker
{
    private const int PointerIdMask = 0xFFFF;

    private readonly HashSet<uint> _contacts = [];

    /// <summary>True when the system accepted the touchpad-capable registration.</summary>
    public bool Available { get; private set; }

    /// <summary>True while at least one touchpad contact is down.</summary>
    public bool FingersDown => _contacts.Count > 0;

    public bool Register(IntPtr hwnd)
    {
        try
        {
            Available = NativeMethods.RegisterTouchpadCapableWindow(hwnd, true);

            AppLog.Write(Available
                ? "Touchpad contact tracking is available"
                : $"Touchpad contact tracking unavailable (error {Marshal.GetLastWin32Error()})");

            return Available;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not register for touchpad pointer input");
            Available = false;
            return false;
        }
    }

    /// <summary>
    /// Updates the contact set from a window message. Returns true when the
    /// message was a touchpad contact change, or false for every other message
    /// so the caller can leave it for the normal message processing.
    /// </summary>
    public bool Track(int message, IntPtr wParam)
    {
        if (!Available)
        {
            return false;
        }

        if (message is not (NativeMethods.WmPointerDown
            or NativeMethods.WmPointerUp
            or NativeMethods.WmPointerLeave
            or NativeMethods.WmPointerCaptureChanged))
        {
            return false;
        }

        if (!NativeMethods.GetPointerInfo(
                (uint)(wParam.ToInt64() & PointerIdMask),
                out NativeMethods.PointerInfo info))
        {
            return false;
        }

        if (info.PointerType != NativeMethods.PointerTypeTouchpad)
        {
            return false;
        }

        if (message == NativeMethods.WmPointerDown)
        {
            bool wasDown = FingersDown;
            _contacts.Add(info.PointerId);

            if (!wasDown)
            {
                AppLog.Write("Touchpad contact started");
            }

            return true;
        }

        if (message is NativeMethods.WmPointerUp or NativeMethods.WmPointerLeave)
        {
            bool wasDown = FingersDown;
            _contacts.Remove(info.PointerId);

            if (wasDown && !FingersDown)
            {
                AppLog.Write("Touchpad contact ended");
            }

            return true;
        }

        // Capture changed: the contact stream was interrupted, so nothing that
        // follows can be trusted to belong to the same gesture.
        if (_contacts.Count > 0)
        {
            _contacts.Clear();
            AppLog.Write("Touchpad contact ended (capture changed)");
        }

        return true;
    }
}
