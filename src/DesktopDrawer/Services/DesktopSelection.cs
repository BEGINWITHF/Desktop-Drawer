using System.IO;
using System.Runtime.InteropServices;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Selects a file in Explorer's desktop view through the shell's own COM API.
/// This is the same selection a user would make with the mouse: tools that read
/// the desktop selection (PowerToys Peek, shell extensions...) see it, and unlike
/// direct list view messages it is safe across processes.
/// </summary>
internal static class DesktopSelection
{
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;

    private const uint SvgioAllView = 2;
    private const uint SigdnNormalDisplay = 0x00000000;
    private const uint SigdnParentRelativeEditing = 0x80031001;

    private const uint SvsiSelect = 0x0001;
    private const uint SvsiDeselectOthers = 0x0004;
    private const uint SvsiFocused = 0x0010;

    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidSTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    public static bool TrySelect(string path)
    {
        string fullName = Path.GetFileName(path);
        string displayName = Path.GetFileNameWithoutExtension(path);

        try
        {
            IFolderView? view = GetDesktopView();
            if (view is null)
            {
                AppLog.Write("Forward: the desktop view is not available");
                return false;
            }

            view.ItemCount(SvgioAllView, out int count);

            Guid iidArray = typeof(IShellItemArray).GUID;
            int result = view.Items(SvgioAllView, ref iidArray, out IntPtr arrayPointer);
            if (result != 0 || arrayPointer == IntPtr.Zero)
            {
                AppLog.Write($"Forward: the desktop items could not be read (0x{result:X8})");
                return false;
            }

            var items = (IShellItemArray)Marshal.GetObjectForIUnknown(arrayPointer);
            Marshal.Release(arrayPointer);

            for (uint index = 0; index < count; index++)
            {
                if (items.GetItemAt(index, out IntPtr itemPointer) != 0 || itemPointer == IntPtr.Zero)
                {
                    continue;
                }

                var item = (IShellItem)Marshal.GetObjectForIUnknown(itemPointer);
                Marshal.Release(itemPointer);

                string editing = GetName(item, SigdnParentRelativeEditing);
                string display = GetName(item, SigdnNormalDisplay);

                bool match =
                    string.Equals(editing, fullName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(display, fullName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(editing, displayName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(display, displayName, StringComparison.OrdinalIgnoreCase);

                if (!match)
                {
                    continue;
                }

                view.SelectItem((int)index, SvsiSelect | SvsiFocused | SvsiDeselectOthers);
                return true;
            }

            AppLog.Write($"Forward: \"{fullName}\" was not found in the desktop view");
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not select the desktop icon");
            return false;
        }
    }

    private static string GetName(IShellItem item, uint form)
    {
        if (item.GetDisplayName(form, out IntPtr pointer) != 0 || pointer == IntPtr.Zero)
        {
            return string.Empty;
        }

        string name = Marshal.PtrToStringUni(pointer) ?? string.Empty;
        Marshal.FreeCoTaskMem(pointer);
        return name;
    }

    private static IFolderView? GetDesktopView()
    {
        Guid clsid = ClsidShellWindows;
        Guid iid = typeof(IShellWindows).GUID;
        if (CoCreateInstance(ref clsid, IntPtr.Zero, 0x17, ref iid, out object shellWindowsObject) != 0)
        {
            return null;
        }

        var shellWindows = (IShellWindows)shellWindowsObject;
        object missing = null!;
        shellWindows.FindWindowSW(ref missing, ref missing, SwcDesktop, out _, SwfoNeedDispatch, out object dispatch);

        var provider = (IServiceProvider)dispatch;
        Guid service = SidSTopLevelBrowser;
        Guid iidBrowser = typeof(IShellBrowser).GUID;
        if (provider.QueryService(ref service, ref iidBrowser, out object browserObject) != 0)
        {
            return null;
        }

        var browser = (IShellBrowser)browserObject;
        browser.QueryActiveShellView(out object viewObject);
        return viewObject as IFolderView;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr outer,
        uint context,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object instance);

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        // IDispatch slots: ShellWindows is a dual interface.
        void GetTypeInfoCount(out uint count);
        void GetTypeInfo(uint typeInfo, uint locale, out IntPtr info);
        void GetIDsOfNames(ref Guid iid, IntPtr names, uint count, uint locale, IntPtr ids);
        void Invoke(int id, ref Guid iid, uint locale, ushort flags, IntPtr parameters, out IntPtr result, IntPtr exceptionInfo, IntPtr argumentError);

        void GetCount(out int count);
        void Item(object index, out object folder);
        void NewEnum(out IntPtr enumVar);
        void Register(object pid, IntPtr hwnd, int swClass, out int cookie);
        void RegisterPending(int threadId, IntPtr location, IntPtr root, int swClass, out int cookie);
        void Revoke(int cookie);
        void OnNavigate(int cookie, IntPtr location);
        void OnActivated(int cookie, bool active);
        void FindWindowSW(ref object location, ref object root, int swClass, out int hwnd, int options, [MarshalAs(UnmanagedType.Interface)] out object dispatch);
        void OnCreated(int cookie);
        void ProcessAttachDetach(bool attach);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object instance);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(bool enter);
        void InsertMenusSB(IntPtr sharedMenu, IntPtr menuWidths);
        void SetMenuSB(IntPtr sharedMenu, IntPtr holeMenu, IntPtr activeObject);
        void RemoveMenusSB(IntPtr sharedMenu);
        void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        void EnableModelessSB(bool enable);
        void TranslateAcceleratorSB(IntPtr message, ushort id);
        void BrowseObject(IntPtr pidl, uint flags);
        void GetViewStateStream(uint mode, out IntPtr stream);
        void GetControlWindow(uint id, out IntPtr hwnd);
        void SendControlMsg(uint id, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
        void OnViewWindowActive(IntPtr view);
        void SetToolbarItems(IntPtr buttons, uint count, uint flags);
    }

    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode(out uint mode);
        void SetCurrentViewMode(uint mode);
        void GetFolder(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder);
        void Item(int index, out IntPtr pidl);
        void ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid iid, out IntPtr items);
        void GetSelectionMarkedItem(out int index);
        void GetFocusedItem(out int index);
        void GetItemPosition(IntPtr pidl, out Point point);
        void GetSpacing(out Point point);
        void GetDefaultSpacing(out Point point);
        void GetAutoArrange();
        void SelectItem(int index, uint flags);
        void SelectAndPositionItems(uint count, IntPtr pidls, IntPtr points, uint flags);
    }

    [ComImport]
    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetPropertyStore(uint flags, ref Guid iid, out IntPtr store);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid iid, out IntPtr list);
        void GetAttributes(uint flags, uint mask, out uint attributes);
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemAt(uint index, out IntPtr item);
        void EnumItems(out IntPtr enumerator);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint form, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem item, uint hint, out int order);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
