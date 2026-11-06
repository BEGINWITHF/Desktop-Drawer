using System.Windows;
using System.Windows.Media;
using DesktopDrawer.Helpers;
using DesktopDrawer.Services;

namespace DesktopDrawer;

public partial class MainWindow
{
    private bool _updateInProgress;

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInProgress)
        {
            return;
        }

        _updateInProgress = true;
        UpdateButton.IsEnabled = false;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateStatusText.Foreground = (Brush)FindResource("TextMutedBrush");
        UpdateStatusText.Text = I18n.T("About.Checking");

        try
        {
            UpdateCheckResult check = await UpdateService.CheckAsync();

            if (check.Status == UpdateCheckStatus.UpToDate)
            {
                UpdateStatusText.Text = I18n.T("About.UpToDate");
                return;
            }

            if (check.Status == UpdateCheckStatus.NoPackage)
            {
                UpdateStatusText.Text = I18n.T("About.UpdateUnavailable");
                return;
            }

            UpdatePackage package = check.Package!;
            UpdateStatusText.Text = I18n.F("About.Downloading", package.Version);

            var progress = new Progress<double>(value =>
            {
                UpdateStatusText.Text = I18n.F(
                    "About.DownloadProgress",
                    (int)Math.Round(value * 100));
            });

            StagedUpdate staged = await UpdateService.DownloadAndStageAsync(package, progress);

            UpdateStatusText.Text = I18n.F("About.Installing", package.Version);
            UpdateService.LaunchInstaller(staged);

            // Give the helper a moment to start before this process disappears.
            await Task.Delay(500);
            ExitApplication();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Update failed");
            UpdateStatusText.Foreground = (Brush)FindResource("DangerBrush");
            UpdateStatusText.Text = I18n.T("About.UpdateFailed");
        }
        finally
        {
            _updateInProgress = false;

            if (!_isExiting)
            {
                UpdateButton.IsEnabled = true;
            }
        }
    }
}
