using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopDrawer.Models;
using DesktopDrawer.Services;

namespace DesktopDrawer;

public partial class MainWindow
{
    private CustomGroup? _selectedGroup;
    private bool _groupReorderMode;
    private CustomGroup? _draggingGroup;
    private Border? _groupDropLine;

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
        CustomGroupsPanel.AllowDrop = true;
        CustomGroupsPanel.Background = Brushes.Transparent;
        CustomGroupsPanel.DragOver -= GroupsPanel_DragOver;
        CustomGroupsPanel.DragOver += GroupsPanel_DragOver;
        CustomGroupsPanel.DragLeave -= GroupsPanel_DragLeave;
        CustomGroupsPanel.DragLeave += GroupsPanel_DragLeave;
        CustomGroupsPanel.Drop -= GroupsPanel_Drop;
        CustomGroupsPanel.Drop += GroupsPanel_Drop;

        CustomGroupsPanel.Children.Clear();
        GroupEditorPanel.Children.Clear();
        GroupDropCanvas.Children.Clear();
        _groupDropLine = null;

        if (_selectedGroup is null || !_settings.CustomGroups.Contains(_selectedGroup))
        {
            _selectedGroup = _settings.CustomGroups.FirstOrDefault();
        }

        if (_settings.CustomGroups.Count <= 1)
        {
            _groupReorderMode = false;
        }

        GroupReorderButton.Visibility = _settings.CustomGroups.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        SortGroupsButton.Visibility = _groupReorderMode && _settings.CustomGroups.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        GroupReorderButton.Content = I18n.T(
            _groupReorderMode ? "Groups.ReorderDone" : "Groups.Reorder");
        GroupReorderButton.Background = _groupReorderMode
            ? (Brush)FindResource("AccentBrush")
            : Brushes.Transparent;
        GroupReorderButton.Foreground = _groupReorderMode
            ? (Brush)FindResource("OnAccentBrush")
            : (Brush)FindResource("TextSecondaryBrush");
        UpdateGroupReorderHint();

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

            var noSelection = new TextBlock
            {
                Text = I18n.T("Groups.NoSelection"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 2, 10, 0)
            };
            noSelection.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
            noSelection.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            GroupEditorPanel.Children.Add(noSelection);
            return;
        }

        foreach (CustomGroup group in _settings.CustomGroups)
        {
            CustomGroupsPanel.Children.Add(CreateGroupDirectoryItem(group));
        }

        if (_selectedGroup is not null)
        {
            BuildGroupEditor(_selectedGroup);
        }
    }

    private void GroupsPanel_DragOver(object sender, DragEventArgs e)
    {
        if (!_groupReorderMode || !e.Data.GetDataPresent(typeof(CustomGroup)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        CollapseDraggedRow();
        Point position = e.GetPosition(CustomGroupsPanel);
        ShowGroupDropMarker(GetGroupInsertIndex(position));
        UpdateGroupRowHover(position);
        e.Handled = true;
    }

    private void CollapseDraggedRow()
    {
        if (_draggingGroup is null)
        {
            return;
        }

        foreach (UIElement child in CustomGroupsPanel.Children)
        {
            if (child is Button button &&
                button.Tag is CustomGroup group &&
                ReferenceEquals(group, _draggingGroup) &&
                button.Visibility == Visibility.Visible)
            {
                button.Visibility = Visibility.Collapsed;
                CustomGroupsPanel.UpdateLayout();
                return;
            }
        }
    }

    private void GroupsPanel_DragLeave(object sender, DragEventArgs e)
    {
        HideGroupDropMarker();
        ResetGroupRowOpacity();
    }

    private void GroupsPanel_Drop(object sender, DragEventArgs e)
    {
        HideGroupDropMarker();
        ResetGroupRowOpacity();

        if (!_groupReorderMode ||
            e.Data.GetData(typeof(CustomGroup)) is not CustomGroup source ||
            !_settings.CustomGroups.Contains(source))
        {
            return;
        }

        int insertIndex = GetGroupInsertIndex(e.GetPosition(CustomGroupsPanel));

        _settings.CustomGroups.Remove(source);
        insertIndex = Math.Clamp(insertIndex, 0, _settings.CustomGroups.Count);
        _settings.CustomGroups.Insert(insertIndex, source);
        SaveSettings();

        _draggingGroup = null;
        BuildCustomGroupRows();

        if (_settings.GroupMode == GroupMode.Custom)
        {
            RebuildFilteredItems();
            UpdatePage();
        }

        e.Handled = true;
    }

    private void ShowGroupDropMarker(int insertIndex)
    {
        if (_groupDropLine is null)
        {
            _groupDropLine = new Border
            {
                Height = 2,
                IsHitTestVisible = false
            };
            _groupDropLine.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            GroupDropCanvas.Children.Add(_groupDropLine);
        }

        double y = 0;
        int seen = 0;

        foreach (UIElement child in CustomGroupsPanel.Children)
        {
            if (child is not Button button ||
                button.Tag is not CustomGroup ||
                button.Visibility != Visibility.Visible ||
                button.ActualHeight <= 0)
            {
                continue;
            }

            Point origin = button.TranslatePoint(new Point(0, 0), CustomGroupsPanel);

            if (seen == insertIndex)
            {
                y = origin.Y;
                break;
            }

            seen++;
            y = origin.Y + button.ActualHeight;
        }

        _groupDropLine.Width = Math.Max(1, CustomGroupsPanel.ActualWidth);
        Canvas.SetLeft(_groupDropLine, 0);
        Canvas.SetTop(_groupDropLine, Math.Max(0, y - 1));
        _groupDropLine.Visibility = Visibility.Visible;
    }

    private void HideGroupDropMarker()
    {
        if (_groupDropLine is not null)
        {
            _groupDropLine.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateGroupRowHover(Point position)
    {
        foreach (UIElement child in CustomGroupsPanel.Children)
        {
            if (child is not Button button ||
                button.Tag is not CustomGroup group ||
                button.Visibility != Visibility.Visible)
            {
                continue;
            }

            Point origin = button.TranslatePoint(new Point(0, 0), CustomGroupsPanel);
            bool inside = position.Y >= origin.Y &&
                          position.Y <= origin.Y + button.ActualHeight;

            button.Opacity = ReferenceEquals(group, _draggingGroup)
                ? 0.4
                : inside ? 0.72 : 1;
        }
    }

    private void ResetGroupRowOpacity()
    {
        foreach (UIElement child in CustomGroupsPanel.Children)
        {
            if (child is Button button && button.Tag is CustomGroup)
            {
                button.Opacity = 1;
            }
        }
    }

    private void UpdateGroupReorderHint()
    {
        if (!_groupReorderMode)
        {
            GroupReorderHint.Visibility = Visibility.Collapsed;
            GroupReorderHint.Text = string.Empty;
            return;
        }

        GroupReorderHint.Visibility = Visibility.Visible;
        GroupReorderHint.Text = _draggingGroup is null
            ? I18n.T("Groups.ReorderHint")
            : I18n.F("Groups.ReorderMoving", _draggingGroup.Name);
    }

    private void ToggleGroupReorder_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.CustomGroups.Count <= 1)
        {
            return;
        }

        _groupReorderMode = !_groupReorderMode;
        _draggingGroup = null;
        HideGroupDropMarker();
        BuildCustomGroupRows();
    }

    private int GetGroupInsertIndex(Point position)
    {
        int index = 0;

        foreach (UIElement child in CustomGroupsPanel.Children)
        {
            if (child is not Button button ||
                button.Tag is not CustomGroup ||
                button.Visibility != Visibility.Visible ||
                button.ActualHeight <= 0)
            {
                continue;
            }

            Point origin = button.TranslatePoint(new Point(0, 0), CustomGroupsPanel);

            if (position.Y < origin.Y + button.ActualHeight / 2)
            {
                return index;
            }

            index++;
        }

        return index;
    }

    private Button CreateGroupDirectoryItem(CustomGroup group)
    {
        bool selected = ReferenceEquals(_selectedGroup, group);
        int memberCount = CountItemsInCustomGroup(group);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = group.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        name.SetResourceReference(TextBlock.FontSizeProperty, "FontSize12");
        name.SetResourceReference(
            TextBlock.ForegroundProperty,
            selected ? "TextPrimaryBrush" : "TextSecondaryBrush");

        if (_groupReorderMode)
        {
            var handle = new TextBlock
            {
                Text = "\uE76F",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            handle.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            content.Children.Add(handle);
        }

        var count = new TextBlock
        {
            Text = memberCount == 1
                ? I18n.T("Groups.ItemOne")
                : I18n.F("Groups.ItemCount", memberCount),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        count.SetResourceReference(TextBlock.FontSizeProperty, "FontSize95");
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        Grid.SetColumn(name, 1);
        Grid.SetColumn(count, 2);
        content.Children.Add(name);
        content.Children.Add(count);

        var button = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Height = 30,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(8, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content,
            Tag = group,
            Cursor = _groupReorderMode ? Cursors.SizeAll : Cursors.Hand,
            Background = selected
                ? (Brush)FindResource("PanelHoverBrush")
                : Brushes.Transparent,
            BorderBrush = selected
                ? (Brush)FindResource("StrokeStrongBrush")
                : Brushes.Transparent
        };

        Point dragStart = default;
        bool dragging = false;
        bool dragged = false;

        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(button);
            dragged = false;
        };

        button.PreviewMouseMove += (_, e) =>
        {
            if (!_groupReorderMode ||
                dragging ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point position = e.GetPosition(button);

            if (Math.Abs(position.X - dragStart.X) < 6 &&
                Math.Abs(position.Y - dragStart.Y) < 6)
            {
                return;
            }

            dragging = true;
            dragged = true;
            _draggingGroup = group;
            button.Opacity = 0.4;
            UpdateGroupReorderHint();

            DragDrop.DoDragDrop(button, group, DragDropEffects.Move);

            dragging = false;
            _draggingGroup = null;
            button.Visibility = Visibility.Visible;
            button.Opacity = 1;
            HideGroupDropMarker();
            ResetGroupRowOpacity();
            UpdateGroupReorderHint();
        };

        button.Click += (_, _) =>
        {
            if (dragged)
            {
                dragged = false;
                return;
            }

            _selectedGroup = group;
            BuildCustomGroupRows();
        };
        return button;
    }

    private void BuildGroupEditor(CustomGroup group)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 4, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBox = new TextBox
        {
            Text = group.Name,
            Style = (Style)FindResource("PickerValueBox"),
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        nameBox.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        nameBox.SetResourceReference(Control.FontSizeProperty, "FontSize13");
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
        var count = new TextBlock
        {
            Text = memberCount == 1
                ? I18n.T("Groups.ItemOne")
                : I18n.F("Groups.ItemCount", memberCount),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        count.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        Button delete = CreateGroupRowButton(
            "\uE74D",
            I18n.T("Groups.Delete"),
            true,
            () => DeleteCustomGroup(group));

        Grid.SetColumn(count, 1);
        Grid.SetColumn(delete, 2);
        header.Children.Add(nameBox);
        header.Children.Add(count);
        header.Children.Add(delete);
        GroupEditorPanel.Children.Add(header);

        GroupEditorPanel.Children.Add(CreateEditorLabel("Groups.Extensions", new Thickness(0, 16, 0, 6)));

        if (group.Extensions.Count > 0)
        {
            GroupEditorPanel.Children.Add(CreateExtensionChipRow(group));
        }

        GroupEditorPanel.Children.Add(CreateExtensionInputRow(group));

        GroupEditorPanel.Children.Add(CreateEditorDivider());
        GroupEditorPanel.Children.Add(CreateEditorLabel("Groups.FileNames", new Thickness(0, 0, 0, 6)));

        if (group.FileNameFilters.Count > 0)
        {
            GroupEditorPanel.Children.Add(CreateFileNameChipRow(group));
        }

        GroupEditorPanel.Children.Add(CreateFileNameInputRow(group));

        GroupEditorPanel.Children.Add(CreateEditorDivider());
        GroupEditorPanel.Children.Add(CreateEditorLabel("Group.Folders", new Thickness(0, 0, 0, 6)));

        if (group.IncludeFolders)
        {
            var folderRow = new StackPanel { Orientation = Orientation.Horizontal };
            folderRow.Children.Add(CreateFolderChip(group));

            var editFolderButton = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = I18n.T("Groups.EditFolderFilter"),
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(8, 0, 0, 0)
            };
            editFolderButton.Click += (_, _) => OpenFolderFilterDialog(group);
            folderRow.Children.Add(editFolderButton);

            GroupEditorPanel.Children.Add(folderRow);
        }
        else
        {
            var foldersButton = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = I18n.T("Groups.TypeFolders"),
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            foldersButton.Click += (_, _) => OpenFolderFilterDialog(group);
            GroupEditorPanel.Children.Add(foldersButton);
        }
    }

    private TextBlock CreateEditorLabel(string key, Thickness margin)
    {
        var label = new TextBlock
        {
            Text = I18n.T(key),
            Margin = margin
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        return label;
    }

    private Border CreateEditorDivider()
    {
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 14, 0, 10)
        };
        divider.SetResourceReference(Border.BackgroundProperty, "DividerBrush");
        return divider;
    }

    private WrapPanel CreateExtensionChipRow(CustomGroup group)
    {
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };

        foreach (string extension in group.Extensions)
        {
            chips.Children.Add(CreateExtensionChip(group, extension));
        }

        return chips;
    }

    private WrapPanel CreateFileNameChipRow(CustomGroup group)
    {
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };

        foreach (string filter in group.FileNameFilters)
        {
            chips.Children.Add(CreateFileNameChip(group, filter));
        }

        return chips;
    }

    private Grid CreateExtensionInputRow(CustomGroup group)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var extensionBox = new TextBox
        {
            Style = (Style)FindResource("PickerValueBox"),
            Height = 26,
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
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 6, 0)
        };
        addButton.Click += (_, _) => AddExtensionsFromInput(group, extensionBox);

        var browseButton = new Button
        {
            Style = (Style)FindResource("PanelIconButton"),
            Width = 26,
            Height = 26,
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
        row.Children.Add(extensionBox);
        row.Children.Add(addButton);
        row.Children.Add(browseButton);
        return row;
    }

    private Grid CreateFileNameInputRow(CustomGroup group)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBox = new TextBox
        {
            Style = (Style)FindResource("PickerValueBox"),
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = I18n.T("Groups.FileNamesHint")
        };
        nameBox.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        nameBox.SetResourceReference(Control.FontSizeProperty, "FontSize105");
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddFileNameFiltersFromInput(group, nameBox);
            }
        };

        var addButton = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Content = I18n.T("Groups.TypeAdd"),
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0)
        };
        addButton.Click += (_, _) => AddFileNameFiltersFromInput(group, nameBox);

        Grid.SetColumn(nameBox, 0);
        Grid.SetColumn(addButton, 1);
        row.Children.Add(nameBox);
        row.Children.Add(addButton);
        return row;
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
            group.FolderNames.Count == 0
                ? I18n.T("Groups.FolderChipAll")
                : I18n.F("Groups.FolderChip", string.Join(", ", group.FolderNames)),
            () => RemoveFoldersFromCustomGroup(group));

    private Border CreateFileNameChip(CustomGroup group, string filter) =>
        CreateGroupChip(
            filter,
            () => RemoveFileNameFilter(group, filter));

    private Border CreateGroupChip(string text, Action remove)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 2, 2, 2),
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

    private CustomGroup? _folderFilterGroup;
    private readonly List<string> _folderFilterDraft = [];

    private void OpenFolderFilterDialog(CustomGroup group)
    {
        _folderFilterGroup = group;
        _folderFilterDraft.Clear();
        _folderFilterDraft.AddRange(group.FolderNames);

        FolderFilterTitle.Text = I18n.T(group.IncludeFolders
            ? "Groups.FolderDialogEditTitle"
            : "Groups.FolderDialogTitle");
        FolderFilterInput.Text = string.Empty;
        BuildFolderFilterChips();

        FolderFilterOverlay.Visibility = Visibility.Visible;
        FolderFilterCard.BeginAnimation(OpacityProperty, null);
        FolderFilterCard.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130));
        FolderFilterCard.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        Dispatcher.BeginInvoke(new Action(() => FolderFilterInput.Focus()));
    }

    private void BuildFolderFilterChips()
    {
        FolderFilterChips.Children.Clear();

        if (_folderFilterDraft.Count == 0)
        {
            var all = new TextBlock
            {
                Text = I18n.T("Groups.FolderChipAll"),
                VerticalAlignment = VerticalAlignment.Center
            };
            all.SetResourceReference(TextBlock.FontSizeProperty, "FontSize105");
            all.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            FolderFilterChips.Children.Add(all);
            return;
        }

        foreach (string filter in _folderFilterDraft)
        {
            FolderFilterChips.Children.Add(CreateGroupChip(
                filter,
                () =>
                {
                    _folderFilterDraft.Remove(filter);
                    BuildFolderFilterChips();
                }));
        }
    }

    private void FolderFilterAdd_Click(object sender, RoutedEventArgs e) =>
        AddFolderFilterTokens();

    private void FolderFilterInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddFolderFilterTokens();
        }
    }

    private void AddFolderFilterTokens()
    {
        List<string> tokens = CustomGroup.ParseFolderNames(FolderFilterInput.Text);

        if (tokens.Count == 0)
        {
            FolderFilterInput.Focus();
            return;
        }

        FolderFilterInput.Text = string.Empty;

        foreach (string token in tokens)
        {
            TryAddFilter(_folderFilterDraft, token);
        }

        BuildFolderFilterChips();
    }

    private void FolderFilterApply_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderFilter(useFilter: true);

    private void FolderFilterSkip_Click(object sender, RoutedEventArgs e) =>
        ApplyFolderFilter(useFilter: false);

    private void FolderFilterCancel_Click(object sender, RoutedEventArgs e) =>
        CloseFolderFilterDialog();

    private void FolderFilterBackdrop_MouseDown(object sender, MouseButtonEventArgs e) =>
        CloseFolderFilterDialog();

    private void ApplyFolderFilter(bool useFilter)
    {
        if (_folderFilterGroup is null)
        {
            CloseFolderFilterDialog();
            return;
        }

        _folderFilterGroup.IncludeFolders = true;

        if (useFilter)
        {
            _folderFilterGroup.FolderNames = [.. _folderFilterDraft];
        }
        else
        {
            _folderFilterGroup.FolderNames.Clear();
        }

        _folderFilterGroup.Excluded.Clear();
        _settings.GroupMode = GroupMode.Custom;

        CloseFolderFilterDialog();
        ApplyCustomGroupRuleChange();
    }

    private void CloseFolderFilterDialog()
    {
        _folderFilterGroup = null;
        _folderFilterDraft.Clear();
        FolderFilterOverlay.Visibility = Visibility.Collapsed;
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

    private void AddFileNameFiltersFromInput(CustomGroup group, TextBox box)
    {
        List<string> filters = CustomGroup.ParseFileNameFilters(box.Text);

        if (filters.Count == 0)
        {
            box.Focus();
            return;
        }

        box.Text = string.Empty;
        bool changed = false;

        foreach (string filter in filters)
        {
            if (TryAddFilter(group.FileNameFilters, filter))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        group.Excluded.Clear();
        _settings.GroupMode = GroupMode.Custom;
        ApplyCustomGroupRuleChange();
    }

    private bool TryAddFilter(List<string> filters, string token)
    {
        string value = token.Trim().ToLowerInvariant();

        if (value.Length == 0)
        {
            return false;
        }

        string? similar = filters.FirstOrDefault(existing =>
            existing.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            value.Contains(existing, StringComparison.OrdinalIgnoreCase));

        if (similar is not null)
        {
            ShowToast(string.Equals(similar, value, StringComparison.OrdinalIgnoreCase)
                ? I18n.F("Toast.DuplicateFilter", value)
                : I18n.F("Toast.SimilarFilter", value, similar));
            return false;
        }

        filters.Add(value);
        return true;
    }

    private void RemoveFileNameFilter(CustomGroup group, string filter)
    {
        if (!group.FileNameFilters.Remove(filter))
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

    private void SortGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var byName = new MenuItem { Header = I18n.T("Groups.SortByName") };
        byName.Click += (_, _) => SortCustomGroups(byCount: false);
        menu.Items.Add(byName);

        var byCount = new MenuItem { Header = I18n.T("Groups.SortByCount") };
        byCount.Click += (_, _) => SortCustomGroups(byCount: true);
        menu.Items.Add(byCount);

        menu.PlacementTarget = SortGroupsButton;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void SortCustomGroups(bool byCount)
    {
        if (_settings.CustomGroups.Count < 2)
        {
            return;
        }

        if (byCount)
        {
            _settings.CustomGroups.Sort((a, b) =>
            {
                int compare = CountItemsInCustomGroup(b).CompareTo(CountItemsInCustomGroup(a));
                return compare != 0
                    ? compare
                    : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }
        else
        {
            _settings.CustomGroups.Sort((a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        SaveSettings();
        BuildCustomGroupRows();

        if (_settings.GroupMode == GroupMode.Custom)
        {
            RebuildFilteredItems();
            UpdatePage();
        }

        ShowToast(I18n.T("Toast.GroupsSorted"));
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

        var group = new CustomGroup { Name = name };
        _settings.CustomGroups.Add(group);
        _selectedGroup = group;
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

        if (ReferenceEquals(_selectedGroup, group))
        {
            _selectedGroup = null;
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

        if (_settingsOpen || _groupsOpen)
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

        if (_settingsOpen || _groupsOpen)
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

        if (_settingsOpen || _groupsOpen)
        {
            BuildCustomGroupRows();
        }
    }

    private void OpenGroupsSettings()
    {
        OpenGroupsPanel();
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
