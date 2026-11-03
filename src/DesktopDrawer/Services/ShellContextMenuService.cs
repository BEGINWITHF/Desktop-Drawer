using System.Runtime.InteropServices;
using System.Windows.Interop;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;

namespace DesktopDrawer.Services;

/// <summary>
/// Shows the real Windows shell context menu for one or more files, so every
/// installed shell extension (7-Zip, Git, Send to, Share, ...) is available.
/// </summary>
internal sealed class ShellContextMenuService
{
    private const uint CmfNormal = 0x00000000;
    private const uint CmfExtendedVerbs = 0x00000100;

    private const uint TpmLeftButton = 0x0000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const int WmInitMenuPopup = 0x0117;
    private const int WmDrawItem = 0x002B;
    private const int WmMeasureItem = 0x002C;

    private const int SwShownormal = 1;

    public bool Show(IntPtr owner, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        var pidls = new List<IntPtr>();
        IntPtr childArray = IntPtr.Zero;
        IntPtr menu = IntPtr.Zero;
        object? comObject = null;
        HwndSource? source = null;
        HwndSourceHook? hook = null;

        try
        {
            comObject = CreateContextMenu(owner, paths, pidls, out childArray);

            if (comObject is not IContextMenu contextMenu)
            {
                AppLog.Write("Shell menu: no IContextMenu was created.");
                return false;
            }

            var contextMenu2 = comObject as IContextMenu2;
            var contextMenu3 = comObject as IContextMenu3;

            menu = CreatePopupMenu();
            int queryResult = contextMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, CmfNormal | CmfExtendedVerbs);
            if (queryResult < 0)
            {
                AppLog.Write($"Shell menu: QueryContextMenu failed 0x{queryResult:X8}");
                return false;
            }

            SetForegroundWindow(owner);
            ForceForeground(owner);

            if (contextMenu2 is not null || contextMenu3 is not null)
            {
                source = HwndSource.FromHwnd(owner);
                hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem))
                    {
                        return IntPtr.Zero;
                    }

                    if (contextMenu3 is not null)
                    {
                        handled = contextMenu3.HandleMenuMsg2((uint)message, wParam, lParam, out IntPtr result) == 0;
                        return handled ? result : IntPtr.Zero;
                    }

                    if (contextMenu2 is not null)
                    {
                        handled = contextMenu2.HandleMenuMsg((uint)message, wParam, lParam) == 0;
                    }

                    return IntPtr.Zero;
                };

                source?.AddHook(hook);
            }

            long startedAt = Environment.TickCount64;

            uint command = TrackPopupMenuEx(
                menu,
                TpmReturnCmd | TpmLeftButton | TpmRightButton,
                screenX,
                screenY,
                owner,
                IntPtr.Zero);

            long elapsed = Environment.TickCount64 - startedAt;

            if (hook is not null)
            {
                source?.RemoveHook(hook);
                hook = null;
            }

            if (command > 0)
            {
                var info = new CmInvokeCommandInfoEx
                {
                    cbSize = Marshal.SizeOf<CmInvokeCommandInfoEx>(),
                    fMask = 0,
                    hwnd = owner,
                    lpVerb = (IntPtr)(command - 1),
                    nShow = SwShownormal,
                    ptInvoke = new NativePoint { X = screenX, Y = screenY }
                };

                contextMenu.InvokeCommand(ref info);
                return true;
            }

            // The menu can fail to appear when the owner is not the active window;
            // callers fall back to the in-app menu in that case.
            return elapsed >= 150;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not show the shell context menu");
            return false;
        }
        finally
        {
            if (hook is not null)
            {
                source?.RemoveHook(hook);
            }

            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }

            if (childArray != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(childArray);
            }

            foreach (IntPtr pidl in pidls)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    private static object? CreateContextMenu(
        IntPtr owner,
        IReadOnlyList<string> paths,
        List<IntPtr> pidls,
        out IntPtr childArray)
    {
        childArray = IntPtr.Zero;

        try
        {
            foreach (string path in paths)
            {
                if (SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
                {
                    pidls.Add(pidl);
                }
            }

            if (pidls.Count == 0)
            {
                return null;
            }

            Guid shellFolderId = typeof(IShellFolder).GUID;
            int bindResult = SHBindToParent(
                pidls[0],
                ref shellFolderId,
                out IShellFolder folder,
                out IntPtr firstChild);

            if (bindResult != 0 || folder is null)
            {
                AppLog.Write($"Shell menu: SHBindToParent 0x{bindResult:X8}");
                return null;
            }

            childArray = Marshal.AllocHGlobal(IntPtr.Size * pidls.Count);
            Marshal.WriteIntPtr(childArray, firstChild);

            for (int i = 1; i < pidls.Count; i++)
            {
                Marshal.WriteIntPtr(childArray, i * IntPtr.Size, ILFindLastID(pidls[i]));
            }

            Guid contextMenuId = typeof(IContextMenu).GUID;
            int objectResult = folder.GetUIObjectOf(
                owner,
                (uint)pidls.Count,
                childArray,
                ref contextMenuId,
                IntPtr.Zero,
                out IntPtr menuPointer);

            if (objectResult != 0 || menuPointer == IntPtr.Zero)
            {
                AppLog.Write($"Shell menu: GetUIObjectOf 0x{objectResult:X8}");
                return null;
            }

            try
            {
                return Marshal.GetObjectForIUnknown(menuPointer);
            }
            finally
            {
                Marshal.Release(menuPointer);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Shell menu: could not create the context menu");
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CmInvokeCommandInfoEx
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public NativePoint ptInvoke;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(
            IntPtr hwnd,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            ref uint pchEaten,
            out IntPtr ppidl,
            ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(
            IntPtr hwndOwner,
            uint cidl,
            IntPtr apidl,
            ref Guid riid,
            IntPtr rgfReserved,
            out IntPtr ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);

        [PreserveSig]
        int SetNameOf(
            IntPtr hwnd,
            IntPtr pidl,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            uint uFlags,
            out IntPtr ppidlOut);
    }

    [ComImport]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CmInvokeCommandInfoEx pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CmInvokeCommandInfoEx pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, int idCmdFirst, int idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CmInvokeCommandInfoEx pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr pidl,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv,
        out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hWnd,
        IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private static void ForceForeground(IntPtr window)
    {
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint currentThread = GetCurrentThreadId();

        bool attached = foregroundThread != 0 &&
                        foregroundThread != currentThread &&
                        AttachThreadInput(foregroundThread, currentThread, true);

        try
        {
            SetForegroundWindow(window);
            BringWindowToTop(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(foregroundThread, currentThread, false);
            }
        }
    }
}
