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
        ApplyLanguage();
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
                StatusText.Text = Lang.T("noRelease");
                return;
            }

            StatusText.Text = Lang.F("found", _release.Version);
            MetaText.Text = Lang.F("willInstall", _release.Version);
            InstallButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.F("noInfo", ex.Message);
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
        StatusText.Text = Lang.T("downloading");

        try
        {
            string path = InstallerCore.SetupPath(_release.Version);
            var progress = new Progress<double>(value =>
            {
                Progress.Value = value;
                MetaText.Text = Lang.F("downloaded", (int)(value * 100));
            });

            await InstallerCore.DownloadAsync(_client, _release.SetupUrl, path, progress);
            StatusText.Text = Lang.T("verifying");
            await InstallerCore.VerifyAsync(_client, _release, path);

            StatusText.Text = Lang.T("installing");
            using Process setup = InstallerCore.StartSetup(path, silent: true);
            await setup.WaitForExitAsync();

            if (setup.ExitCode != 0)
            {
                StatusText.Text = Lang.F("code", setup.ExitCode);
                InstallButton.IsEnabled = true;
                return;
            }

            _installedPath = path;
            Progress.Value = 1;
            StatusText.Text = Lang.T("installed");
            MetaText.Text = Lang.T("installedNote");
            InstallButton.Content = Lang.T("launch");
            InstallButton.IsEnabled = true;
            CloseButton.Content = Lang.T("close");
        }
        catch (Exception ex)
        {
            StatusText.Text = Lang.F("failed", ex.Message);
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

    private void ApplyLanguage()
    {
        SubtitleText.Text = Lang.T("subtitle");
        CloseButton.Content = Lang.T("cancel");
        LangButton.Content = Lang.Current switch
        {
            "zh-CN" => "简",
            "zh-TW" => "繁",
            "ja" => "日",
            _ => "EN"
        };

        if (_installedPath is not null)
        {
            InstallButton.Content = Lang.T("launch");
            CloseButton.Content = Lang.T("close");
        }
        else
        {
            InstallButton.Content = Lang.T("install");
        }
    }

    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        int index = Array.IndexOf(Lang.Options, Lang.Current);
        Lang.Set(Lang.Options[(index + 1) % Lang.Options.Length]);
        ApplyLanguage();

        if (!_busy && _installedPath is null)
        {
            StatusText.Text = Lang.T("checking");
            MetaText.Text = " ";
            _ = LoadLatestAsync();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
