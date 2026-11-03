using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Replays a shortcut against the real desktop: the matching icon is selected in
/// Explorer's own list view, the desktop is made the foreground window and the
/// keystroke is injected as genuine keyboard input. Global hotkeys, low level
/// hooks and shell extensions therefore react exactly as they would if the user
/// had pressed the key on the desktop, and anything they open appears above the
/// drawer like any other application window.
/// </summary>
internal static class DesktopKeyForwarder
{
    private const uint LvmFirst = 0x1000;
    private const uint LvmFindItemW = LvmFirst + 83;
    private const uint LvmSetItemState = LvmFirst + 43;

    private const uint LvfiString = 0x0002;

    private const uint LvisSelected = 0x0002;
    private const uint LvisFocused = 0x0001;

    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;

    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkMenu = 0x12;
    private const byte VkLWin = 0x5B;
    private const byte VkRWin = 0x5C;

    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint MouseeventfMove = 0x0001;
    private const uint KeyeventfExtendedKey = 0x0001;
    private const uint KeyeventfKeyUp = 0x0002;
    private const uint MapvkVkToVsc = 0;
    private const uint GwOwner = 4;

    private static readonly object Gate = new();
    private static DateTime _ignoreUntil = DateTime.MinValue;

    /// <summary>
    /// True while an injected shortcut is still being processed. The window uses it
    /// to ignore keys that bounce back into the drawer.
    /// </summary>
    public static bool IsForwarding => DateTime.UtcNow < _ignoreUntil;

    /// <summary>
    /// WPF does not always report the Windows key through Keyboard.Modifiers, so
    /// the pressed state is read straight from the keyboard.
    /// </summary>
    public static bool IsWinDown =>
        (GetAsyncKeyState(VkLWin) & 0x8000) != 0 ||
        (GetAsyncKeyState(VkRWin) & 0x8000) != 0;

    public static bool TryForward(
        IntPtr owner,
        string? path,
        byte virtualKey,
        bool control,
        bool shift,
        bool alt,
        bool win)
    {
        lock (Gate)
        {
            if (IsForwarding)
            {
                // Re-entrant call caused by our own injected keys; swallow it.
                return true;
            }
        }

        IntPtr list = FindIconList();
        if (list == IntPtr.Zero)
        {
            AppLog.Write("Forward: the desktop icon list is not available");
            return false;
        }

        IntPtr progman = FindWindow("Progman", null);

        int index = -1;
        if (!string.IsNullOrEmpty(path))
        {
            index = FindIndex(list, Path.GetFileName(path));
        }

        try
        {
            if (index >= 0)
            {
                SelectOnly(list, index);
            }

            HashSet<IntPtr> desktopWindows = SnapshotDesktopWindows(progman);

            // Best effort: if the desktop can own the foreground, tools that check
            // the active window see the same state as a real desktop shortcut.
            // Global hotkeys and hooks fire regardless, so this is not a gate.
            bool focusedDesktop = ForceForeground(progman);

            _ignoreUntil = DateTime.UtcNow.AddMilliseconds(500);
            bool injected = SendChord(virtualKey, control, shift, alt, win);

            if (!injected)
            {
                // Injection was refused; at least let view level handlers see it.
                PostChord(list, virtualKey, control, shift, alt, win);
            }

            AppLog.Write($"Forwarded 0x{virtualKey:X2} (desktop focused: {focusedDesktop}, injected: {injected})");
            WatchAfterForward(owner, progman, desktopWindows, focusedDesktop);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not forward the shortcut to the desktop");
            return false;
        }
    }

    private static bool SendChord(byte virtualKey, bool control, bool shift, bool alt, bool win)
    {
        var downs = new List<Input>();
        var pressedModifiers = new List<byte>();

        // Only synthesize modifiers the user is not already holding; injecting a
        // duplicate modifier would leave the OS believing it was released.
        if (control && !IsHeld(VkControl))
        {
            downs.Add(KeyInput(VkControl, up: false));
            pressedModifiers.Add(VkControl);
        }

        if (shift && !IsHeld(VkShift))
        {
            downs.Add(KeyInput(VkShift, up: false));
            pressedModifiers.Add(VkShift);
        }

        if (alt && !IsHeld(VkMenu))
        {
            downs.Add(KeyInput(VkMenu, up: false));
            pressedModifiers.Add(VkMenu);
        }

        if (win && !IsHeld(VkLWin))
        {
            downs.Add(KeyInput(VkLWin, up: false));
            pressedModifiers.Add(VkLWin);
        }

        downs.Add(KeyInput(virtualKey, up: false));

        var releases = new List<Input> { KeyInput(virtualKey, up: true) };

        // Release modifiers in the opposite order they were pressed.
        for (int i = pressedModifiers.Count - 1; i >= 0; i--)
        {
            releases.Add(KeyInput(pressedModifiers[i], up: true));
        }

        Send(downs.ToArray());

        // Give polling tools (GetAsyncKeyState) a chance to see the key down.
        Thread.Sleep(24);

        return Send(releases.ToArray());
    }

    private static bool Send(Input[] inputs)
    {
        if (inputs.Length == 0)
        {
            return true;
        }

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        if (sent != inputs.Length)
        {
            AppLog.Write($"SendInput delivered {sent}/{inputs.Length} events (error {Marshal.GetLastWin32Error()})");
            return false;
        }

        return true;
    }

    private static Input KeyInput(byte virtualKey, bool up)
    {
        uint scan = MapVirtualKey(virtualKey, MapvkVkToVsc);
        uint flags = 0;

        if (up)
        {
            flags |= KeyeventfKeyUp;
        }

        if (IsExtended(virtualKey, scan))
        {
            flags |= KeyeventfExtendedKey;
        }

        return new Input
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = virtualKey,
                    wScan = (ushort)scan,
                    dwFlags = flags
                }
            }
        };
    }

    private static bool IsExtended(byte virtualKey, uint scan)
    {
        if (((scan >> 8) & 0xFF) == 0xE0)
        {
            return true;
        }

        return virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
            or 0x2D or 0x2E or 0x2F or 0x5B or 0x5C or 0x5D or 0x6F or 0x90;
    }

    private static bool IsHeld(byte virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void NudgeMouse()
    {
        var input = new Input
        {
            type = InputMouse,
            u = new InputUnion
            {
                mi = new MouseInput { dwFlags = MouseeventfMove }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
        Thread.Sleep(30);
    }

    private static void WatchAfterForward(IntPtr owner, IntPtr progman, HashSet<IntPtr> before, bool focusedDesktop)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        int tick = 0;

        timer.Tick += (_, _) =>
        {
            tick++;

            try
            {
                AppLog.Write($"Forward watch: foreground {Describe(GetForegroundWindow())}");

                foreach (IntPtr window in SnapshotDesktopWindows(progman))
                {
                    if (!before.Contains(window) && IsWindowVisible(window))
                    {
                        AppLog.Write($"Forward watch: new window {Describe(window)} {DescribeSize(window)}");
                    }
                }

                if (tick >= 4)
                {
                    timer.Stop();

                    // Keep the focus on anything the shortcut opened; only take it
                    // back when the desktop is still in front.
                    IntPtr foreground = GetForegroundWindow();
                    if (focusedDesktop && owner != IntPtr.Zero &&
                        (foreground == progman || foreground == IntPtr.Zero))
                    {
                        ForceForeground(owner);
                    }

                    _ignoreUntil = DateTime.MinValue;
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                _ignoreUntil = DateTime.MinValue;
                AppLog.Write(ex, "Could not inspect the forwarded shortcut");
            }
        };
        timer.Start();
    }

    private static HashSet<IntPtr> SnapshotDesktopWindows(IntPtr progman)
    {
        var windows = new HashSet<IntPtr>();

        if (progman != IntPtr.Zero)
        {
            EnumChildWindows(progman, (window, _) =>
            {
                windows.Add(window);
                return true;
            }, IntPtr.Zero);
        }

        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window))
            {
                windows.Add(window);
            }

            if (ClassName(window) == "WorkerW")
            {
                EnumChildWindows(window, (child, __) =>
                {
                    windows.Add(child);
                    return true;
                }, IntPtr.Zero);
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static string Describe(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return "none";
        }

        GetWindowThreadProcessId(window, out uint processId);
        return $"0x{window:X} [{ClassName(window)}] \"{WindowText(window)}\" pid={processId} owner=0x{GetWindow(window, GwOwner):X}";
    }

    private static string DescribeSize(IntPtr window)
    {
        if (!GetWindowRect(window, out Rect rect))
        {
            return "no rect";
        }

        return $"{rect.Right - rect.Left}x{rect.Bottom - rect.Top} at {rect.Left},{rect.Top}";
    }

    private static string ClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        GetClassName(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string WindowText(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static void PostChord(IntPtr list, byte virtualKey, bool control, bool shift, bool alt, bool win)
    {
        var modifiers = new List<byte>();
        if (control)
        {
            modifiers.Add(VkControl);
        }

        if (shift)
        {
            modifiers.Add(VkShift);
        }

        if (alt)
        {
            modifiers.Add(VkMenu);
        }

        if (win)
        {
            modifiers.Add(VkLWin);
        }

        foreach (byte modifier in modifiers)
        {
            PostMessage(list, WmKeyDown, (IntPtr)modifier, IntPtr.Zero);
        }

        PostMessage(list, WmKeyDown, (IntPtr)virtualKey, IntPtr.Zero);
        PostMessage(list, WmKeyUp, (IntPtr)virtualKey, IntPtr.Zero);

        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            PostMessage(list, WmKeyUp, (IntPtr)modifiers[i], IntPtr.Zero);
        }
    }

    private static bool ForceForeground(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        if (GetForegroundWindow() == window)
        {
            return true;
        }

        // Windows only lets the foreground process move the activation elsewhere.
        // A zero-distance mouse move counts as user input without any visible
        // effect, which unlocks that restriction.
        NudgeMouse();

        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint currentThread = GetCurrentThreadId();

        bool attached = foregroundThread != 0 &&
                        foregroundThread != currentThread &&
                        AttachThreadInput(foregroundThread, currentThread, true);

        try
        {
            SetForegroundWindow(window);

            for (int i = 0; i < 12 && GetForegroundWindow() != window; i++)
            {
                Thread.Sleep(15);
            }

            return GetForegroundWindow() == window;
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(foregroundThread, currentThread, false);
            }
        }
    }

    private static IntPtr FindIconList()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }

    private static int FindIndex(IntPtr list, string name)
    {
        IntPtr text = Marshal.StringToHGlobalUni(name);

        try
        {
            var info = new LvFindInfo
            {
                flags = LvfiString,
                psz = text
            };

            return (int)SendMessage(list, LvmFindItemW, new IntPtr(-1), ref info);
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    private static void SelectOnly(IntPtr list, int index)
    {
        var clear = new LvItem { stateMask = LvisSelected | LvisFocused, state = 0 };
        SendMessage(list, LvmSetItemState, new IntPtr(-1), ref clear);

        var select = new LvItem
        {
            stateMask = LvisSelected | LvisFocused,
            state = LvisSelected | LvisFocused
        };

        SendMessage(list, LvmSetItemState, (IntPtr)index, ref select);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LvFindInfo
    {
        public uint flags;
        public IntPtr psz;
        public IntPtr lParam;
        public int x;
        public int y;
        public uint vkDirection;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LvItem
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput mi;

        [FieldOffset(0)]
        public KeyboardInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, ref LvFindInfo lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, ref LvItem lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);
}
