using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopDrawer.Helpers;
using DesktopDrawer.Models;

namespace DesktopDrawer;

public partial class MainWindow
{
    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        ContextMenu menu = BuildSortMenu();
        SortButton.ContextMenu = menu;
        menu.PlacementTarget = SortButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu BuildSortMenu()
    {
        var menu = new ContextMenu();

        AddCheckableItem(menu, "Name", _settings.SortMode == SortMode.Name, () => SetSortMode(SortMode.Name));
        AddCheckableItem(menu, "Date modified", _settings.SortMode == SortMode.DateModified, () => SetSortMode(SortMode.DateModified));
        AddCheckableItem(menu, "Type", _settings.SortMode == SortMode.Type, () => SetSortMode(SortMode.Type));
        AddCheckableItem(menu, "Size", _settings.SortMode == SortMode.Size, () => SetSortMode(SortMode.Size));

        menu.Items.Add(new Separator());
        AddCheckableItem(menu, "Folders first", _settings.FoldersFirst, ToggleFoldersFirst);
        AddCheckableItem(menu, "Show hidden files", _settings.ShowHidden, ToggleShowHidden);
        menu.Items.Add(BuildGroupByMenu());
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Refresh", "\uE72C", "F5", () => _ = RefreshAsync());

        return menu;
    }

    private MenuItem BuildGroupByMenu()
    {
        var menu = new MenuItem { Header = "Group by" };
        AddCheckableItem(menu.Items, "None", _settings.GroupMode == GroupMode.None, () => SetGroupMode(GroupMode.None));
        AddCheckableItem(menu.Items, "Type", _settings.GroupMode == GroupMode.Type, () => SetGroupMode(GroupMode.Type));
        AddCheckableItem(menu.Items, "Name", _settings.GroupMode == GroupMode.Name, () => SetGroupMode(GroupMode.Name));
        AddCheckableItem(menu.Items, "Date modified", _settings.GroupMode == GroupMode.Date, () => SetGroupMode(GroupMode.Date));
        return menu;
    }

    private void SetSortMode(SortMode mode)
    {
        _settings.SortMode = mode;
        SaveSettings();
        UpdateSortLabel();
        RebuildFilteredItems();
        _pageIndex = 0;
        PageList.UnselectAll();
        UpdatePage();
    }

    private void SetGroupMode(GroupMode mode)
    {
        _settings.GroupMode = mode;
        SaveSettings();
        _pageIndex = 0;
        PageList.UnselectAll();
        UpdatePage();
        ShowToast(mode == GroupMode.None ? "Grouping off" : $"Grouped by {DescribeGroupMode(mode)}");
    }

    private static string DescribeGroupMode(GroupMode mode) => mode switch
    {
        GroupMode.Type => "type",
        GroupMode.Name => "name",
        GroupMode.Date => "date modified",
        _ => "nothing"
    };

    private void ToggleFoldersFirst()
    {
        _settings.FoldersFirst = !_settings.FoldersFirst;
        SaveSettings();

        _initializingSettings = true;
        FoldersFirstToggle.IsChecked = _settings.FoldersFirst;
        _initializingSettings = false;

        RebuildFilteredItems();
        _pageIndex = 0;
        PageList.UnselectAll();
        UpdatePage();
    }

    private void ToggleShowHidden()
    {
        _settings.ShowHidden = !_settings.ShowHidden;
        SaveSettings();

        _initializingSettings = true;
        ShowHiddenToggle.IsChecked = _settings.ShowHidden;
        _initializingSettings = false;

        _ = RefreshAsync(resetPage: true);
    }

    private ContextMenu BuildItemMenu()
    {
        List<DesktopItem> selected = PageList.SelectedItems.Cast<DesktopItem>().ToList();

        if (selected.Count == 0)
        {
            return BuildEmptyAreaMenu();
        }

        var menu = new ContextMenu();

        if (selected.Count == 1)
        {
            DesktopItem item = selected[0];

            MenuItem open = AddMenuItem(menu, "Open", "\uE8E5", "Enter", OpenSelectedItems);
            open.FontWeight = FontWeights.SemiBold;
            AddMenuItem(menu, "Open with...", "\uE7AC", null, OpenWithSelectedItem);
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Copy", "\uE8C8", "Ctrl+C", CopySelectedFilesToClipboard);
            AddMenuItem(menu, "Copy path", "\uE71B", null, CopySelectedPaths);
            AddMenuItem(menu, "Show in File Explorer", "\uE838", null, ShowSelectedInExplorer);
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Rename", "\uE8AC", "F2", () => BeginRename(item));
            MenuItem delete = AddMenuItem(menu, "Move to Recycle Bin", "\uE74D", "Del", () => DeleteItems(selected));
            delete.Foreground = (Brush)FindResource("DangerBrush");
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Properties", "\uE946", null, ShowSelectedProperties);
        }
        else
        {
            AddMenuItem(menu, $"Open {selected.Count} items", "\uE8E5", "Enter", OpenSelectedItems);
            AddMenuItem(menu, "Copy", "\uE8C8", "Ctrl+C", CopySelectedFilesToClipboard);
            AddMenuItem(menu, "Copy paths", "\uE71B", null, CopySelectedPaths);
            menu.Items.Add(new Separator());
            MenuItem delete = AddMenuItem(
                menu,
                $"Move {selected.Count} items to Recycle Bin",
                "\uE74D",
                "Del",
                () => DeleteItems(selected));
            delete.Foreground = (Brush)FindResource("DangerBrush");
        }

        menu.Items.Add(new Separator());
        AddMenuItem(menu, "More options...", "\uE712", "Shift+F10", ShowNativeContextMenu);

        return menu;
    }

    /// <summary>
    /// Opens the native Windows menu, which has every installed shell extension.
    /// </summary>
    private void ShowNativeContextMenu()
    {
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

        if (!shown)
        {
            ShowToast("Windows could not open its menu for these items.");
        }

        _ = RefreshAsync();
    }

    private ContextMenu BuildEmptyAreaMenu()
    {
        var menu = new ContextMenu();

        AddMenuItem(menu, "New folder", "\uE8F4", "Ctrl+Shift+N", () => _ = CreateNewFolderAsync());
        AddMenuItem(menu, "Refresh", "\uE72C", "F5", () => _ = RefreshAsync());
        menu.Items.Add(new Separator());
        AddCheckableItem(menu, "Show hidden files", _settings.ShowHidden, ToggleShowHidden);
        menu.Items.Add(BuildGroupByMenu());
        AddCheckableItem(
            menu,
            "Hide Windows desktop icons",
            _desktopIconsService.AreHidden(),
            () => SetDesktopIconsHidden(!_desktopIconsService.AreHidden()));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Open Desktop folder", "\uE838", null, () =>
        {
            OpenDesktopFolderButton_Click(this, new RoutedEventArgs());
        });
        AddMenuItem(menu, "Settings", "\uE713", null, OpenSettings);
        menu.Items.Add(new Separator());
        MenuItem exit = AddMenuItem(menu, "Exit Desktop Drawer", "\uE8BB", null, ExitApplication);
        exit.Foreground = (Brush)FindResource("DangerBrush");

        return menu;
    }

    private void AddCheckableItem(ContextMenu menu, string header, bool isChecked, Action action)
    {
        AddCheckableItem(menu.Items, header, isChecked, action);
    }

    private void AddCheckableItem(ItemCollection items, string header, bool isChecked, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = isChecked
        };

        item.Click += (_, _) => action();
        items.Add(item);
    }

    private MenuItem AddMenuItem(
        ContextMenu menu,
        string header,
        string? glyph,
        string? gesture,
        Action action)
    {
        var item = new MenuItem
        {
            Header = header
        };

        if (!string.IsNullOrEmpty(glyph))
        {
            item.Icon = CreateMenuIcon(glyph);
        }

        if (!string.IsNullOrEmpty(gesture))
        {
            item.InputGestureText = gesture;
        }

        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private TextBlock CreateMenuIcon(string glyph)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (FindResource("TextMutedBrush") is Brush brush)
        {
            icon.Foreground = brush;
        }

        return icon;
    }
}
