using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopDrawer.Models;
using DesktopDrawer.Services;

namespace DesktopDrawer;

public partial class MainWindow
{
    private const string ReorderFormat = "DesktopDrawer.ReorderItem";

    /// <summary>
    /// Marks the dragged item as pinned and renumbers every pinned item so the
    /// stored positions describe the exact order the user just created.
    /// </summary>
    private void ReorderItem(DesktopItem dragged, DesktopItem target, bool after)
    {
        if (HasSearchText || ReferenceEquals(dragged, target))
        {
            return;
        }

        var order = _filteredItems.ToList();
        if (!order.Remove(dragged))
        {
            return;
        }

        int targetIndex = order.IndexOf(target);
        if (targetIndex < 0)
        {
            return;
        }

        int insertIndex = Math.Clamp(targetIndex + (after ? 1 : 0), 0, order.Count);
        order.Insert(insertIndex, dragged);

        _settings.ManualOrder[PathKey(dragged.FullPath)] = 0;
        RenumberManualOrder(order);
        SaveSettings();
        RebuildFilteredItems();

        int capacity = Math.Max(1, Columns * Rows);
        List<PageSlice> pages = BuildPages(capacity);
        int pageIndex = pages.FindIndex(page => page.Items.Contains(dragged));
        _pageIndex = pageIndex >= 0 ? pageIndex : 0;

        UpdatePage(selectPath: dragged.FullPath);
        UpdateSortLabel();
        ShowToast(I18n.T("Toast.PositionPinned"));
    }

    private void ResetManualOrder()
    {
        if (_settings.ManualOrder.Count == 0)
        {
            return;
        }

        _settings.ManualOrder.Clear();
        SaveSettings();
        RebuildFilteredItems();
        _pageIndex = 0;
        UpdatePage();
        UpdateSortLabel();
        ShowToast(I18n.T("Toast.ManualReset"));
    }

    private void ApplyManualOrder(List<DesktopItem> items)
    {
        if (_settings.ManualOrder.Count == 0)
        {
            return;
        }

        var pinned = new List<(DesktopItem Item, int Position)>();
        foreach (DesktopItem item in items)
        {
            if (_settings.ManualOrder.TryGetValue(PathKey(item.FullPath), out int position))
            {
                pinned.Add((item, position));
            }
        }

        if (pinned.Count == 0)
        {
            return;
        }

        pinned.Sort((left, right) => left.Position.CompareTo(right.Position));

        var pinnedPaths = pinned
            .Select(entry => PathKey(entry.Item.FullPath))
            .ToHashSet(StringComparer.Ordinal);

        var result = items
            .Where(item => !pinnedPaths.Contains(PathKey(item.FullPath)))
            .ToList();

        foreach ((DesktopItem item, int position) in pinned)
        {
            result.Insert(Math.Clamp(position, 0, result.Count), item);
        }

        items.Clear();
        items.AddRange(result);
    }

    private void RenumberManualOrder(IReadOnlyList<DesktopItem> order)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < order.Count; i++)
        {
            string key = PathKey(order[i].FullPath);
            if (_settings.ManualOrder.ContainsKey(key))
            {
                _settings.ManualOrder[key] = i;
                seen.Add(key);
            }
        }

        foreach (string key in _settings.ManualOrder.Keys.ToList())
        {
            if (!seen.Contains(key))
            {
                _settings.ManualOrder.Remove(key);
            }
        }
    }

    private void PruneManualOrder()
    {
        if (_settings.ManualOrder.Count == 0 || _allItems.Count == 0)
        {
            return;
        }

        var paths = _allItems
            .Select(item => PathKey(item.FullPath))
            .ToHashSet(StringComparer.Ordinal);

        List<string> stale = _settings.ManualOrder.Keys
            .Where(key => !paths.Contains(key))
            .ToList();

        if (stale.Count == 0)
        {
            return;
        }

        foreach (string key in stale)
        {
            _settings.ManualOrder.Remove(key);
        }

        SaveSettings();
        UpdateSortLabel();
    }

    private static string PathKey(string path) => path.ToLowerInvariant();

    private void PageList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ReorderFormat))
        {
            return;
        }

        e.Handled = true;

        if (HasSearchText)
        {
            HideReorderMarker();
            e.Effects = DragDropEffects.None;
            return;
        }

        string? draggedPath = e.Data.GetData(ReorderFormat) as string;
        DesktopItem? target = GetItemAtDragPoint(e);

        if (target is null ||
            string.Equals(target.FullPath, draggedPath, StringComparison.OrdinalIgnoreCase))
        {
            HideReorderMarker();
            e.Effects = DragDropEffects.None;
            return;
        }

        ShowReorderMarker(target);
        e.Effects = DragDropEffects.Move;
    }

    private void PageList_DragLeave(object sender, DragEventArgs e)
    {
        HideReorderMarker();
    }

    private void PageList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ReorderFormat))
        {
            return;
        }

        e.Handled = true;
        HideReorderMarker();

        if (HasSearchText)
        {
            return;
        }

        string? draggedPath = e.Data.GetData(ReorderFormat) as string;
        if (string.IsNullOrWhiteSpace(draggedPath))
        {
            return;
        }

        DesktopItem? dragged = _filteredItems.FirstOrDefault(
            item => string.Equals(item.FullPath, draggedPath, StringComparison.OrdinalIgnoreCase));

        DesktopItem? target = GetItemAtDragPoint(e);
        if (dragged is null || target is null || ReferenceEquals(dragged, target))
        {
            return;
        }

        bool after = false;
        if (PageList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem container)
        {
            Point point = e.GetPosition(container);
            after = point.X > container.ActualWidth / 2;
        }

        ReorderItem(dragged, target, after);
    }

    private void ShowReorderMarker(DesktopItem item)
    {
        if (PageList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
        {
            HideReorderMarker();
            return;
        }

        Point topLeft = container
            .TransformToAncestor(SelectionCanvas)
            .Transform(new Point(0, 0));

        Canvas.SetLeft(ReorderMarker, topLeft.X);
        Canvas.SetTop(ReorderMarker, topLeft.Y);
        ReorderMarker.Width = container.ActualWidth;
        ReorderMarker.Height = container.ActualHeight;
        ReorderMarker.Visibility = Visibility.Visible;
    }

    private DesktopItem? GetItemAtDragPoint(DragEventArgs e)
    {
        Point point = e.GetPosition(PageList);

        if (point.X < 0 || point.Y < 0 ||
            point.X > PageList.ActualWidth || point.Y > PageList.ActualHeight)
        {
            return null;
        }

        return GetItemFromSource(PageList.InputHitTest(point) as DependencyObject);
    }

    private void HideReorderMarker()
    {
        ReorderMarker.Visibility = Visibility.Collapsed;
        ReorderMarker.Width = 0;
        ReorderMarker.Height = 0;
    }
}
