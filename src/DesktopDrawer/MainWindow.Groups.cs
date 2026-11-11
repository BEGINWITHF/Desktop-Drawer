using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDrawer.Models;
using DesktopDrawer.Services;

namespace DesktopDrawer;

public partial class MainWindow
{
    private void BuildCustomGroupRows()
    {
        CustomGroupsPanel.Children.Clear();

        if (_settings.CustomGroups.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = I18n.T("Groups.Empty"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 2, 10, 0)
            };
            empty.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            CustomGroupsPanel.Children.Add(empty);
            return;
        }

        foreach (CustomGroup group in _settings.CustomGroups)
        {
            int index = _settings.CustomGroups.IndexOf(group);

            var row = new Grid { Margin = new Thickness(0, 0, 10, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBox = new TextBox
            {
                Text = group.Name,
                Style = (Style)FindResource("PickerValueBox"),
                Height = 26,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            nameBox.SetResourceReference(Control.FontFamilyProperty, "UiFont");
            nameBox.SetResourceReference(Control.FontSizeProperty, "FontSize12");
            nameBox.LostFocus += (_, _) => CommitCustomGroupRename(group, nameBox);
            nameBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Keyboard.ClearFocus();
                }
                else if (e.Key == Key.Escape)
                {
                    nameBox.Text = group.Name;
                    e.Handled = true;
                    Keyboard.ClearFocus();
                }
            };

            int memberCount = CountItemsInCustomGroup(group);
            string itemCount = memberCount == 1
                ? I18n.T("Groups.ItemOne")
                : I18n.F("Groups.ItemCount", memberCount);

            var count = new TextBlock
            {
                Text = itemCount,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            count.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
            count.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

            Button up = CreateGroupRowButton(
                "\uE70E",
                I18n.T("Groups.MoveUp"),
                index > 0,
                () => MoveCustomGroup(group, -1));

            Button down = CreateGroupRowButton(
                "\uE70D",
                I18n.T("Groups.MoveDown"),
                index < _settings.CustomGroups.Count - 1,
                () => MoveCustomGroup(group, 1));

            Button delete = CreateGroupRowButton(
                "\uE74D",
                I18n.T("Groups.Delete"),
                true,
                () => DeleteCustomGroup(group));

            Grid.SetColumn(count, 1);
            Grid.SetColumn(up, 2);
            Grid.SetColumn(down, 3);
            Grid.SetColumn(delete, 4);
            row.Children.Add(nameBox);
            row.Children.Add(count);
            row.Children.Add(up);
            row.Children.Add(down);
            row.Children.Add(delete);

            var rulesRow = new Grid { Margin = new Thickness(0, 6, 10, 0) };
            rulesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rulesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rulesLabel = new TextBlock
            {
                Text = I18n.T("Groups.Extensions"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0)
            };
            rulesLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
            rulesLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

            var extensionBox = new TextBox
            {
                Text = CustomGroup.FormatExtensions(group.Extensions),
                Style = (Style)FindResource("PickerValueBox"),
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = I18n.T("Groups.ExtensionsHint")
            };
            extensionBox.SetResourceReference(Control.FontFamilyProperty, "UiFont");
            extensionBox.SetResourceReference(Control.FontSizeProperty, "FontSize105");
            extensionBox.LostFocus += (_, _) => CommitCustomGroupExtensions(group, extensionBox);
            extensionBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Keyboard.ClearFocus();
                }
                else if (e.Key == Key.Escape)
                {
                    extensionBox.Text = CustomGroup.FormatExtensions(group.Extensions);
                    e.Handled = true;
                    Keyboard.ClearFocus();
                }
            };

            Grid.SetColumn(rulesLabel, 0);
            Grid.SetColumn(extensionBox, 1);
            rulesRow.Children.Add(rulesLabel);
            rulesRow.Children.Add(extensionBox);

            var entry = new StackPanel();
            entry.Children.Add(row);
            entry.Children.Add(rulesRow);

            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 4, 0, 4),
                Child = entry
            };
            border.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
            CustomGroupsPanel.Children.Add(border);
        }
    }

    private Button CreateGroupRowButton(string glyph, string tooltip, bool enabled, Action action)
    {
        var button = new Button
        {
            Style = (Style)FindResource("PanelIconButton"),
            Width = 20,
            Height = 20,
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = tooltip,
            IsEnabled = enabled
        };

        button.Content = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        button.Click += (_, _) => action();
        return button;
    }

    private void CommitCustomGroupRename(CustomGroup group, TextBox box)
    {
        string name = box.Text.Trim();

        if (string.Equals(name, group.Name, StringComparison.Ordinal))
        {
            return;
        }

        if (name.Length == 0)
        {
            ShowToast(I18n.T("Toast.GroupNameEmpty"));
            box.Text = group.Name;
            return;
        }

        if (string.Equals(name, I18n.T("Group.Ungrouped"), StringComparison.OrdinalIgnoreCase))
        {
            ShowToast(I18n.T("Toast.GroupNameReserved"));
            box.Text = group.Name;
            return;
        }

        if (_settings.CustomGroups.Any(other =>
                !ReferenceEquals(other, group) &&
                string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowToast(I18n.T("Toast.GroupAlreadyExists"));
            box.Text = group.Name;
            return;
        }

        group.Name = name;
        SaveSettings();
        BuildCustomGroupRows();

        if (_settings.GroupMode == GroupMode.Custom)
        {
            RebuildFilteredItems();
            UpdatePage();
        }

        ShowToast(I18n.F("Toast.GroupRenamed", name));
    }

    private int CountItemsInCustomGroup(CustomGroup group) =>
        _allItems.Count(item => IsItemInCustomGroup(group, item));

    private void CommitCustomGroupExtensions(CustomGroup group, TextBox box)
    {
        List<string> extensions = CustomGroup.ParseExtensions(box.Text);
        box.Text = CustomGroup.FormatExtensions(extensions);

        if (extensions.SequenceEqual(group.Extensions))
        {
            return;
        }

        group.Extensions = extensions;
        group.Excluded.Clear();

        if (extensions.Count > 0)
        {
            _settings.GroupMode = GroupMode.Custom;
        }

        SaveSettings();
        BuildCustomGroupRows();
        RebuildFilteredItems();
        _pageIndex = 0;
        UpdatePage();

        ShowToast(I18n.F("Toast.GroupRulesSaved", group.Name));
    }

    private void MoveCustomGroup(CustomGroup group, int delta)
    {
        int index = _settings.CustomGroups.IndexOf(group);
        int target = index + delta;

        if (index < 0 || target < 0 || target >= _settings.CustomGroups.Count)
        {
            return;
        }

        (_settings.CustomGroups[index], _settings.CustomGroups[target]) =
            (_settings.CustomGroups[target], _settings.CustomGroups[index]);

        SaveSettings();
        BuildCustomGroupRows();

        if (_settings.GroupMode == GroupMode.Custom)
        {
            RebuildFilteredItems();
            UpdatePage();
        }
    }

    private void AddCustomGroupButton_Click(object sender, RoutedEventArgs e) => AddCustomGroupFromInput();

    private void NewGroupNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddCustomGroupFromInput();
            e.Handled = true;
        }
    }

    private void AddCustomGroupFromInput()
    {
        string name = NewGroupNameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowToast(I18n.T("Toast.GroupNameEmpty"));
            return;
        }

        if (string.Equals(name, I18n.T("Group.Ungrouped"), StringComparison.OrdinalIgnoreCase))
        {
            ShowToast(I18n.T("Toast.GroupNameReserved"));
            return;
        }

        if (_settings.CustomGroups.Any(group =>
                string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowToast(I18n.T("Toast.GroupAlreadyExists"));
            return;
        }

        _settings.CustomGroups.Add(new CustomGroup { Name = name });
        _settings.GroupMode = GroupMode.Custom;
        NewGroupNameBox.Text = string.Empty;
        SaveSettings();
        BuildCustomGroupRows();
        RebuildFilteredItems();
        _pageIndex = 0;
        UpdatePage();
        ShowToast(I18n.F("Toast.GroupCreated", name));
    }

    private void DeleteCustomGroup(CustomGroup group)
    {
        if (!_settings.CustomGroups.Remove(group))
        {
            return;
        }

        SaveSettings();
        BuildCustomGroupRows();
        ShowToast(I18n.F("Toast.GroupDeleted", group.Name));

        if (_settings.GroupMode == GroupMode.Custom)
        {
            RebuildFilteredItems();
            _pageIndex = 0;
            UpdatePage();
        }
    }

    private void AssignToGroup(CustomGroup group)
    {
        List<DesktopItem> selected = PageList.SelectedItems.Cast<DesktopItem>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        List<string> keys = selected.Select(item => PathKey(item.FullPath)).ToList();
        bool allInGroup = selected.All(item => IsItemInCustomGroup(group, item));

        if (allInGroup)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                group.Items.Remove(keys[i]);

                if (group.MatchesExtension(selected[i].Name) &&
                    !group.Excluded.Contains(keys[i]))
                {
                    group.Excluded.Add(keys[i]);
                }
            }

            ShowToast(I18n.F("Toast.RemovedFromGroup", group.Name));
        }
        else
        {
            foreach (CustomGroup other in _settings.CustomGroups)
            {
                other.Items.RemoveAll(key => keys.Contains(key));
                other.Excluded.RemoveAll(key => keys.Contains(key));
            }

            foreach (string key in keys)
            {
                if (!group.Items.Contains(key))
                {
                    group.Items.Add(key);
                }
            }

            _settings.GroupMode = GroupMode.Custom;
            ShowToast(I18n.F("Toast.AddedToGroup", group.Name));
        }

        SaveSettings();
        RebuildFilteredItems();
        NavigateToItem(selected[0]);

        if (_settingsOpen)
        {
            BuildCustomGroupRows();
        }
    }

    private void UnassignSelectedFromGroups()
    {
        List<DesktopItem> selected = PageList.SelectedItems.Cast<DesktopItem>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var keys = selected
            .Select(item => PathKey(item.FullPath))
            .ToHashSet(StringComparer.Ordinal);

        bool changed = false;
        foreach (CustomGroup group in _settings.CustomGroups)
        {
            if (group.Items.RemoveAll(key => keys.Contains(key)) > 0)
            {
                changed = true;
            }

            foreach (DesktopItem item in selected)
            {
                string key = PathKey(item.FullPath);

                if (group.MatchesExtension(item.Name) &&
                    !group.Excluded.Contains(key))
                {
                    group.Excluded.Add(key);
                    changed = true;
                }
            }
        }

        if (!changed)
        {
            return;
        }

        SaveSettings();
        RebuildFilteredItems();
        NavigateToItem(selected[0]);

        if (_settingsOpen)
        {
            BuildCustomGroupRows();
        }

        ShowToast(I18n.T("Toast.Unassigned"));
    }

    private void NavigateToItem(DesktopItem item)
    {
        int capacity = Math.Max(1, Columns * Rows);
        List<PageSlice> pages = BuildPages(capacity);
        int pageIndex = pages.FindIndex(page => page.Items.Contains(item));

        _pageIndex = pageIndex >= 0 ? pageIndex : 0;
        UpdatePage(selectPath: item.FullPath);
    }

    private void PruneCustomGroups()
    {
        if (_settings.CustomGroups.Count == 0 || _allItems.Count == 0)
        {
            return;
        }

        var paths = _allItems
            .Select(item => PathKey(item.FullPath))
            .ToHashSet(StringComparer.Ordinal);

        bool changed = false;
        foreach (CustomGroup group in _settings.CustomGroups)
        {
            if (group.Items.RemoveAll(key => !paths.Contains(key)) > 0)
            {
                changed = true;
            }

            if (group.Excluded.RemoveAll(key => !paths.Contains(key)) > 0)
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        SaveSettings();

        if (_settingsOpen)
        {
            BuildCustomGroupRows();
        }
    }

    private void OpenGroupsSettings()
    {
        OpenSettings();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            CustomGroupsSection.BringIntoView();
            NewGroupNameBox.Focus();
        }), DispatcherPriority.Background);
    }

    private MenuItem BuildGroupsMenu(IReadOnlyList<DesktopItem> selected)
    {
        var menu = new MenuItem { Header = I18n.T("Menu.Groups") };

        if (_settings.CustomGroups.Count == 0)
        {
            var newGroup = new MenuItem { Header = I18n.T("Menu.NewGroup") };
            newGroup.Click += (_, _) => OpenGroupsSettings();
            menu.Items.Add(newGroup);

            var manage = new MenuItem { Header = I18n.T("Menu.ManageGroups") };
            manage.Click += (_, _) => OpenGroupsSettings();
            menu.Items.Add(manage);
            return menu;
        }

        foreach (CustomGroup group in _settings.CustomGroups)
        {
            bool allInGroup = selected.Count > 0 &&
                              selected.All(item => IsItemInCustomGroup(group, item));

            AddCheckableItem(menu.Items, group.Name, allInGroup, () => AssignToGroup(group));
        }

        menu.Items.Add(new Separator());

        var newGroupItem = new MenuItem { Header = I18n.T("Menu.NewGroup") };
        newGroupItem.Click += (_, _) => OpenGroupsSettings();
        menu.Items.Add(newGroupItem);

        var remove = new MenuItem { Header = I18n.T("Menu.RemoveFromGroup") };
        remove.Click += (_, _) => UnassignSelectedFromGroups();
        menu.Items.Add(remove);

        var manageItem = new MenuItem { Header = I18n.T("Menu.ManageGroups") };
        manageItem.Click += (_, _) => OpenGroupsSettings();
        menu.Items.Add(manageItem);

        return menu;
    }
}
