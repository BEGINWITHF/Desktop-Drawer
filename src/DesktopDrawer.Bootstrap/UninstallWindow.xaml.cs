using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DesktopDrawer.Bootstrap;

public partial class UninstallWindow : Window
{
    private bool _removing;

    public UninstallWindow() => InitializeComponent();

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_removing)
        {
            return;
        }

        _removing = true;
        UninstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        string appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopDrawer");

        try
        {
            StatusText.Text = "正在关闭 Desktop Drawer…";
            await Task.Run(() =>
            {
                foreach (Process process in Process.GetProcessesByName("DesktopDrawer"))
                {
                    try { process.Kill(entireProcessTree: true); process.WaitForExit(3000); } catch { }
                }
            });

            Progress.Value = 0.35;
            StatusText.Text = "正在删除程序文件…";

            await Task.Run(() =>
            {
                using RegistryKey? run = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                run?.DeleteValue("DesktopDrawer", throwOnMissingValue: false);

                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopDrawer",
                    throwOnMissingSubKey: false);
            });

            Progress.Value = 0.6;
            StatusText.Text = "正在删除设置…";

            if (KeepSettings.IsChecked != true)
            {
                string settings = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DesktopDrawer");

                await Task.Run(() => TryDeleteDirectory(settings));
            }

            Progress.Value = 0.85;
            StatusText.Text = "正在清理…";

            // The running exe cannot delete its own folder, so let a detached
            // command wait for this process to exit and remove what is left.
            string exe = Environment.ProcessPath ?? string.Empty;

            await Task.Run(() =>
            {
                string args = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{appDir}\"";

                Process.Start(new ProcessStartInfo("cmd.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            });

            Progress.Value = 1;
            StatusText.Text = "卸载完成";
            MetaText.Text = "如果安装目录仍有残留，几秒后会自动清理。";
            UninstallButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "关闭";
            CancelButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"卸载失败：{ex.Message}";
            UninstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
        finally
        {
            _removing = false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort: locked files are skipped.
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
