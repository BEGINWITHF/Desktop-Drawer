using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using DesktopDrawer.Helpers;
using DesktopDrawer.Models;
using DesktopDrawer.Services;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DesktopDrawer;

public partial class MainWindow
{
    private WinForms.ContextMenuStrip? _trayMenu;
    private Drawing.Icon? _trayIconImage;

    private static readonly string[] AccentPresets =
    {
        "#7C9CFF",
        "#8B7CF6",
        "#4CC9F0",
        "#34D399",
        "#F6C453",
        "#FF8A65",
        "#F472B6",
        "#9AA4B8"
    };

    private readonly List<(Border Swatch, string Hex)> _accentSwatches = new();

    private void BuildAccentSwatches()
    {
        AccentSwatchesPanel.Children.Clear();
        _accentSwatches.Clear();

        foreach (string hex in AccentPresets)
        {
            ThemeService.TryParse(hex, out Color color);

            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = hex
            };

            AutomationProperties.SetName(swatch, $"Accent {hex}");
            swatch.MouseLeftButtonDown += AccentSwatch_Click;
            _accentSwatches.Add((swatch, hex));
            AccentSwatchesPanel.Children.Add(swatch);
        }

        UpdateAccentSelection();
    }

    private void AccentSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { ToolTip: string hex })
        {
            ApplyAccent(hex, save: true);
            e.Handled = true;
        }
    }

    private void AccentHexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyCustomAccent();
        e.Handled = true;
    }

    private void AccentHexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyCustomAccent();

    private void AccentApplyButton_Click(object sender, RoutedEventArgs e) => ApplyCustomAccent();

    private void ApplyCustomAccent()
    {
        if (_initializingSettings)
        {
            return;
        }

        if (ThemeService.TryParse(AccentHexBox.Text, out _))
        {
            ApplyAccent(AccentHexBox.Text, save: true);
            return;
        }

        AccentHexBox.Text = CurrentAccentHex;
        ShowToast("Enter a color like #4CC9F0");
    }

    private void ApplyAccent(string hex, bool save)
    {
        Color accent = ThemeService.ApplyAccent(hex);
        _settings.AccentColor = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";

        _initializingSettings = true;
        AccentHexBox.Text = _settings.AccentColor;
        _initializingSettings = false;

        UpdateAccentSelection();

        if (save)
        {
            SaveSettings();
        }
    }

    private string CurrentAccentHex =>
        ThemeService.TryParse(_settings.AccentColor, out Color color)
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : ThemeService.DefaultAccent;

    private void UpdateAccentSelection()
    {
        string current = CurrentAccentHex;

        foreach ((Border swatch, string hex) in _accentSwatches)
        {
            bool selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = selected ? (Brush)FindResource("TextPrimaryBrush") : Brushes.Transparent;
        }
    }

    private void AlwaysShowChromeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
        {
            return;
        }

        _settings.AlwaysShowChrome = AlwaysShowChromeToggle.IsChecked == true;
        SaveSettings();

        if (_settings.AlwaysShowChrome)
        {
            ShowChrome();
        }
    }

    private void WallpaperDimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializingSettings || WallpaperDimValueText is null)
        {
            return;
        }

        _settings.WallpaperDim = (int)Math.Round(e.NewValue);
        WallpaperDimValueText.Text = $"{_settings.WallpaperDim}%";
        ApplyWallpaperDim();
        SaveSettings();
    }

    private void ApplyWallpaperDim()
    {
        WallpaperDimLayer.Opacity = Math.Clamp(_settings.WallpaperDim, 0, 60) / 100.0;
    }

    private void SyncSettingsUi()
    {
        bool startupEnabled = _startupService.IsEnabled();
        if (startupEnabled != _settings.StartWithWindows)
        {
            _settings.StartWithWindows = startupEnabled;
            SaveSettings();
        }

        CompactRadio.IsChecked = _settings.Density == TileDensity.Compact;
        CozyRadio.IsChecked = _settings.Density == TileDensity.Cozy;
        LargeRadio.IsChecked = _settings.Density == TileDensity.Large;

        IconSizeSlider.Value = _settings.IconSize > 0
            ? Math.Clamp(_settings.IconSize, 24, 160)
            : _settings.Density switch
            {
                TileDensity.Compact => 44,
                TileDensity.Large => 96,
                _ => 72
            };
        IconSizeValueText.Text = $"{IconSizeSlider.Value:0} px";

        bool autoGrid = _settings.GridColumns <= 0 || _settings.GridRows <= 0;
        AutoGridToggle.IsChecked = autoGrid;
        ColumnsSlider.IsEnabled = !autoGrid;
        RowsSlider.IsEnabled = !autoGrid;
        ColumnsSlider.Value = autoGrid ? Math.Max(2, Columns) : Math.Clamp(_settings.GridColumns, 2, 16);
        RowsSlider.Value = autoGrid ? Math.Max(1, Rows) : Math.Clamp(_settings.GridRows, 1, 16);
        ColumnsValueText.Text = ((int)Math.Round(ColumnsSlider.Value)).ToString();
        RowsValueText.Text = ((int)Math.Round(RowsSlider.Value)).ToString();

        FoldersFirstToggle.IsChecked = _settings.FoldersFirst;
        ShowHiddenToggle.IsChecked = _settings.ShowHidden;
        CloseToTrayToggle.IsChecked = _settings.CloseToTray;
        AlwaysShowChromeToggle.IsChecked = _settings.AlwaysShowChrome;
        StartWithWindowsToggle.IsChecked = _settings.StartWithWindows;
        HideIconsOnLaunchToggle.IsChecked = _settings.HideIconsOnLaunch;
        HideIconsSettingsToggle.IsChecked = _desktopIconsService.AreHidden();
        LayoutEditToggle.IsChecked = _layoutEditing;

        AccentHexBox.Text = CurrentAccentHex;
        WallpaperDimSlider.Value = Math.Clamp(_settings.WallpaperDim, 0, 60);
        WallpaperDimValueText.Text = $"{(int)WallpaperDimSlider.Value}%";
        UpdateAccentSelection();
        ApplyWallpaperDim();

        UpdateSortLabel();
        CloseButton.ToolTip = _settings.CloseToTray ? "Close to tray" : "Close";
    }

    private void DensityRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings || sender is not RadioButton { IsChecked: true } radio)
        {
            return;
        }

        if (!Enum.TryParse(radio.Tag?.ToString(), ignoreCase: true, out TileDensity density) ||
            density == _settings.Density)
        {
            return;
        }

        _settings.IconSize = 0;
        ApplyDensity(density);
        SaveSettings();
        RecalculateGrid(force: true);

        _initializingSettings = true;
        IconSizeSlider.Value = _requestedIconSize;
        IconSizeValueText.Text = $"{_requestedIconSize:0} px";
        _initializingSettings = false;
    }

    private void IconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializingSettings || IconSizeValueText is null)
        {
            return;
        }

        _settings.IconSize = (int)Math.Round(e.NewValue);
        IconSizeValueText.Text = $"{_settings.IconSize} px";
        ApplyDensity(_settings.Density);
        SaveSettings();
        RecalculateGrid(force: true);
    }

    private void AutoGridToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
        {
            return;
        }

        bool auto = AutoGridToggle.IsChecked == true;
        _settings.GridColumns = auto ? 0 : (int)Math.Round(ColumnsSlider.Value);
        _settings.GridRows = auto ? 0 : (int)Math.Round(RowsSlider.Value);
        ColumnsSlider.IsEnabled = !auto;
        RowsSlider.IsEnabled = !auto;
        SaveSettings();
        RecalculateGrid(force: true);
    }

    private void GridSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializingSettings || ColumnsValueText is null || RowsValueText is null)
        {
            return;
        }

        ColumnsValueText.Text = ((int)Math.Round(ColumnsSlider.Value)).ToString();
        RowsValueText.Text = ((int)Math.Round(RowsSlider.Value)).ToString();

        if (AutoGridToggle.IsChecked == true)
        {
            return;
        }

        _settings.GridColumns = (int)Math.Round(ColumnsSlider.Value);
        _settings.GridRows = (int)Math.Round(RowsSlider.Value);
        SaveSettings();
        RecalculateGrid(force: true);
    }

    private void FoldersFirstToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
        {
            return;
        }

        _settings.FoldersFirst = FoldersFirstToggle.IsChecked == true;
        SaveSettings();

        if (_hasLoadedOnce)
        {
            RebuildFilteredItems();
            _pageIndex = 0;
            UpdatePage();
        }
    }

    private void ShowHiddenToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
        {
            return;
        }

        _settings.ShowHidden = ShowHiddenToggle.IsChecked == true;
        SaveSettings();

        if (_hasLoadedOnce)
        {
            _ = RefreshAsync(resetPage: true);
        }
    }

    private void CloseToTrayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
        {
            return;
        }

        _settings.CloseToTray = CloseToTrayToggle.IsChecked == true;
        SaveSettings();
        CloseButton.ToolTip = _settings.CloseToTray ? "Close to tray" : "Close";
    }

    private void StartWithWindowsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
        {
            return;
        }

        SetStartWithWindows(StartWithWindowsToggle.IsChecked == true);
    }

    private void SetStartWithWindows(bool enabled)
    {
        if (_settings.StartWithWindows == enabled &&
            _startupService.IsEnabled() == enabled)
        {
            return;
        }

        if (!_startupService.SetEnabled(enabled))
        {
            ShowToast("Could not update the Windows startup entry. Run Desktop Drawer from its published .exe to use this.");
            _initializingSettings = true;
            StartWithWindowsToggle.IsChecked = _settings.StartWithWindows;
            _initializingSettings = false;
            return;
        }

        _settings.StartWithWindows = enabled;
        SaveSettings();

        _initializingSettings = true;
        StartWithWindowsToggle.IsChecked = enabled;
        _initializingSettings = false;

        ShowToast(enabled
            ? "Desktop Drawer will start with Windows."
            : "Desktop Drawer will no longer start with Windows.");
    }

    private void HideIconsToggle_Click(object sender, RoutedEventArgs e)
    {
        SetDesktopIconsHidden(HideIconsToggle.IsChecked == true);
    }

    private void LayoutEditToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
        {
            return;
        }

        SetLayoutEditing(LayoutEditToggle.IsChecked == true);
    }

    private void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Layout.Clear();
        SaveSettings();
        ApplyChromeLayout();
        ShowToast("Layout positions reset.");
    }

    private void HideIconsSettingsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings || _syncingIconToggles)
        {
            return;
        }

        SetDesktopIconsHidden(HideIconsSettingsToggle.IsChecked == true);
    }

    private void HideIconsOnLaunchToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
        {
            return;
        }

        _settings.HideIconsOnLaunch = HideIconsOnLaunchToggle.IsChecked == true;
        SaveSettings();
    }

    private void SetDesktopIconsHidden(bool hidden)
    {
        try
        {
            _desktopIconsService.SetHidden(hidden);
            _desktopIconsChanged = _desktopIconsService.AreHidden() != _initialIconsHidden;
            UpdateDesktopIconsUi();
            ShowToast(hidden ? "Windows desktop icons hidden" : "Windows desktop icons restored");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not change desktop icon visibility");
            UpdateDesktopIconsUi();
            ShowToast(ex is InvalidOperationException ? ex.Message : "Could not change the Windows desktop icon setting.");
        }
    }

    private void UpdateDesktopIconsUi()
    {
        if (_cleanedUp)
        {
            return;
        }

        _syncingIconToggles = true;
        try
        {
            bool hidden = _desktopIconsService.AreHidden();
            HideIconsToggle.IsChecked = hidden;
            HideIconsSettingsToggle.IsChecked = hidden;
            HideIconsToggle.ToolTip = hidden
                ? "Show the Windows desktop icons"
                : "Hide the Windows desktop icons";
        }
        finally
        {
            _syncingIconToggles = false;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsOpen)
        {
            CloseSettings();
        }
        else
        {
            OpenSettings();
        }
    }

    private void SettingsCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSettings();
    }

    private void SettingsBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        CloseSettings();
    }

    private void OpenSettings()
    {
        _initializingSettings = true;
        SyncSettingsUi();
        _initializingSettings = false;

        _settingsOpen = true;
        ShowChrome();
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsOverlay.BeginAnimation(OpacityProperty, null);
        SettingsOverlay.Opacity = 0;
        SettingsTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SettingsTranslate.X = 404;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        var slide = new DoubleAnimation(404, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        SettingsOverlay.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        SettingsTranslate.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }

    private void CloseSettings()
    {
        if (!_settingsOpen)
        {
            return;
        }

        _settingsOpen = false;
        var slide = new DoubleAnimation(0, 404, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            if (!_settingsOpen)
            {
                SettingsOverlay.Visibility = Visibility.Collapsed;
                SettingsTranslate.X = 404;
            }
        };

        SettingsTranslate.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
        SettingsOverlay.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        PageList.Focus();
        ShowChrome();
    }

    private void RecordTouchpadButton_Click(object sender, RoutedEventArgs e)
    {
        InputDiagnostics.Start(20);
        ShowToast($"Recording input for 20 seconds - swipe now.\nLog: {InputDiagnostics.LogPath}");
    }

    private void OpenDiagnosticFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "explorer.exe",
                $"/select,\"{InputDiagnostics.LogPath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not open the diagnostic log");
        }
    }

    private void OpenDesktopFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_desktopService.UserDesktopPath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not open the desktop folder");
            ShowToast("Could not open the desktop folder.");
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_settingsService.SettingsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settingsService.SettingsDirectory}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not open the data folder");
            ShowToast("Could not open the data folder.");
        }
    }

    private void RefreshIconsButton_Click(object sender, RoutedEventArgs e)
    {
        _iconService.Clear();

        foreach (DesktopItem item in _allItems)
        {
            item.Icon = null;
        }

        UpdatePage();
        StartPrefetch();
        ShowToast("Icons refreshed");
    }

    private void ExitApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void InitializeTray()
    {
        try
        {
            using Stream? iconStream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute))?.Stream;

            _trayIconImage = iconStream is not null
                ? new Drawing.Icon(iconStream)
                : Drawing.SystemIcons.Application;

            _trayMenu = new WinForms.ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = WinForms.SystemInformation.MenuFont
            };

            var openItem = new WinForms.ToolStripMenuItem("Open Desktop Drawer")
            {
                Font = new Drawing.Font(WinForms.SystemInformation.MenuFont, Drawing.FontStyle.Bold)
            };
            openItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));

            _trayHideIconsItem = new WinForms.ToolStripMenuItem("Hide Windows desktop icons");
            _trayHideIconsItem.Click += (_, _) => SetDesktopIconsHidden(!_desktopIconsService.AreHidden());

            _trayStartupItem = new WinForms.ToolStripMenuItem("Start with Windows");
            _trayStartupItem.Click += (_, _) => SetStartWithWindows(!_settings.StartWithWindows);

            var openDesktopItem = new WinForms.ToolStripMenuItem("Open Desktop folder");
            openDesktopItem.Click += (_, _) => OpenDesktopFolderButton_Click(this, new RoutedEventArgs());

            var exitItem = new WinForms.ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication));

            _trayMenu.Items.Add(openItem);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add(_trayHideIconsItem);
            _trayMenu.Items.Add(_trayStartupItem);
            _trayMenu.Items.Add(openDesktopItem);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);

            _trayMenu.Opening += (_, _) =>
            {
                if (_trayHideIconsItem is not null)
                {
                    _trayHideIconsItem.Checked = _desktopIconsService.AreHidden();
                }

                if (_trayStartupItem is not null)
                {
                    _trayStartupItem.Checked = _settings.StartWithWindows;
                }
            };

            _trayIcon = new WinForms.NotifyIcon
            {
                Icon = _trayIconImage,
                Text = "Desktop Drawer",
                Visible = true,
                ContextMenuStrip = _trayMenu
            };

            _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
            UpdateTrayText();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not create the notification icon");
        }
    }

    public void ShowFromTray()
    {
        if (_isExiting || _cleanedUp)
        {
            return;
        }

        Show();

        if (_iconsRestoredForTray)
        {
            _iconsRestoredForTray = false;
            try
            {
                _desktopIconsService.SetHidden(true);
                UpdateDesktopIconsUi();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Could not hide desktop icons after reopening");
            }
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!_embeddedInDesktop)
        {
            Activate();
            Topmost = true;
            Topmost = false;
            PageList.Focus();
        }
    }

    private void HideToTray()
    {
        Hide();

        // The overlay is gone, so hand the native desktop back instead of leaving
        // it empty behind a hidden window.
        try
        {
            if (_desktopIconsService.AreHidden())
            {
                _desktopIconsService.SetHidden(false);
                _iconsRestoredForTray = true;
                UpdateDesktopIconsUi();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not restore desktop icons while hiding to the tray");
        }

        if (!_trayHintShown && _trayIcon is not null)
        {
            _trayHintShown = true;
            _trayIcon.ShowBalloonTip(
                3000,
                "Desktop Drawer is still running",
                "Double-click the notification icon to reopen it.",
                WinForms.ToolTipIcon.Info);
        }
    }

    private void DisposeTray()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }
}
