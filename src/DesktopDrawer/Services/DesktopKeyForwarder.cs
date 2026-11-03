using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Sends a keystroke to Explorer's own desktop icon list. Shell extensions and
/// third-party tools that hook the desktop view react to it and show their own
/// windows, which is what makes unknown shortcuts "just work".
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

    public static bool TryForward(string path, byte virtualKey, bool control, bool shift, bool alt)
    {
        IntPtr list = FindIconList();
        if (list == IntPtr.Zero)
        {
            return false;
        }

        int index = FindIndex(list, Path.GetFileName(path));
        if (index < 0)
        {
            return false;
        }

        try
        {
            SelectOnly(list, index);

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

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not forward the shortcut to the desktop");
            return false;
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
}
