using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
                StatusText.Text = "ç›®å‰æ²¡æœ‰å¯å®‰è£…çš„æ­£å¼ç‰ˆæœ¬ã€‚";
                return;
            }

            StatusText.Text = $"æœ€æ–°ç‰ˆæœ¬ï¼š{_release.Version}";
            InstallButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"æ— æ³•èŽ·å–ç‰ˆæœ¬ä¿¡æ¯ï¼š{ex.Message}";
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
        StatusText.Text = $"æ­£åœ¨ä¸‹è½½ {_release.Version}â€¦";

        try
        {
            string path = InstallerCore.SetupPath(_release.Version);
            var progress = new Progress<double>(value =>
            {
                Progress.Value = value;
                StatusText.Text = $"æ­£åœ¨ä¸‹è½½ {_release.Version}â€¦ {(int)(value * 100)}%";
            });

            await InstallerCore.DownloadAsync(_client, _release.SetupUrl, path, progress);
            StatusText.Text = "æ­£åœ¨æ ¡éªŒå®‰è£…åŒ…â€¦";
            await InstallerCore.VerifyAsync(_client, _release, path);

            StatusText.Text = "æ­£åœ¨å®‰è£…â€¦";
            using Process setup = InstallerCore.StartSetup(path, silent: true);
            await setup.WaitForExitAsync();

            if (setup.ExitCode != 0)
            {
                StatusText.Text = $"å®‰è£…æœªå®Œæˆï¼ˆé€€å‡ºä»£ç  {setup.ExitCode}ï¼‰ã€‚";
                InstallButton.IsEnabled = true;
                return;
            }

            _installedPath = path;
            Progress.Value = 1;
            StatusText.Text = "å®‰è£…å®Œæˆã€‚";
            InstallButton.Content = "å¯åŠ¨ Desktop Drawer";
            InstallButton.IsEnabled = true;
            CloseButton.Content = "å…³é—­";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"å®‰è£…å¤±è´¥ï¼š{ex.Message}";
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

