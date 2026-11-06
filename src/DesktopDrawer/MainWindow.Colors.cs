using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopDrawer.Models;
using DesktopDrawer.Services;

namespace DesktopDrawer;

public partial class MainWindow
{
    /// <summary>Standard RGB palette shown above the channel sliders.</summary>
    private static readonly string[] StandardColors =
    {
        "#000000", "#1A1A1A", "#3A3A3A", "#555555", "#737373", "#9E9E9E", "#C7C7C7", "#FFFFFF",
        "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#3949AB", "#8E24AA", "#D81B60",
        "#FFCDD2", "#FFE0B2", "#FFF9C4", "#C8E6C9", "#BBDEFB", "#C5CAE9", "#E1BEE7", "#F8BBD0",
        "#B71C1C", "#E65100", "#F57F17", "#1B5E20", "#0D47A1", "#1A237E", "#4A148C", "#880E4F"
    };

    private readonly List<(Border Swatch, string Hex)> _paletteSwatches = new();
    private readonly Dictionary<string, (Border Swatch, TextBlock HexLabel)> _colorRows =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _editingColorKey;
    private Dictionary<string, string>? _colorEditBackup;
    private bool _updatingColorPicker;
    private Color _pendingColor = ThemeService.CurrentAccent;

    private void BuildColorRows()
    {
        ColorRowsPanel.Children.Clear();
        _colorRows.Clear();
        _paletteSwatches.Clear();
        PickerPaletteGrid.Children.Clear();

        foreach (ThemeColorToken token in ThemeColorTokens.All)
        {
            var row = new Button
            {
                Style = (Style)FindResource("ColorRowButton"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");

            var label = new TextBlock
            {
                Text = token.Label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var hexLabel = new TextBlock
            {
                Text = "#000000",
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            hexLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

            Grid.SetColumn(label, 1);
            Grid.SetColumn(hexLabel, 2);
            grid.Children.Add(swatch);
            grid.Children.Add(label);
            grid.Children.Add(hexLabel);
            row.Content = grid;

            AutomationProperties.SetName(row, $"Edit {token.Label} color");
            string key = token.Key;
            row.Click += (_, _) => OpenColorPicker(key);

            _colorRows[token.Key] = (swatch, hexLabel);
            ColorRowsPanel.Children.Add(row);
        }

        foreach (string hex in StandardColors)
        {
            ThemeService.TryParse(hex, out Color color);

            var swatch = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                ToolTip = hex
            };

            swatch.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");

            AutomationProperties.SetName(swatch, $"Palette {hex}");
            swatch.MouseLeftButtonDown += (_, e) =>
            {
                if (ThemeService.TryParse(hex, out Color picked))
                {
                    ApplyPickerColor(picked);
                }

                e.Handled = true;
            };

            _paletteSwatches.Add((swatch, hex));
            PickerPaletteGrid.Children.Add(swatch);
        }

        UpdateColorRows();
    }

    private void UpdateColorRows()
    {
        foreach (ThemeColorToken token in ThemeColorTokens.All)
        {
            if (!_colorRows.TryGetValue(token.Key, out (Border Swatch, TextBlock HexLabel) row))
            {
                continue;
            }

            Color color = ThemeService.Read(_settings.Colors, token.Key);
            row.Swatch.Background = new SolidColorBrush(color);
            row.HexLabel.Text = ToHex(color);
        }
    }

    private void OpenColorPicker(string key)
    {
        ThemeColorToken? token = ThemeColorTokens.Find(key);
        if (token is null)
        {
            return;
        }

        _editingColorKey = token.Key;
        _colorEditBackup = new Dictionary<string, string>(_settings.Colors, StringComparer.OrdinalIgnoreCase);
        PickerTokenLabel.Text = token.Label;

        Color color = ThemeService.Read(_settings.Colors, token.Key);
        _updatingColorPicker = true;
        SetPickerColor(color);
        _updatingColorPicker = false;

        ColorPickerOverlay.Visibility = Visibility.Visible;
        ColorPickerOverlay.BeginAnimation(OpacityProperty, null);
        ColorPickerOverlay.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
        ColorPickerOverlay.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Writes the colour into the controls without triggering previews.</summary>
    private void SetPickerColor(Color color)
    {
        _pendingColor = color;

        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        RedBox.Text = color.R.ToString();
        GreenBox.Text = color.G.ToString();
        BlueBox.Text = color.B.ToString();
        PickerHexBox.Text = ToHex(color);

        UpdatePickerPreview(color);
    }

    private void UpdatePickerPreview(Color color)
    {
        PickerPreviewBrush.Color = color;
        PickerPreviewText.Text = ToHex(color);
        PickerPreviewText.Foreground = new SolidColorBrush(
            ThemeService.IsLight(color) ? Color.FromRgb(0x0B, 0x10, 0x20) : Colors.White);

        foreach ((Border swatch, string hex) in _paletteSwatches)
        {
            bool selected = string.Equals(hex, ToHex(color), StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = selected
                ? (Brush)FindResource("TextPrimaryBrush")
                : (Brush)FindResource("StrokeBrush");
        }
    }

    private void ApplyPickerColor(Color color)
    {
        if (_editingColorKey is null)
        {
            return;
        }

        _updatingColorPicker = true;
        SetPickerColor(color);
        _updatingColorPicker = false;

        PreviewColor(color);
    }

    /// <summary>Applies a candidate colour without saving it, so the whole overlay previews live.</summary>
    private void PreviewColor(Color color)
    {
        if (_editingColorKey is null || _colorEditBackup is null)
        {
            return;
        }

        var preview = new Dictionary<string, string>(_colorEditBackup, StringComparer.OrdinalIgnoreCase)
        {
            [_editingColorKey] = ToHex(color)
        };

        ThemeService.Apply(preview);
        ApplyWallpaperDim();
    }

    private void ColorChannelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingColorPicker)
        {
            return;
        }

        ApplyPickerColor(Color.FromRgb(
            (byte)Math.Clamp(Math.Round(RedSlider.Value), 0, 255),
            (byte)Math.Clamp(Math.Round(GreenSlider.Value), 0, 255),
            (byte)Math.Clamp(Math.Round(BlueSlider.Value), 0, 255)));
    }

    private void ColorChannelBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        CommitChannelBox(sender);
        e.Handled = true;
    }

    private void ColorChannelBox_LostFocus(object sender, RoutedEventArgs e) => CommitChannelBox(sender);

    private void CommitChannelBox(object sender)
    {
        if (_updatingColorPicker || sender is not TextBox)
        {
            return;
        }

        byte Value(TextBox textBox) =>
            byte.TryParse(textBox.Text.Trim(), out byte value) ? value : (byte)0;

        ApplyPickerColor(Color.FromRgb(Value(RedBox), Value(GreenBox), Value(BlueBox)));
    }

    private void PickerHexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        CommitHexBox();
        e.Handled = true;
    }

    /// <summary>Live preview while a complete hex value is typed or pasted.</summary>
    private void PickerHexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingColorPicker)
        {
            return;
        }

        string text = PickerHexBox.Text.Trim();
        int digits = text.StartsWith('#') ? text.Length - 1 : text.Length;

        if (digits is 3 or 6 && ThemeService.TryParse(text, out Color color))
        {
            ApplyPickerColor(color);
        }
    }

    private void PickerHexBox_LostFocus(object sender, RoutedEventArgs e) => CommitHexBox();

    private void CommitHexBox()
    {
        if (_updatingColorPicker)
        {
            return;
        }

        if (ThemeService.TryParse(PickerHexBox.Text, out Color color))
        {
            ApplyPickerColor(color);
            return;
        }

        PickerHexBox.Text = ToHex(_pendingColor);
    }

    private void ColorPickerReset_Click(object sender, RoutedEventArgs e)
    {
        if (_editingColorKey is null)
        {
            return;
        }

        ThemeService.TryParse(ThemeColorTokens.DefaultFor(_editingColorKey), out Color fallback);
        ApplyPickerColor(fallback);
    }

    private void ColorPickerApply_Click(object sender, RoutedEventArgs e)
    {
        if (_editingColorKey is null)
        {
            CloseColorPicker();
            return;
        }

        // A value typed into the hex or channel box may not have lost focus yet.
        CommitPendingPickerInput();

        _settings.Colors[_editingColorKey] = ToHex(_pendingColor);
        SaveSettings();
        ThemeService.Apply(_settings.Colors);
        ApplyWallpaperDim();
        UpdateColorRows();
        CloseColorPicker();
    }

    /// <summary>Commits whichever text box was last edited before saving.</summary>
    private void CommitPendingPickerInput()
    {
        if (RedBox.IsKeyboardFocusWithin || GreenBox.IsKeyboardFocusWithin || BlueBox.IsKeyboardFocusWithin)
        {
            CommitChannelBox(RedBox);
            return;
        }

        CommitHexBox();
    }

    private void ColorPickerCancel_Click(object sender, RoutedEventArgs e)
    {
        CancelColorPicker();
    }

    private void ColorPickerBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ColorPickerOverlay))
        {
            CancelColorPicker();
        }
    }

    private void CancelColorPicker()
    {
        ThemeService.Apply(_settings.Colors);
        ApplyWallpaperDim();
        UpdateColorRows();
        CloseColorPicker();
    }

    private void CloseColorPicker()
    {
        _editingColorKey = null;
        _colorEditBackup = null;
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
        ColorPickerOverlay.BeginAnimation(OpacityProperty, null);
    }

    private void ResetColorsButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Colors.Clear();
        SaveSettings();
        ThemeService.Apply(_settings.Colors);
        ApplyWallpaperDim();
        UpdateColorRows();
        CloseColorPicker();
        ShowToast("All colors reset to the default look.");
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
