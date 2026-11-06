using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopDrawer.Helpers;
using DesktopDrawer.Models;
using DesktopDrawer.Services;

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

        AddCheckableItem(menu, I18n.T("Sort.Name"), _settings.SortMode == SortMode.Name, () => SetSortMode(SortMode.Name));
        AddCheckableItem(menu, I18n.T("Sort.Date"), _settings.SortMode == SortMode.DateModified, () => SetSortMode(SortMode.DateModified));
        AddCheckableItem(menu, I18n.T("Sort.Type"), _settings.SortMode == SortMode.Type, () => SetSortMode(SortMode.Type));
        AddCheckableItem(menu, I18n.T("Sort.Size"), _settings.SortMode == SortMode.Size, () => SetSortMode(SortMode.Size));

        menu.Items.Add(new Separator());
        AddCheckableItem(menu, I18n.T("Sort.FoldersFirst"), _settings.FoldersFirst, ToggleFoldersFirst);
        AddCheckableItem(menu, I18n.T("Sort.ShowHidden"), _settings.ShowHidden, ToggleShowHidden);
        menu.Items.Add(BuildGroupByMenu());
        menu.Items.Add(new Separator());
        AddMenuItem(menu, I18n.T("Sort.Refresh"), "\uE72C", "F5", () => _ = RefreshAsync());

        return menu;
    }

    private MenuItem BuildGroupByMenu()
    {
        var menu = new MenuItem { Header = I18n.T("Sort.GroupBy") };
        AddCheckableItem(menu.Items, I18n.T("Sort.None"), _settings.GroupMode == GroupMode.None, () => SetGroupMode(GroupMode.None));
        AddCheckableItem(menu.Items, I18n.T("Sort.Type"), _settings.GroupMode == GroupMode.Type, () => SetGroupMode(GroupMode.Type));
        AddCheckableItem(menu.Items, I18n.T("Sort.Name"), _settings.GroupMode == GroupMode.Name, () => SetGroupMode(GroupMode.Name));
        AddCheckableItem(menu.Items, I18n.T("Sort.Date"), _settings.GroupMode == GroupMode.Date, () => SetGroupMode(GroupMode.Date));
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
        ShowToast(mode == GroupMode.None
            ? I18n.T("Toast.GroupingOff")
            : I18n.F("Toast.GroupedBy", DescribeGroupMode(mode)));
    }

    private static string DescribeGroupMode(GroupMode mode) => mode switch
    {
        GroupMode.Type => I18n.T("Group.Type"),
        GroupMode.Name => I18n.T("Group.Name"),
        GroupMode.Date => I18n.T("Group.Date"),
        _ => I18n.T("Group.None")
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

            MenuItem open = AddMenuItem(menu, I18n.T("Menu.Open"), "\uE8E5", "Enter", OpenSelectedItems);
            open.FontWeight = FontWeights.SemiBold;
            AddMenuItem(menu, I18n.T("Menu.OpenWith"), "\uE7AC", null, OpenWithSelectedItem);
            menu.Items.Add(new Separator());
            AddMenuItem(menu, I18n.T("Menu.Copy"), "\uE8C8", "Ctrl+C", CopySelectedFilesToClipboard);
            AddMenuItem(menu, I18n.T("Menu.CopyPath"), "\uE71B", null, CopySelectedPaths);
            AddMenuItem(menu, I18n.T("Menu.ShowInExplorer"), "\uE838", null, ShowSelectedInExplorer);
            menu.Items.Add(new Separator());
            AddMenuItem(menu, I18n.T("Menu.Rename"), "\uE8AC", "F2", () => BeginRename(item));
            MenuItem delete = AddMenuItem(menu, I18n.T("Menu.Delete"), "\uE74D", "Del", () => DeleteItems(selected));
            delete.Foreground = (Brush)FindResource("DangerBrush");
            menu.Items.Add(new Separator());
            AddMenuItem(menu, I18n.T("Menu.Properties"), "\uE946", null, ShowSelectedProperties);
        }
        else
        {
            AddMenuItem(menu, I18n.F("Menu.OpenN", selected.Count), "\uE8E5", "Enter", OpenSelectedItems);
            AddMenuItem(menu, I18n.T("Menu.Copy"), "\uE8C8", "Ctrl+C", CopySelectedFilesToClipboard);
            AddMenuItem(menu, I18n.T("Menu.CopyPaths"), "\uE71B", null, CopySelectedPaths);
            menu.Items.Add(new Separator());
            MenuItem delete = AddMenuItem(
                menu,
                I18n.F("Menu.DeleteN", selected.Count),
                "\uE74D",
                "Del",
                () => DeleteItems(selected));
            delete.Foreground = (Brush)FindResource("DangerBrush");
        }

        menu.Items.Add(new Separator());
        AddMenuItem(menu, I18n.T("Menu.MoreOptions"), "\uE712", "Shift+F10", ShowNativeContextMenu);

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

        Point position = _lastContextMenuPoint;

        if (position.X <= 0 && position.Y <= 0 &&
            PageList.ItemContainerGenerator.ContainerFromItem(selected[0]) is ListBoxItem container)
        {
            position = container.PointToScreen(new Point(container.ActualWidth - 8, container.ActualHeight / 2));
        }

        bool shown = _shellContextMenu.Show(
            new WindowInteropHelper(this).Handle,
            selected.Select(item => item.FullPath).ToList(),
            (int)position.X,
            (int)position.Y);

        if (!shown)
        {
            ShowToast(I18n.T("Toast.CouldNotOpenMenu"));
        }

        _ = RefreshAsync();
    }

    private ContextMenu BuildEmptyAreaMenu()
    {
        var menu = new ContextMenu();

        AddMenuItem(menu, I18n.T("Menu.NewFolder"), "\uE8F4", "Ctrl+Shift+N", () => _ = CreateNewFolderAsync());
        AddMenuItem(menu, I18n.T("Sort.Refresh"), "\uE72C", "F5", () => _ = RefreshAsync());
        menu.Items.Add(new Separator());
        AddCheckableItem(menu, I18n.T("Sort.ShowHidden"), _settings.ShowHidden, ToggleShowHidden);
        menu.Items.Add(BuildGroupByMenu());
        bool iconsHidden = _desktopIconsService.AreHidden();
        AddCheckableItem(
            menu,
            I18n.T(iconsHidden ? "Menu.ShowDesktopIcons" : "Menu.HideDesktopIcons"),
            iconsHidden,
            () => SetDesktopIconsHidden(!_desktopIconsService.AreHidden()));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, I18n.T("Menu.OpenDesktopFolder"), "\uE838", null, () =>
        {
            OpenDesktopFolderButton_Click(this, new RoutedEventArgs());
        });
        AddMenuItem(menu, I18n.T("Menu.Settings"), "\uE713", null, OpenSettings);
        AddMenuItem(menu, I18n.T("Menu.Restart"), "\uE895", null, RestartApplication);
        menu.Items.Add(new Separator());
        MenuItem exit = AddMenuItem(menu, I18n.T("Menu.Exit"), "\uE8BB", null, ExitApplication);
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
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(TextBlock.FontSizeProperty, "FontSize13");

        if (FindResource("TextMutedBrush") is Brush brush)
        {
            icon.Foreground = brush;
        }

        return icon;
    }
}
