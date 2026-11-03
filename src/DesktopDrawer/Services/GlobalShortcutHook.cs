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
    /// Combinations Windows itself owns. They are left alone so system behaviour
    /// stays untouched; everything else with the Windows key is replayed.
    /// </summary>
    private static readonly HashSet<byte> SystemKeys = BuildSystemKeys();

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

        if (up)
        {
            return _suppressed.Remove((byte)data.vkCode)
                ? new IntPtr(1)
                : CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (!IsOwnerActive() ||
            !IsChordWithWin((byte)data.vkCode) ||
            SystemKeys.Contains((byte)data.vkCode))
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        var chord = new WinChord(
            (byte)data.vkCode,
            IsPressed(VkControl),
            IsPressed(VkShift),
            IsPressed(VkMenu));

        _suppressed.Add((byte)data.vkCode);
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

    private static bool IsChordWithWin(byte key)
    {
        if (key is VkShift or VkControl or VkMenu or VkLWin or VkRWin or 0x5D or 0x90 or 0x91)
        {
            return false;
        }

        return IsPressed(VkLWin) || IsPressed(VkRWin);
    }

    private static bool IsPressed(int key)
    {
        return (GetAsyncKeyState(key) & 0x8000) != 0;
    }

    private static HashSet<byte> BuildSystemKeys()
    {
        var keys = new HashSet<byte>();

        for (byte key = 0x30; key <= 0x39; key++)
        {
            keys.Add(key); // 0-9
        }

        for (byte key = 0x41; key <= 0x5A; key++)
        {
            keys.Add(key); // A-Z
        }

        for (byte key = 0x70; key <= 0x87; key++)
        {
            keys.Add(key); // F1-F24
        }

        foreach (byte key in new byte[]
                 {
                     0x09, 0x0D, 0x1B, 0x20,
                     0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
                     0x2D, 0x2E,
                     0xBA, 0xBB, 0xBC, 0xBE, 0xBF, 0xDB, 0xDD, 0xDE
                 })
        {
            keys.Add(key);
        }

        return keys;
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
