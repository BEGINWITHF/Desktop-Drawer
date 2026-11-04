using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Watches horizontal mouse wheel input (two finger swipes on a precision
/// touchpad) and turns it into page changes while the cursor is over the drawer.
/// A low level hook is needed because WM_MOUSEHWHEEL is only delivered to the
/// focused window, and the drawer is usually not focused.
/// </summary>
internal sealed class HorizontalWheelHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseHWheel = 0x020E;
    private const int GaRoot = 2;

    private readonly IntPtr _owner;
    private readonly Dispatcher _dispatcher;
    private readonly Action<int> _onSwipe;
    private readonly HookProc _callback;

    private IntPtr _hook;

    public HorizontalWheelHook(IntPtr owner, Dispatcher dispatcher, Action<int> onSwipe)
    {
        _owner = owner;
        _dispatcher = dispatcher;
        _onSwipe = onSwipe;
        _callback = OnMouseMessage;
    }

    public bool Install()
    {
        _hook = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero)
        {
            AppLog.Write($"Could not install the horizontal wheel hook (error {Marshal.GetLastWin32Error()})");
            return false;
        }

        return true;
    }

    private IntPtr OnMouseMessage(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || wParam.ToInt32() != WmMouseHWheel)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<MouseHookStruct>(lParam);
        int delta = (short)((data.MouseData >> 16) & 0xFFFF);

        if (delta == 0 || !IsOverDrawer(data.PointX, data.PointY))
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Input, () => _onSwipe(delta));
        return new IntPtr(1);
    }

    private bool IsOverDrawer(int x, int y)
    {
        var point = new Point { X = x, Y = y };
        IntPtr window = WindowFromPoint(point);

        return window != IntPtr.Zero && GetAncestor(window, GaRoot) == _owner;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public int PointX;
        public int PointY;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
