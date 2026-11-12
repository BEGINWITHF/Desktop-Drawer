using System.IO;
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
    /// <summary>
    /// Seeds the groups that mirror the built-in type categories the first time
    /// the custom-group feature runs. Names follow the current UI language.
    /// </summary>
    private void EnsureDefaultCustomGroups()
    {
        if (_settings.CustomGroupsSeeded)
        {
            return;
        }

        _settings.CustomGroupsSeeded = true;

        foreach (CustomGroup group in CreateDefaultCustomGroups())
        {
            bool exists = _settings.CustomGroups.Any(existing =>
                string.Equals(existing.Name, group.Name, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                _settings.CustomGroups.Add(group);
            }
        }

        SaveSettings();
    }

    private static List<CustomGroup> CreateDefaultCustomGroups()
    {
        (string Key, string[] Extensions)[] defaults =
        [
            ("Group.Documents", [".doc", ".docx", ".pdf", ".txt", ".md", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".rtf", ".odt"]),
            ("Group.Images", [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".psd", ".tif", ".tiff"]),
            ("Group.Videos", [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v"]),
            ("Group.Audio", [".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma"]),
            ("Group.Archives", [".zip", ".rar", ".7z", ".tar", ".gz", ".xz", ".bz2", ".iso"]),
            ("Group.Programs", [".exe", ".msi", ".bat", ".cmd", ".ps1", ".lnk"])
        ];

        var groups = new List<CustomGroup>();

        foreach ((string key, string[] extensions) in defaults)
        {
            groups.Add(new CustomGroup
            {
                Name = I18n.T(key),
                Extensions = CustomGroup.NormalizeExtensions(extensions)
            });
        }

        return groups;
    }

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

            var rulesPanel = new StackPanel { Margin = new Thickness(0, 6, 10, 0) };

            var rulesLabel = new TextBlock
            {
                Text = I18n.T("Groups.Extensions"),
                Margin = new Thickness(2, 0, 0, 4)
            };
            rulesLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
            rulesLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            rulesPanel.Children.Add(rulesLabel);

            if (group.IncludeFolders || group.Extensions.Count > 0)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

                if (group.IncludeFolders)
                {
                    chips.Children.Add(CreateFolderChip(group));

                    foreach (string folderName in group.FolderNames)
                    {
                        chips.Children.Add(CreateFolderNameChip(group, folderName));
                    }
                }

                foreach (string extension in group.Extensions)
                {
                    chips.Children.Add(CreateExtensionChip(group, extension));
                }

                rulesPanel.Children.Add(chips);
            }

            var inputRow = new Grid();
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var extensionBox = new TextBox
            {
                Style = (Style)FindResource("PickerValueBox"),
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = I18n.T("Groups.ExtensionsHint")
            };
            extensionBox.SetResourceReference(Control.FontFamilyProperty, "UiFont");
            extensionBox.SetResourceReference(Control.FontSizeProperty, "FontSize105");
            extensionBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    AddExtensionsFromInput(group, extensionBox);
                }
            };

            var addButton = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = I18n.T("Groups.TypeAdd"),
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0)
            };
            addButton.Click += (_, _) => AddExtensionsFromInput(group, extensionBox);

            var browseButton = new Button
            {
                Style = (Style)FindResource("PanelIconButton"),
                Width = 24,
                Height = 24,
                ToolTip = I18n.T("Groups.TypeBrowse")
            };
            browseButton.Content = new TextBlock
            {
                Text = "\uE8E5",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            browseButton.Click += (_, _) => BrowseExtensionsForGroup(group);

            Grid.SetColumn(extensionBox, 0);
            Grid.SetColumn(addButton, 1);
            Grid.SetColumn(browseButton, 2);
            inputRow.Children.Add(extensionBox);
            inputRow.Children.Add(addButton);
            inputRow.Children.Add(browseButton);
            rulesPanel.Children.Add(inputRow);

            if (group.IncludeFolders)
            {
                var folderLabel = new TextBlock
                {
                    Text = I18n.T("Groups.FolderNames"),
                    Margin = new Thickness(2, 2, 0, 4)
                };
                folderLabel.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
                folderLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

                var folderRow = new Grid();
                folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var folderBox = new TextBox
                {
                    Style = (Style)FindResource("PickerValueBox"),
                    Height = 24,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    ToolTip = I18n.T("Groups.FolderNamesHint")
                };
                folderBox.SetResourceReference(Control.FontFamilyProperty, "UiFont");
                folderBox.SetResourceReference(Control.FontSizeProperty, "FontSize105");
                folderBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        e.Handled = true;
                        AddFolderNamesFromInput(group, folderBox);
                    }
                };

                var folderAddButton = new Button
                {
                    Style = (Style)FindResource("GhostButton"),
                    Content = I18n.T("Groups.TypeAdd"),
                    Height = 24,
                    Padding = new Thickness(10, 0, 10, 0)
                };
                folderAddButton.Click += (_, _) => AddFolderNamesFromInput(group, folderBox);

                Grid.SetColumn(folderBox, 0);
                Grid.SetColumn(folderAddButton, 1);
                folderRow.Children.Add(folderBox);
                folderRow.Children.Add(folderAddButton);

                rulesPanel.Children.Add(folderLabel);
                rulesPanel.Children.Add(folderRow);
            }
            else
            {
                var foldersButton = new Button
                {
                    Style = (Style)FindResource("GhostButton"),
                    Content = I18n.T("Groups.TypeFolders"),
                    Height = 22,
                    Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                foldersButton.Click += (_, _) => AddFoldersToCustomGroup(group);
                rulesPanel.Children.Add(foldersButton);
            }

            var entry = new StackPanel();
            entry.Children.Add(row);
            entry.Children.Add(rulesPanel);

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

    private Border CreateExtensionChip(CustomGroup group, string extension) =>
        CreateGroupChip(
            extension.TrimStart('.'),
            () => RemoveCustomGroupExtension(group, extension));

    private Border CreateFolderChip(CustomGroup group) =>
        CreateGroupChip(
            I18n.T("Group.Folders"),
            () => RemoveFoldersFromCustomGroup(group));

    private Border CreateFolderNameChip(CustomGroup group, string folderName) =>
        CreateGroupChip(
            folderName,
            () => RemoveFolderName(group, folderName));

    private Border CreateGroupChip(string text, Action remove)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2, 3, 2),
            Margin = new Thickness(0, 0, 4, 4)
        };
        chip.SetResourceReference(Border.BackgroundProperty, "BadgePanelBrush");
        chip.SetResourceReference(Border.BorderBrushProperty, "StrokeStrongBrush");

        var content = new StackPanel { Orientation = Orientation.Horizontal };

        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var removeButton = new Button
        {
            Style = (Style)FindResource("PanelIconButton"),
            Width = 16,
            Height = 16,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = I18n.T("Groups.TypeRemove")
        };
        removeButton.Content = new TextBlock
        {
            Text = "\uE711",
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        removeButton.Click += (_, _) => remove();

        content.Children.Add(label);
        content.Children.Add(removeButton);
        chip.Child = content;
        return chip;
    }

    private void AddExtensionsFromInput(CustomGroup group, TextBox box)
    {
        List<string> extensions = CustomGroup.ParseExtensions(box.Text);

        if (extensions.Count == 0)
        {
            box.Focus();
            return;
        }

        box.Text = string.Empty;
        AddCustomGroupExtensions(group, extensions);
    }

    private void AddCustomGroupExtensions(CustomGroup group, IEnumerable<string> extensions)
    {
        bool changed = false;

        foreach (string extension in extensions)
        {
            string? normalized = CustomGroup.NormalizeExtension(extension);

            if (normalized is not null && !group.Extensions.Contains(normalized))
            {
                group.Extensions.Add(normalized);
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        group.Excluded.Clear();

        if (group.Extensions.Count > 0)
        {
            _settings.GroupMode = GroupMode.Custom;
        }

        ApplyCustomGroupRuleChange();
    }

    private void RemoveCustomGroupExtension(CustomGroup group, string extension)
    {
        if (!group.Extensions.Remove(extension))
        {
            return;
        }

        ApplyCustomGroupRuleChange();
    }

    private void AddFoldersToCustomGroup(CustomGroup group)
    {
        if (group.IncludeFolders)
        {
            return;
        }

        group.IncludeFolders = true;
        group.Excluded.Clear();
        _settings.GroupMode = GroupMode.Custom;
        ApplyCustomGroupRuleChange();
    }

    private void RemoveFoldersFromCustomGroup(CustomGroup group)
    {
        if (!group.IncludeFolders)
        {
            return;
        }

        group.IncludeFolders = false;
        group.FolderNames.Clear();
        ApplyCustomGroupRuleChange();
    }

    private void AddFolderNamesFromInput(CustomGroup group, TextBox box)
    {
        List<string> names = CustomGroup.ParseFolderNames(box.Text);

        if (names.Count == 0)
        {
            box.Focus();
            return;
        }

        box.Text = string.Empty;
        bool changed = false;

        foreach (string name in names)
        {
            if (!group.FolderNames.Contains(name))
            {
                group.FolderNames.Add(name);
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        group.IncludeFolders = true;
        group.Excluded.Clear();
        _settings.GroupMode = GroupMode.Custom;
        ApplyCustomGroupRuleChange();
    }

    private void RemoveFolderName(CustomGroup group, string folderName)
    {
        if (!group.FolderNames.Remove(folderName))
        {
            return;
        }

        ApplyCustomGroupRuleChange();
    }

    private void ApplyCustomGroupRuleChange()
    {
        SaveSettings();
        BuildCustomGroupRows();
        RebuildFilteredItems();
        _pageIndex = 0;
        UpdatePage();
    }

    private void BrowseExtensionsForGroup(CustomGroup group)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = I18n.T("Groups.TypeBrowse"),
            Multiselect = true,
            CheckFileExists = true,
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        List<string> extensions = dialog.FileNames
            .Select(Path.GetExtension)
            .Select(CustomGroup.NormalizeExtension)
            .Where(extension => extension is not null)
            .Select(extension => extension!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (extensions.Count == 0)
        {
            ShowToast(I18n.T("Toast.FileTypeNoExtension"));
            return;
        }

        AddCustomGroupExtensions(group, extensions);
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

                bool matchesRule = group.Matches(selected[i].Name, selected[i].IsFolder);

                if (matchesRule && !group.Excluded.Contains(keys[i]))
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

                bool matchesRule = group.Matches(item.Name, item.IsFolder);

                if (matchesRule && !group.Excluded.Contains(key))
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
