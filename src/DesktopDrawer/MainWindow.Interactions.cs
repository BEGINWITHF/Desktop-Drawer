using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDrawer.Helpers;
using DesktopDrawer.Models;
using DesktopDrawer.Services;

namespace DesktopDrawer;

public partial class MainWindow
{
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ShowChrome();

        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;
        bool editingText = Keyboard.FocusedElement is TextBoxBase;
        Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

        if (control && e.Key == Key.F)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_rubberBandActive)
            {
                EndRubberBand();
            }
            else if (_layoutEditing)
            {
                SetLayoutEditing(false);
            }
            else if (_settingsOpen)
            {
                CloseSettings();
            }
            else if (_renamingItem is not null)
            {
                CancelRename(_renamingItem);
            }
            else if (HasSearchText)
            {
                SearchText = string.Empty;
                PageList.Focus();
            }
            else
            {
                PageList.UnselectAll();
                PageList.Focus();
            }

            e.Handled = true;
            return;
        }

        if (_settingsOpen)
        {
            return;
        }

        if (e.Key == Key.F5 && !editingText && _renamingItem is null)
        {
            _ = RefreshAsync();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.N && !editingText)
        {
            _ = CreateNewFolderAsync();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.H)
        {
            SetDesktopIconsHidden(!_desktopIconsService.AreHidden());
            e.Handled = true;
            return;
        }

        if (!editingText && _renamingItem is null && control && shift && e.Key == Key.C)
        {
            CopySelectedPaths();
            e.Handled = true;
            return;
        }

        if (!editingText && _renamingItem is null && control && e.Key == Key.C)
        {
            CopySelectedFilesToClipboard();
            e.Handled = true;
            return;
        }

        if (!editingText && _renamingItem is null && control && e.Key == Key.X)
        {
            CutSelectedFilesToClipboard();
            e.Handled = true;
            return;
        }

        if (!editingText && _renamingItem is null && control && e.Key == Key.V)
        {
            PasteIntoDesktop();
            e.Handled = true;
            return;
        }

        if (!editingText && _renamingItem is null && control && e.Key == Key.A)
        {
            PageList.SelectAll();
            e.Handled = true;
            return;
        }

        if (_renamingItem is null && (e.Key == Key.PageDown || (alt && actualKey == Key.Right)))
        {
            GoToPage(_pageIndex + 1);
            e.Handled = true;
            return;
        }

        if (_renamingItem is null && (e.Key == Key.PageUp || (alt && actualKey == Key.Left)))
        {
            GoToPage(_pageIndex - 1);
            e.Handled = true;
            return;
        }

        if (_renamingItem is null && control && e.Key == Key.Home)
        {
            GoToPage(0);
            e.Handled = true;
            return;
        }

        if (_renamingItem is null && control && e.Key == Key.End)
        {
            GoToPage(_pageCount - 1);
            e.Handled = true;
            return;
        }

        if (editingText || _renamingItem is not null)
        {
            return;
        }

        if (e.Key == Key.Apps || (shift && e.Key == Key.F10))
        {
            ShowNativeContextMenu();
            e.Handled = true;
            return;
        }

        // File actions work no matter which chrome element has focus.
        if (!editingText && _renamingItem is null && alt && actualKey == Key.Enter)
        {
            ShowSelectedProperties();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key is Key.D1 or Key.D2 or Key.D3)
        {
            SetDensityFromShortcut(e.Key switch
            {
                Key.D1 => TileDensity.Compact,
                Key.D2 => TileDensity.Cozy,
                _ => TileDensity.Large
            });
            e.Handled = true;
            return;
        }

        if (shift && e.Key == Key.Delete)
        {
            DeleteSelectedItems(permanent: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelectedItems(permanent: false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            OpenSelectedItems();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            DesktopItem? item = GetPrimarySelectedItem();
            if (item is not null)
            {
                BeginRename(item);
            }

            e.Handled = true;
            return;
        }

        // Anything else with Ctrl, Alt or Win is handed to the real desktop icon
        // list, where shell extensions and third-party tools can react with their
        // own windows.
        if ((control || alt || win) &&
            actualKey is not (Key.F4 or Key.Tab or Key.Escape or
                              Key.LWin or Key.RWin or
                              Key.LeftCtrl or Key.RightCtrl or
                              Key.LeftAlt or Key.RightAlt or
                              Key.LeftShift or Key.RightShift))
        {
            string? path = PageList.SelectedItems.Count == 1 &&
                           PageList.SelectedItems[0] is DesktopItem single
                ? single.FullPath
                : null;

            int virtualKey = KeyInterop.VirtualKeyFromKey(actualKey);
            if (virtualKey > 0 &&
                DesktopKeyForwarder.TryForward(
                    new WindowInteropHelper(this).Handle,
                    path,
                    (byte)virtualKey,
                    control,
                    shift,
                    alt,
                    win))
            {
                e.Handled = true;
            }
        }
    }

    private void SetDensityFromShortcut(TileDensity density)
    {
        _settings.IconSize = 0;
        ApplyDensity(density);
        SaveSettings();
        RecalculateGrid(force: true);

        _initializingSettings = true;
        CompactRadio.IsChecked = density == TileDensity.Compact;
        CozyRadio.IsChecked = density == TileDensity.Cozy;
        LargeRadio.IsChecked = density == TileDensity.Large;
        IconSizeSlider.Value = _requestedIconSize;
        IconSizeValueText.Text = $"{_requestedIconSize:0} px";
        _initializingSettings = false;
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_settingsOpen || _renamingItem is not null || Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        string text = e.Text;
        if (string.IsNullOrEmpty(text) || text.Length == 0 || char.IsControl(text[0]))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if ((now - _typeAheadTime).TotalMilliseconds > 900)
        {
            _typeAhead = string.Empty;
        }

        _typeAheadTime = now;
        _typeAhead += text;

        DesktopItem? match = VisibleItems.FirstOrDefault(
                                item => item.DisplayName.StartsWith(_typeAhead, StringComparison.CurrentCultureIgnoreCase))
                            ?? VisibleItems.FirstOrDefault(
                                item => item.DisplayName.Contains(_typeAhead, StringComparison.CurrentCultureIgnoreCase));

        if (match is null)
        {
            return;
        }

        PageList.SelectedItems.Clear();
        PageList.SelectedItems.Add(match);
        PageList.ScrollIntoView(match);
        e.Handled = true;
    }

    private void PageArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_settingsOpen)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+wheel switches the icon size, like Explorer's view zoom.
            TileDensity next = e.Delta > 0
                ? _settings.Density switch
                {
                    TileDensity.Compact => TileDensity.Cozy,
                    TileDensity.Cozy => TileDensity.Large,
                    _ => TileDensity.Large
                }
                : _settings.Density switch
                {
                    TileDensity.Large => TileDensity.Cozy,
                    TileDensity.Cozy => TileDensity.Compact,
                    _ => TileDensity.Compact
                };

            if (next != _settings.Density)
            {
                SetDensityFromShortcut(next);
            }

            e.Handled = true;
            return;
        }

        DateTime now = DateTime.UtcNow;
        if ((now - _lastWheel).TotalMilliseconds > 450)
        {
            _wheelAccumulator = 0;
        }

        _lastWheel = now;
        _wheelAccumulator += e.Delta;

        if (Math.Abs(_wheelAccumulator) < 110)
        {
            e.Handled = true;
            return;
        }

        if ((now - _lastPageFlip).TotalMilliseconds < 140)
        {
            e.Handled = true;
            return;
        }

        int direction = _wheelAccumulator > 0 ? -1 : 1;
        _wheelAccumulator = 0;
        GoToPage(_pageIndex + direction);
        e.Handled = true;
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        GoToPage(_pageIndex - 1);
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        GoToPage(_pageIndex + 1);
    }

    private void PageDot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is not null)
        {
            GoToPage(Convert.ToInt32(button.Tag));
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshAsync();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        ShowToast("Desktop Drawer stays on the desktop. Exit it from the tray icon.");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        if (_settings.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _isExiting = true;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (!_isExiting)
        {
            return;
        }

        Cleanup();
        Application.Current.Shutdown();
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        Cleanup();
        Application.Current.Shutdown();
    }

    private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        SearchBox.SelectAll();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchText = string.Empty;
            PageList.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            PageList.Focus();
            if (PageList.SelectedItems.Count == 0 && VisibleItems.Count > 0)
            {
                PageList.SelectedItems.Add(VisibleItems[0]);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            SearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

            if (VisibleItems.Count > 0)
            {
                PageList.SelectedItems.Clear();
                PageList.SelectedItems.Add(VisibleItems[0]);
                OpenSelectedItems();
            }

            e.Handled = true;
        }
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchText = string.Empty;
        PageList.Focus();
    }

    private void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingPage)
        {
            UpdateSummary();
        }

        UpdateExpandedLabel();
    }

    /// <summary>
    /// Grows the selected tile downwards so its selection box covers the full
    /// (wrapped) file name instead of clipping it at the normal label height.
    /// </summary>
    private void UpdateExpandedLabel()
    {
        if (_expandedContainer is not null)
        {
            if (_expandedContainer.Template?.FindName("Card", _expandedContainer) is Border previousCard)
            {
                previousCard.Margin = new Thickness(4);
            }

            if (_expandedContainer.Template?.FindName("ContentHost", _expandedContainer) is FrameworkElement previousHost)
            {
                previousHost.Margin = new Thickness(0);
                previousHost.VerticalAlignment = VerticalAlignment.Stretch;
            }

            _expandedContainer = null;
        }

        if (_cleanedUp || _updatingPage || PageList.SelectedItems.Count != 1 ||
            PageList.SelectedItems[0] is not DesktopItem item)
        {
            return;
        }

        if (PageList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container ||
            container.Template?.FindName("Card", container) is not Border card)
        {
            return;
        }

        double labelWidth = Math.Max(40, container.ActualWidth - 8);

        TextBlock? label = FindLabel(container);
        if (label is not null && label.ActualWidth > 10)
        {
            labelWidth = label.ActualWidth;
        }

        var probe = new TextBlock
        {
            Text = item.DisplayName,
            FontFamily = FontFamily,
            FontSize = TileFontSize,
            LineHeight = 16,
            TextWrapping = TextWrapping.Wrap
        };

        probe.Measure(new Size(labelWidth, double.PositiveInfinity));
        double overflow = probe.DesiredSize.Height - TileNameHeight;

        if (overflow <= 1)
        {
            return;
        }

        double contentHeight = IconSize + 7 + TileNameHeight;
        double cardHeight = Math.Max(1, container.ActualHeight - 8);
        double innerHeight = Math.Max(1, cardHeight - 2);
        double hostMargin = Math.Max(0, (innerHeight - contentHeight) / 2);
        double topGap = hostMargin + 1;

        double textHeight = TileNameHeight + overflow;
        double textBottom = topGap + IconSize + 7 + textHeight;
        double extension = Math.Max(0, textBottom + topGap - cardHeight);

        card.Margin = new Thickness(4, 4, 4, -extension);

        // Keep the icon and label where they are: the extra height is added only
        // below them, with the same padding as above.
        if (container.Template?.FindName("ContentHost", container) is FrameworkElement contentHost)
        {
            contentHost.VerticalAlignment = VerticalAlignment.Top;
            contentHost.Margin = new Thickness(0, hostMargin, 0, 0);
        }

        _expandedContainer = container;
    }

    private static TextBlock? FindLabel(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is TextBlock { Name: "NameText" } text)
            {
                return text;
            }

            TextBlock? nested = FindLabel(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void PageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsTextEditingSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        DesktopItem? item = GetItemFromSource(e.OriginalSource);
        if (item is null)
        {
            if (_renamingItem is not null)
            {
                CommitRename(_renamingItem);
            }

            PageList.UnselectAll();
            _dragCandidate = null;
            PageList.Focus();

            if (!_layoutEditing && !_settingsOpen)
            {
                StartRubberBand(e.GetPosition(PageList));
                e.Handled = true;
            }

            return;
        }

        _dragCandidate = item;
        _dragStart = e.GetPosition(PageList);
    }

    private void StartRubberBand(Point origin)
    {
        _rubberBandActive = true;
        _rubberBandOrigin = origin;

        SelectionRect.Visibility = Visibility.Visible;
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        UpdateRubberBand(origin);

        PageList.CaptureMouse();
    }

    private void UpdateRubberBand(Point current)
    {
        double x = Math.Min(_rubberBandOrigin.X, current.X);
        double y = Math.Min(_rubberBandOrigin.Y, current.Y);
        double width = Math.Abs(current.X - _rubberBandOrigin.X);
        double height = Math.Abs(current.Y - _rubberBandOrigin.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = width;
        SelectionRect.Height = height;

        if (width < 2 && height < 2)
        {
            return;
        }

        var area = new Rect(x, y, width, height);
        var hits = new List<DesktopItem>();

        foreach (DesktopItem item in VisibleItems)
        {
            if (PageList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container ||
                !container.IsVisible)
            {
                continue;
            }

            Point topLeft = container.TransformToAncestor(PageList).Transform(new Point(0, 0));
            var bounds = new Rect(topLeft, container.RenderSize);

            if (area.IntersectsWith(bounds))
            {
                hits.Add(item);
            }
        }

        var selected = PageList.SelectedItems.Cast<DesktopItem>().ToHashSet();
        if (selected.SetEquals(hits))
        {
            return;
        }

        PageList.UnselectAll();
        foreach (DesktopItem hit in hits)
        {
            PageList.SelectedItems.Add(hit);
        }
    }

    private void EndRubberBand()
    {
        if (!_rubberBandActive)
        {
            return;
        }

        _rubberBandActive = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;

        if (PageList.IsMouseCaptured)
        {
            PageList.ReleaseMouseCapture();
        }
    }

    private void PageList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_rubberBandActive)
        {
            return;
        }

        EndRubberBand();
        e.Handled = true;
    }

    private void PageList_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_rubberBandActive)
        {
            EndRubberBand();
        }
    }

    private void PageList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_rubberBandActive)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateRubberBand(e.GetPosition(PageList));
            }
            else
            {
                EndRubberBand();
            }

            e.Handled = true;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragCandidate = null;
            return;
        }

        if (_dragCandidate is null || _renamingItem is not null)
        {
            return;
        }

        Point position = e.GetPosition(PageList);
        if (Math.Abs(position.X - _dragStart.X) < 6 && Math.Abs(position.Y - _dragStart.Y) < 6)
        {
            return;
        }

        DesktopItem dragItem = _dragCandidate;
        _dragCandidate = null;

        List<DesktopItem> items = PageList.SelectedItems.Cast<DesktopItem>().ToList();
        if (!items.Contains(dragItem))
        {
            items = [dragItem];
        }

        string[] paths = items
            .Select(item => item.FullPath)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToArray();

        if (paths.Length == 0)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, paths);

        try
        {
            DragDrop.DoDragDrop(
                PageList,
                data,
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Drag and drop failed");
        }
    }

    private void PageList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DesktopItem? item = GetItemFromSource(e.OriginalSource);
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        _lastContextMenuPoint = PointToScreen(e.GetPosition(this));

        if (item is null)
        {
            _shellMenuPending = false;
            PageList.UnselectAll();
            PageList.ContextMenu = BuildEmptyAreaMenu();
        }
        else
        {
            if (!PageList.SelectedItems.Contains(item))
            {
                if (!control)
                {
                    PageList.UnselectAll();
                }

                PageList.SelectedItems.Add(item);
            }

            // Shift+right-click opens the native Windows menu, plain right-click
            // opens the styled app menu.
            _shellMenuPending = shift;
            PageList.ContextMenu = shift ? null : BuildItemMenu();
        }

        if (PageList.ContextMenu is not null)
        {
            PageList.ContextMenu.PlacementTarget = PageList;
            PageList.ContextMenu.Placement = PlacementMode.MousePoint;
        }
    }

    private void PageList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_shellMenuPending)
        {
            return;
        }

        _shellMenuPending = false;
        e.Handled = true;

        List<DesktopItem> selected = PageList.SelectedItems.Cast<DesktopItem>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        bool shown = _shellContextMenu.Show(
            new WindowInteropHelper(this).Handle,
            selected.Select(item => item.FullPath).ToList(),
            (int)_lastContextMenuPoint.X,
            (int)_lastContextMenuPoint.Y);

        if (!shown && !_cleanedUp)
        {
            PageList.ContextMenu = BuildItemMenu();
            PageList.ContextMenu.PlacementTarget = PageList;
            PageList.ContextMenu.Placement = PlacementMode.MousePoint;
            PageList.ContextMenu.IsOpen = true;
        }

        _ = RefreshAsync();
    }

    private void PageList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_renamingItem is not null || IsTextEditingSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        DesktopItem? item = GetItemFromSource(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        if (!PageList.SelectedItems.Contains(item))
        {
            PageList.UnselectAll();
            PageList.SelectedItems.Add(item);
        }

        OpenSelectedItems();
    }

    private void PageList_KeyDown(object sender, KeyEventArgs e)
    {
        // Shortcuts are handled in Window_PreviewKeyDown.
    }

    private DesktopItem? GetPrimarySelectedItem()
    {
        return PageList.SelectedItems.Cast<DesktopItem>().FirstOrDefault();
    }

    private void OpenSelectedItems()
    {
        List<DesktopItem> items = PageList.SelectedItems.Cast<DesktopItem>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        try
        {
            _shellService.Open(items);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not open desktop items");
            ShowToast("Could not open that item.");
        }
    }

    private void OpenWithSelectedItem()
    {
        DesktopItem? item = GetPrimarySelectedItem();
        if (item is null)
        {
            return;
        }

        try
        {
            _shellService.OpenWith(item);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not open the Open With dialog");
            ShowToast("Could not open the Open With dialog.");
        }
    }

    private void ShowSelectedInExplorer()
    {
        DesktopItem? item = GetPrimarySelectedItem();
        if (item is null)
        {
            return;
        }

        try
        {
            _shellService.ShowInExplorer(item);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not show item in Explorer");
            ShowToast("Could not open File Explorer.");
        }
    }

    private void ShowSelectedProperties()
    {
        DesktopItem? item = GetPrimarySelectedItem();
        if (item is null)
        {
            return;
        }

        try
        {
            _shellService.ShowProperties(item, new WindowInteropHelper(this).Handle);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not show item properties");
            ShowToast("Could not open item properties.");
        }
    }

    private void CopySelectedPaths()
    {
        List<DesktopItem> items = PageList.SelectedItems.Cast<DesktopItem>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        try
        {
            _shellService.CopyPaths(items);
            ShowToast(items.Count == 1 ? "Path copied" : "Paths copied");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not copy paths");
            ShowToast("Could not copy to the clipboard.");
        }
    }

    private void CopySelectedFilesToClipboard()
    {
        SetClipboardFileDrop(move: false);
    }

    private void CutSelectedFilesToClipboard()
    {
        SetClipboardFileDrop(move: true);
    }

    private void SetClipboardFileDrop(bool move)
    {
        List<DesktopItem> items = PageList.SelectedItems.Cast<DesktopItem>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        try
        {
            var collection = new StringCollection();
            foreach (DesktopItem item in items)
            {
                collection.Add(item.FullPath);
            }

            var data = new DataObject();
            data.SetFileDropList(collection);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(move ? 2 : 1)));
            Clipboard.SetDataObject(data, true);

            if (move)
            {
                ShowToast(items.Count == 1 ? "Item cut" : $"{items.Count} items cut");
            }
            else
            {
                ShowToast(items.Count == 1 ? "Item copied" : $"{items.Count} items copied");
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not copy files to the clipboard");
            ShowToast("Could not copy to the clipboard.");
        }
    }

    private async void PasteIntoDesktop()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                ShowToast("The clipboard is empty.");
                return;
            }

            List<string> paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (paths.Count == 0)
            {
                return;
            }

            bool move = GetClipboardDropEffect() == 2;
            _shellService.TransferToFolder(
                paths,
                _desktopService.UserDesktopPath,
                move,
                new WindowInteropHelper(this).Handle);

            if (move)
            {
                Clipboard.Clear();
            }

            ShowToast(move ? "Moved to the desktop" : "Pasted to the desktop");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Paste failed");
            ShowToast(ex is ShellOperationException ? ex.Message : "Could not paste into the desktop.");
        }

        await RefreshAsync();
    }

    private static int GetClipboardDropEffect()
    {
        try
        {
            if (Clipboard.GetDataObject() is { } data && data.GetDataPresent("Preferred DropEffect"))
            {
                if (data.GetData("Preferred DropEffect") is MemoryStream stream)
                {
                    byte[] bytes = stream.ToArray();
                    if (bytes.Length >= 4)
                    {
                        return BitConverter.ToInt32(bytes, 0);
                    }
                }
            }
        }
        catch
        {
            // Default to copy.
        }

        return 1;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (_settingsOpen || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = GetDropEffect(e, GetDroppedPaths(e), GetDropDestination(e));
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_settingsOpen || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        string destination = GetDropDestination(e);
        List<string> paths = GetDroppedPaths(e);

        // Dropping items back into the folder they already live in changes nothing.
        string normalizedDestination = destination.TrimEnd(Path.DirectorySeparatorChar);
        paths = paths
            .Where(path =>
            {
                string? parent = Path.GetDirectoryName(path);
                return parent is null ||
                       !string.Equals(parent.TrimEnd(Path.DirectorySeparatorChar), normalizedDestination, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (paths.Count == 0)
        {
            return;
        }

        bool move = GetDropEffect(e, paths, destination) == DragDropEffects.Move;

        try
        {
            _shellService.TransferToFolder(paths, destination, move, new WindowInteropHelper(this).Handle);
            ShowToast(move ? "Moved to the desktop" : "Copied to the desktop");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Drop failed");
            ShowToast(ex is ShellOperationException ? ex.Message : "Could not drop the items here.");
        }

        await RefreshAsync();
    }

    private string GetDropDestination(DragEventArgs e)
    {
        DesktopItem? target = GetItemFromSource(e.OriginalSource as DependencyObject);
        if (target is { IsFolder: true })
        {
            return target.FullPath;
        }

        return _desktopService.UserDesktopPath;
    }

    private static List<string> GetDroppedPaths(DragEventArgs e)
    {
        return e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList()
            : [];
    }

    private static DragDropEffects GetDropEffect(DragEventArgs e, IReadOnlyCollection<string> paths, string destination)
    {
        if (paths.Count == 0)
        {
            return DragDropEffects.None;
        }

        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
        {
            return DragDropEffects.Copy;
        }

        if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0)
        {
            return DragDropEffects.Move;
        }

        string? destinationRoot = Path.GetPathRoot(destination);
        bool sameVolume = paths.All(path =>
            string.Equals(Path.GetPathRoot(path), destinationRoot, StringComparison.OrdinalIgnoreCase));

        return sameVolume ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private void DeleteSelectedItems(bool permanent)
    {
        List<DesktopItem> items = PageList.SelectedItems.Cast<DesktopItem>().ToList();
        DeleteItems(items, permanent);
    }

    private async void DeleteItems(IReadOnlyList<DesktopItem> items, bool permanent = false)
    {
        if (items.Count == 0)
        {
            return;
        }

        string message = permanent
            ? items.Count == 1
                ? $"Deleted \"{items[0].DisplayName}\" permanently"
                : $"Deleted {items.Count} items permanently"
            : items.Count == 1
                ? $"Moved \"{items[0].DisplayName}\" to the Recycle Bin"
                : $"Moved {items.Count} items to the Recycle Bin";

        try
        {
            _shellService.Delete(items, permanent);
            ShowToast(message);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Delete failed");
            ShowToast(ex is ShellOperationException
                ? ex.Message
                : permanent
                    ? "Could not delete the items."
                    : "Could not move the items to the Recycle Bin.");
        }
        finally
        {
            await RefreshAsync();
        }
    }

    private async Task CreateNewFolderAsync()
    {
        try
        {
            if (HasSearchText)
            {
                SearchText = string.Empty;
            }

            string path = _shellService.CreateNewFolder(_desktopService.UserDesktopPath);
            await RefreshAsync(selectPath: path);

            DesktopItem? item = _allItems.FirstOrDefault(
                candidate => string.Equals(candidate.FullPath, path, StringComparison.OrdinalIgnoreCase));

            if (item is not null)
            {
                BeginRename(item);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not create a new folder");
            ShowToast("Could not create a new folder.");
        }
    }

    private void BeginRename(DesktopItem? item)
    {
        if (item is null || !VisibleItems.Contains(item))
        {
            return;
        }

        foreach (DesktopItem other in _allItems.Where(candidate => candidate.IsRenaming))
        {
            other.IsRenaming = false;
        }

        _renamingItem = item;
        item.EditName = _shellService.GetRenameSeed(item);
        item.IsRenaming = true;
    }

    private void CancelRename(DesktopItem item)
    {
        item.EditName = item.DisplayName;
        item.IsRenaming = false;

        if (ReferenceEquals(_renamingItem, item))
        {
            _renamingItem = null;
        }

        PageList.Focus();
    }

    private async void CommitRename(DesktopItem item)
    {
        if (!item.IsRenaming)
        {
            return;
        }

        string requestedName = item.EditName;
        item.IsRenaming = false;

        if (ReferenceEquals(_renamingItem, item))
        {
            _renamingItem = null;
        }

        if (string.IsNullOrWhiteSpace(requestedName) ||
            string.Equals(requestedName.Trim(), item.DisplayName, StringComparison.Ordinal))
        {
            item.EditName = item.DisplayName;
            PageList.Focus();
            return;
        }

        try
        {
            string destination = _shellService.Rename(item, requestedName);
            ShowToast("Renamed");
            await RefreshAsync(selectPath: destination);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Rename failed");
            ShowToast(ex is ShellOperationException ? ex.Message : "Could not rename that item.");
            item.EditName = item.DisplayName;
        }
        finally
        {
            PageList.Focus();
        }
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox || !textBox.IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!textBox.IsVisible)
            {
                return;
            }

            textBox.Focus();

            // Windows keeps the extension out of the selection, so typing replaces
            // only the name.
            string text = textBox.Text;
            int selectionLength = text.Length;

            if (textBox.DataContext is DesktopItem item && !item.IsFolder)
            {
                string extension = Path.GetExtension(item.Name);
                if (extension.Length > 0 &&
                    text.EndsWith(extension, StringComparison.OrdinalIgnoreCase) &&
                    text.Length > extension.Length)
                {
                    selectionLength = text.Length - extension.Length;
                }
            }

            textBox.Select(0, selectionLength);
        }));
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: DesktopItem item })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitRename(item);
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelRename(item);
            return;
        }

        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            CommitRename(item);
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: DesktopItem item } && item.IsRenaming)
        {
            CommitRename(item);
        }
    }

    private DesktopItem? GetItemFromSource(object? source)
    {
        if (source is not DependencyObject dependencyObject)
        {
            return null;
        }

        ListBoxItem? container = ItemsControl.ContainerFromElement(PageList, dependencyObject) as ListBoxItem;
        return container?.DataContext as DesktopItem;
    }

    private static bool IsTextEditingSource(DependencyObject? source)
    {
        for (DependencyObject? current = source; current is not null; current = GetParent(current))
        {
            if (current is TextBoxBase)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        for (DependencyObject? current = source; current is not null; current = GetParent(current))
        {
            if (current is ButtonBase or TextBoxBase or ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        try
        {
            DependencyObject? parent = VisualTreeHelper.GetParent(child);
            if (parent is not null)
            {
                return parent;
            }
        }
        catch (InvalidOperationException)
        {
            // Content elements are not part of the visual tree.
        }

        return LogicalTreeHelper.GetParent(child);
    }
}
