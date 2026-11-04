using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

/// <summary>
/// Selects a file in Explorer's desktop icon list. The list view lives in the
/// Explorer process, so its structures and text buffers have to be written into
/// that process before sending the list view messages; otherwise Explorer would
/// read a pointer of ours and crash. This changes the same selection a mouse
/// click would, which is what tools like PowerToys Peek read.
/// </summary>
internal static class DesktopSelection
{
    private const uint LvmFirst = 0x1000;
    private const uint LvmGetItemCount = LvmFirst + 4;
    private const uint LvmSetItemState = LvmFirst + 43;
    private const uint LvmGetItemText = LvmFirst + 115;

    private const uint LvisSelected = 0x0002;
    private const uint LvisFocused = 0x0001;

    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;
    private const uint MemRelease = 0x8000;

    public static bool TrySelect(IntPtr list, string path)
    {
        if (list == IntPtr.Zero)
        {
            return false;
        }

        string fullName = Path.GetFileName(path);
        string displayName = Path.GetFileNameWithoutExtension(path);

        IntPtr process = IntPtr.Zero;
        IntPtr remote = IntPtr.Zero;

        try
        {
            GetWindowThreadProcessId(list, out uint processId);
            process = OpenProcess(
                ProcessVmOperation | ProcessVmWrite | ProcessVmRead | ProcessQueryInformation,
                false,
                processId);

            if (process == IntPtr.Zero)
            {
                AppLog.Write($"Forward: could not open Explorer (error {Marshal.GetLastWin32Error()})");
                return false;
            }

            remote = VirtualAllocEx(process, IntPtr.Zero, 4096, MemCommit | MemReserve, PageReadWrite);
            if (remote == IntPtr.Zero)
            {
                AppLog.Write("Forward: could not allocate memory in Explorer");
                return false;
            }

            IntPtr itemAddress = remote;
            IntPtr textAddress = remote + 1024;

            int count = (int)SendMessage(list, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero);
            int exact = -1;
            int loose = -1;
            int looseCount = 0;

            for (int index = 0; index < count; index++)
            {
                string text = ReadItemText(process, list, itemAddress, textAddress, index);

                if (string.Equals(text, fullName, StringComparison.OrdinalIgnoreCase))
                {
                    exact = index;
                    break;
                }

                if (string.Equals(text, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    loose = index;
                    looseCount++;
                }
            }

            // When extensions are hidden the list only holds the display name, so
            // the fallback is used only if exactly one item carries that name.
            int target = exact >= 0 ? exact : looseCount == 1 ? loose : -1;

            if (target < 0)
            {
                AppLog.Write(looseCount == 0
                    ? $"Forward: \"{fullName}\" was not found on the desktop"
                    : $"Forward: \"{fullName}\" is ambiguous on the desktop; selection left unchanged");
                return false;
            }

            SelectItem(process, list, itemAddress, target);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not select the desktop icon");
            return false;
        }
        finally
        {
            if (remote != IntPtr.Zero)
            {
                VirtualFreeEx(process, remote, IntPtr.Zero, MemRelease);
            }

            if (process != IntPtr.Zero)
            {
                CloseHandle(process);
            }
        }
    }

    private static string ReadItemText(
        IntPtr process,
        IntPtr list,
        IntPtr itemAddress,
        IntPtr textAddress,
        int index)
    {
        var item = new LvItem
        {
            iSubItem = 0,
            pszText = textAddress,
            cchTextMax = 260
        };

        WriteStruct(process, itemAddress, item);

        int length = (int)SendMessage(list, LvmGetItemText, (IntPtr)index, itemAddress);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[Math.Min(length, 259) * 2];
        return ReadProcessMemory(process, textAddress, buffer, (uint)buffer.Length, out _)
            ? Encoding.Unicode.GetString(buffer)
            : string.Empty;
    }

    private static void SelectItem(IntPtr process, IntPtr list, IntPtr itemAddress, int index)
    {
        var clear = new LvItem { stateMask = LvisSelected | LvisFocused, state = 0 };
        WriteStruct(process, itemAddress, clear);
        SendMessage(list, LvmSetItemState, new IntPtr(-1), itemAddress);

        var select = new LvItem
        {
            stateMask = LvisSelected | LvisFocused,
            state = LvisSelected | LvisFocused
        };

        WriteStruct(process, itemAddress, select);
        SendMessage(list, LvmSetItemState, (IntPtr)index, itemAddress);
    }

    private static void WriteStruct(IntPtr process, IntPtr address, object value)
    {
        int size = Marshal.SizeOf(value);
        IntPtr local = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(value, local, false);
            var buffer = new byte[size];
            Marshal.Copy(local, buffer, 0, size);
            WriteProcessMemory(process, address, buffer, (uint)size, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(local);
        }
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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, IntPtr size, uint type, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, IntPtr size, uint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, uint size, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, uint size, out IntPtr read);
}
