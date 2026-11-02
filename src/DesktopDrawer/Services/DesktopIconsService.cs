using Microsoft.Win32;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;

namespace DesktopDrawer.Services;

public sealed class DesktopIconsService
{
    private const string AdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const int ToggleDesktopIconsCommand = 0x7402;
    private const uint GwChild = 5;

    public bool AreHidden()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AdvancedKeyPath);
            object? value = key?.GetValue("HideIcons");
            return value is not null && Convert.ToInt32(value) != 0;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not read desktop icon visibility");
            return false;
        }
    }

    public void SetHidden(bool hidden)
    {
        if (AreHidden() == hidden)
        {
            return;
        }

        try
        {
            IntPtr progman = NativeMethods.FindWindow("Progman", "Program Manager");
            if (progman != IntPtr.Zero)
            {
                IntPtr view = NativeMethods.GetWindow(progman, GwChild);
                IntPtr target = view != IntPtr.Zero ? view : progman;
                NativeMethods.SendMessage(target, NativeMethods.WmCommand, (IntPtr)ToggleDesktopIconsCommand, IntPtr.Zero);
                Thread.Sleep(120);
            }

            if (AreHidden() == hidden)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not toggle desktop icons through Explorer");
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AdvancedKeyPath, writable: true);
            key.SetValue("HideIcons", hidden ? 1 : 0, RegistryValueKind.DWord);
            NativeMethods.SHChangeNotify(
                NativeMethods.ShcneAssocChanged,
                NativeMethods.ShcnfIdList,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not update desktop icon visibility");
            throw new InvalidOperationException("Windows did not allow the desktop icon setting to change.", ex);
        }
    }
}
