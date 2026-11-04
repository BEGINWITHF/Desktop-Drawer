using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>A Windows key combination caught before the shell can consume it.</summary>
internal readonly record struct WinChord(byte Key, bool Control, bool Shift, bool Alt);

/// <summary>
/// Watches the keyboard for Windows key combinations while the drawer is the
/// active window. WPF never receives those keys (the shell eats them), so this
/// hook is what makes Win+... shortcuts reach the desktop replay.
/// </summary>
internal sealed class GlobalShortcutHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private const int GaRoot = 2;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    /// <summary>
    /// The Windows key is swallowed while the drawer is active so the shell never
    /// opens Start or takes the input away; every combination is replayed on the
    /// desktop instead. A bare tap opens Start again through an injected tap.
    /// </summary>
    private bool _winDown;
    private bool _chordPressed;
    private DateTime _winExpiry = DateTime.MinValue;

    private readonly IntPtr _owner;
    private readonly Dispatcher _dispatcher;
    private readonly Action<WinChord> _onChord;

    // The delegate must stay alive for as long as the hook is installed.
    private readonly HookProc _callback;
    private readonly HashSet<byte> _suppressed = new();

    private IntPtr _hook;

    public GlobalShortcutHook(IntPtr owner, Dispatcher dispatcher, Action<WinChord> onChord)
    {
        _owner = owner;
        _dispatcher = dispatcher;
        _onChord = onChord;
        _callback = OnKeyboardMessage;
    }

    public bool Install()
    {
        IntPtr module = GetModuleHandle(null);

        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, module, 0);

        if (_hook == IntPtr.Zero)
        {
            AppLog.Write($"Could not install the shortcut hook (error {Marshal.GetLastWin32Error()})");
            return false;
        }

        AppLog.Write("Shortcut hook installed.");
        return true;
    }

    private IntPtr OnKeyboardMessage(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        // Our own replay must never be captured again.
        if (DesktopKeyForwarder.IsForwarding)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

        int message = wParam.ToInt32();
        bool up = message is WmKeyUp or WmSysKeyUp;
        bool down = message is WmKeyDown or WmSysKeyDown;

        if (!up && !down)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        byte key = (byte)data.vkCode;

        if (up)
        {
            if (!_suppressed.Remove(key))
            {
                return CallNextHookEx(_hook, code, wParam, lParam);
            }

            if (key is VkLWin or VkRWin)
            {
                bool tapped = !_chordPressed;
                _winDown = false;

                if (tapped)
                {
                    // The user just pressed the Windows key; keep Start working.
                    DesktopKeyForwarder.SendWinTap();
                }
            }

            return new IntPtr(1);
        }

        if (!IsOwnerActive())
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (DateTime.UtcNow > _winExpiry)
        {
            _winDown = false;
        }

        if (key is VkLWin or VkRWin)
        {
            _suppressed.Add(key);
            _winDown = true;
            _chordPressed = false;
            _winExpiry = DateTime.UtcNow.AddSeconds(3);
            return new IntPtr(1);
        }

        if (!_winDown || key is VkShift or VkControl or VkMenu)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        var chord = new WinChord(
            key,
            IsPressed(VkControl),
            IsPressed(VkShift),
            IsPressed(VkMenu));

        _suppressed.Add(key);
        _chordPressed = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Input, () => _onChord(chord));
        return new IntPtr(1);
    }

    private bool IsOwnerActive()
    {
        IntPtr foreground = GetForegroundWindow();

        if (foreground == _owner)
        {
            return true;
        }

        return foreground != IntPtr.Zero && GetAncestor(foreground, GaRoot) == _owner;
    }

    private static bool IsPressed(int key)    {
        return (GetAsyncKeyState(key) & 0x8000) != 0;
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
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
