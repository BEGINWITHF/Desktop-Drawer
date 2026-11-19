using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace DesktopDrawer.Bootstrap;

public partial class MainWindow : Window
{
    private readonly HttpClient _client = InstallerCore.CreateClient();
    private ReleaseInfo? _release;
    private string? _installedPath;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadLatestAsync();
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async Task LoadLatestAsync()
    {
        try
        {
            _release = await InstallerCore.FindLatestAsync(_client, allowPrerelease: false);

            if (_release is null)
            {
                StatusText.Text = "目前没有可安装的正式版本。";
                return;
            }

            StatusText.Text = $"发现新版本 {_release.Version}";
            MetaText.Text = $"将下载并安装 Desktop Drawer {_release.Version}";
            InstallButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"无法获取版本信息：{ex.Message}";
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_installedPath is not null)
        {
            LaunchApp();
            return;
        }

        if (_release is null)
        {
            return;
        }

        _busy = true;
        InstallButton.IsEnabled = false;
        StatusText.Text = "正在下载…";

        try
        {
            string path = InstallerCore.SetupPath(_release.Version);
            var progress = new Progress<double>(value =>
            {
                Progress.Value = value;
                MetaText.Text = $"已下载 {(int)(value * 100)}%";
            });

            await InstallerCore.DownloadAsync(_client, _release.SetupUrl, path, progress);
            StatusText.Text = "正在校验安装包…";
            await InstallerCore.VerifyAsync(_client, _release, path);

            StatusText.Text = "正在安装…";
            using Process setup = InstallerCore.StartSetup(path, silent: true);
            await setup.WaitForExitAsync();

            if (setup.ExitCode != 0)
            {
                StatusText.Text = $"安装未完成（退出代码 {setup.ExitCode}）。";
                InstallButton.IsEnabled = true;
                return;
            }

            _installedPath = path;
            Progress.Value = 1;
            StatusText.Text = "安装完成";
            MetaText.Text = "Desktop Drawer 已是最新版本";
            InstallButton.Content = "启动";
            InstallButton.IsEnabled = true;
            CloseButton.Content = "关闭";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"安装失败：{ex.Message}";
            InstallButton.IsEnabled = true;
        }
        finally
        {
            _busy = false;
        }
    }

    private static void LaunchApp()
    {
        string exe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopDrawer",
            "DesktopDrawer.exe");

        if (File.Exists(exe))
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }

        Application.Current.Shutdown();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
