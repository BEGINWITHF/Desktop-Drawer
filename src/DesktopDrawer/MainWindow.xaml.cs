using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopDrawer.Helpers;
using DesktopDrawer.Models;
using DesktopDrawer.Services;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DesktopDrawer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly DesktopService _desktopService;
    private readonly IconService _iconService;
    private readonly ShellService _shellService;
    private readonly DesktopIconsService _desktopIconsService;
    private readonly StartupService _startupService;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _resizeTimer;
    private readonly DispatcherTimer _chromeTimer;

    private List<DesktopItem> _allItems = [];
    private List<DesktopItem> _filteredItems = [];

    private int _pageIndex;
    private int _pageCount = 1;
    private int _columns = 8;
    private int _rows = 4;
    private double _iconSize = 66;
    private double _tileFontSize = 12.5;
    private double _tileNameHeight = 34;
    private double _tileWidth = 145;
    private double _tileHeight = 152;

    private string _searchText = string.Empty;
    private string _itemSummary = "Reading desktop...";
    private string _currentPageLabel = "Page 1 of 1";
    private bool _showPageDots;

    private bool _initialized;
    private bool _hasLoadedOnce;
    private bool _refreshing;
    private bool _refreshQueued;
    private string? _queuedSelectPath;
    private bool _updatingPage;
    private bool _initializingSettings = true;
    private bool _syncingIconToggles;
    private bool _desktopIconsChanged;
    private bool _initialIconsHidden;
    private bool _cleanedUp;
    private bool _isExiting;
    private bool _settingsOpen;
    private bool _trayHintShown;
    private bool _chromeVisible = true;
    private bool _iconsRestoredForTray;
    private bool _embeddedInDesktop;
    private Point _lastCursorPosition;
    private DateTime _lastChromeActivity = DateTime.UtcNow;

    private static readonly TimeSpan ChromeIdleDelay = TimeSpan.FromSeconds(2.4);

    private DesktopItem? _renamingItem;
    private Point _dragStart;
    private DesktopItem? _dragCandidate;
    private CancellationTokenSource? _prefetchCts;
    private double _wheelAccumulator;
    private DateTime _lastWheel;
    private DateTime _lastPageFlip;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _trayHideIconsItem;
    private WinForms.ToolStripMenuItem? _trayStartupItem;

    public MainWindow()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _desktopService = new DesktopService();
        _iconService = new IconService();
        _shellService = new ShellService();
        _desktopIconsService = new DesktopIconsService();
        _startupService = new StartupService();

        _initialIconsHidden = _desktopIconsService.AreHidden();
        IconWatchdog.Start();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += ToastTimer_Tick;
        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _resizeTimer.Tick += ResizeTimer_Tick;
        _chromeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _chromeTimer.Tick += ChromeTimer_Tick;

        InitializeComponent();
        DataContext = this;

        PageList.RenderTransform = new TranslateTransform();
        ApplyDensity(_settings.Density);
        PageList.ContextMenu = BuildEmptyAreaMenu();
        UpdateSortLabel();
        CloseButton.ToolTip = _settings.CloseToTray ? "Close to tray" : "Close";

        PreviewMouseDown += Window_PreviewMouseActivity;
        StateChanged += Window_StateChanged;

        _desktopService.DesktopChanged += OnDesktopChanged;
        Application.Current.SessionEnding += OnSessionEnding;

        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;
    }

    public ObservableCollection<DesktopItem> VisibleItems { get; } = [];
    public ObservableCollection<PageDot> PageDots { get; } = [];

    public int Columns
    {
        get => _columns;
        private set => SetField(ref _columns, value);
    }

    public int Rows
    {
        get => _rows;
        private set => SetField(ref _rows, value);
    }

    public double IconSize
    {
        get => _iconSize;
        private set => SetField(ref _iconSize, value);
    }

    public double TileFontSize
    {
        get => _tileFontSize;
        private set => SetField(ref _tileFontSize, value);
    }

    public double TileNameHeight
    {
        get => _tileNameHeight;
        private set => SetField(ref _tileNameHeight, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(_searchText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            SetField(ref _searchText, normalized);

            OnPropertyChanged(nameof(HasSearchText));

            if (_hasLoadedOnce)
            {
                RebuildFilteredItems();
                _pageIndex = 0;
                UpdatePage();
                PageList.UnselectAll();
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(_searchText);

    public string ItemSummary
    {
        get => _itemSummary;
        private set => SetField(ref _itemSummary, value);
    }

    public string CurrentPageLabel
    {
        get => _currentPageLabel;
        private set => SetField(ref _currentPageLabel, value);
    }

    public bool ShowPageDots
    {
        get => _showPageDots;
        private set => SetField(ref _showPageDots, value);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWorkAreaBounds();

        ImageSource? backdrop = WallpaperService.LoadWallpaper();
        if (backdrop is not null)
        {
            BackdropImage.Source = backdrop;
        }

        InitializeTray();

        if (_settings.StartWithWindows && !_startupService.IsEnabled())
        {
            if (!_startupService.SetEnabled(true))
            {
                _settings.StartWithWindows = false;
                SaveSettings();
            }
        }
        else if (_settings.StartWithWindows)
        {
            _startupService.EnsureRegistered();
        }

        _initializingSettings = true;
        SyncSettingsUi();
        _initializingSettings = false;
        UpdateDesktopIconsUi();

        if (_settings.HideIconsOnLaunch && !_desktopIconsService.AreHidden())
        {
            try
            {
                _desktopIconsService.SetHidden(true);
                _desktopIconsChanged = _desktopIconsService.AreHidden() != _initialIconsHidden;
                UpdateDesktopIconsUi();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Could not hide desktop icons on launch");
            }
        }

        UpdateLayout();
        RecalculateGrid(force: true);
        await RefreshAsync();

        // Become part of the desktop itself instead of a floating window.
        _embeddedInDesktop = DesktopHost.TryEmbed(this);

        _initialized = true;
        _lastCursorPosition = Mouse.GetPosition(this);
        ShowChrome();
        _chromeTimer.Start();

        if (!_settings.HasLaunched)
        {
            _settings.HasLaunched = true;
            SaveSettings();
            ShowToast("Move the mouse to show controls. Scroll or press PageDown to flip pages.");
        }
    }

    private void ApplyWorkAreaBounds()
    {
        Rect workArea = SystemParameters.WorkArea;
        WindowState = WindowState.Normal;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (!_initialized || WindowState != WindowState.Maximized)
        {
            return;
        }

        ApplyWorkAreaBounds();
    }

    private void OnDesktopChanged(object? sender, EventArgs e)
    {
        if (_cleanedUp)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = RefreshAsync()));
    }

    private async Task RefreshAsync(bool resetPage = false, string? selectPath = null)
    {
        if (_cleanedUp)
        {
            return;
        }

        if (_refreshing)
        {
            _refreshQueued = true;
            _queuedSelectPath = selectPath ?? _queuedSelectPath;
            return;
        }

        _refreshing = true;

        try
        {
            var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!resetPage && selectPath is null)
            {
                foreach (DesktopItem item in PageList.SelectedItems.Cast<DesktopItem>())
                {
                    selectedPaths.Add(item.FullPath);
                }
            }

            List<DesktopItem> loaded = await Task.Run(() => _desktopService.Load(_settings.ShowHidden));
            var existing = _allItems.ToDictionary(item => item.FullPath, StringComparer.OrdinalIgnoreCase);
            var merged = new List<DesktopItem>(loaded.Count);

            foreach (DesktopItem item in loaded)
            {
                if (existing.TryGetValue(item.FullPath, out DesktopItem? current))
                {
                    current.UpdateFrom(item);
                    merged.Add(current);
                }
                else
                {
                    merged.Add(item);
                }
            }

            _allItems = merged;
            _hasLoadedOnce = true;
            RebuildFilteredItems();

            if (resetPage)
            {
                _pageIndex = 0;
            }
            else if (!string.IsNullOrEmpty(selectPath))
            {
                int index = _filteredItems.FindIndex(
                    item => string.Equals(item.FullPath, selectPath, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    int capacity = Math.Max(1, Columns * Rows);
                    _pageIndex = index / capacity;
                }
            }

            UpdatePage(restoreSelection: selectedPaths, selectPath: selectPath);
            UpdateTrayText();
            StartPrefetch();

            if (!_hasLoadedOnce)
            {
                PageList.Focus();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Desktop refresh failed");
            ShowToast("Could not read the desktop. See the log for details.");
        }
        finally
        {
            _refreshing = false;

            if (_refreshQueued && !_cleanedUp)
            {
                _refreshQueued = false;
                string? queuedPath = _queuedSelectPath;
                _queuedSelectPath = null;
                _ = RefreshAsync(selectPath: queuedPath);
            }
        }
    }

    private void RebuildFilteredItems()
    {
        IEnumerable<DesktopItem> query = _allItems;

        if (HasSearchText)
        {
            string[] terms = _searchText.Split(
                [' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            query = query.Where(item => terms.All(term =>
                item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.TypeLabel.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var filtered = query.ToList();
        filtered.Sort(CompareItems);
        _filteredItems = filtered;
    }

    private int CompareItems(DesktopItem a, DesktopItem b)
    {
        if (_settings.FoldersFirst && a.IsFolder != b.IsFolder)
        {
            return a.IsFolder ? -1 : 1;
        }

        int result = _settings.SortMode switch
        {
            SortMode.DateModified => b.Modified.CompareTo(a.Modified),
            SortMode.Type => CompareType(a, b),
            SortMode.Size => b.Size.CompareTo(a.Size),
            _ => NaturalStringComparer.Instance.Compare(a.DisplayName, b.DisplayName)
        };

        if (result == 0)
        {
            result = NaturalStringComparer.Instance.Compare(a.DisplayName, b.DisplayName);
        }

        return result;
    }

    private static int CompareType(DesktopItem a, DesktopItem b)
    {
        int result = StringComparer.OrdinalIgnoreCase.Compare(a.TypeLabel, b.TypeLabel);
        return result != 0
            ? result
            : NaturalStringComparer.Instance.Compare(a.DisplayName, b.DisplayName);
    }

    private void UpdatePage(
        ISet<string>? restoreSelection = null,
        string? selectPath = null)
    {
        int capacity = Math.Max(1, Columns * Rows);
        int pageCount = Math.Max(1, (_filteredItems.Count + capacity - 1) / capacity);
        _pageCount = pageCount;
        _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);

        List<DesktopItem> pageItems = _filteredItems
            .Skip(_pageIndex * capacity)
            .Take(capacity)
            .ToList();

        _updatingPage = true;
        try
        {
            PageList.UnselectAll();
            VisibleItems.Clear();

            foreach (DesktopItem item in pageItems)
            {
                VisibleItems.Add(item);
            }

            if (restoreSelection is not null && restoreSelection.Count > 0)
            {
                foreach (DesktopItem item in pageItems)
                {
                    if (restoreSelection.Contains(item.FullPath))
                    {
                        PageList.SelectedItems.Add(item);
                    }
                }
            }

            if (!string.IsNullOrEmpty(selectPath))
            {
                DesktopItem? selected = pageItems.FirstOrDefault(
                    item => string.Equals(item.FullPath, selectPath, StringComparison.OrdinalIgnoreCase));

                if (selected is not null)
                {
                    PageList.SelectedItems.Clear();
                    PageList.SelectedItems.Add(selected);
                }
            }
        }
        finally
        {
            _updatingPage = false;
        }

        PageDots.Clear();
        ShowPageDots = pageCount > 1 && pageCount <= 10;

        if (ShowPageDots)
        {
            for (int i = 0; i < pageCount; i++)
            {
                int start = i * capacity + 1;
                int end = Math.Min((i + 1) * capacity, _filteredItems.Count);

                PageDots.Add(new PageDot
                {
                    Index = i,
                    Label = (i + 1).ToString(),
                    IsCurrent = i == _pageIndex,
                    Tooltip = $"Page {i + 1} - items {start} to {end}"
                });
            }
        }

        CurrentPageLabel = pageCount <= 1 ? "1 page" : $"Page {_pageIndex + 1} of {pageCount}";
        PrevButton.IsEnabled = _pageIndex > 0;
        NextButton.IsEnabled = _pageIndex < pageCount - 1;
        UpdateEmptyState(pageItems.Count);
        UpdateSummary();

        // Pages switch instantly. The old fade and slide animation could leave
        // item labels blank for a moment, so the grid is always reset to a fully
        // visible, untranslated state.
        PageList.BeginAnimation(OpacityProperty, null);
        PageList.Opacity = 1;

        if (PageList.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        }

        _ = LoadVisibleIconsAsync(pageItems);
    }

    private async Task LoadVisibleIconsAsync(List<DesktopItem> items)
    {
        var tasks = items.Select(async item =>
        {
            if (item.Icon is not null)
            {
                return;
            }

            ImageSource? icon = await _iconService.GetAsync(item);
            if (icon is not null && item.Icon is null)
            {
                item.Icon = icon;
            }
        });

        await Task.WhenAll(tasks);
    }

    private void StartPrefetch()
    {
        _prefetchCts?.Cancel();
        _prefetchCts = new CancellationTokenSource();
        CancellationToken token = _prefetchCts.Token;
        List<DesktopItem> snapshot = _allItems.ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                await _iconService.PrefetchAsync(snapshot, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A newer refresh superseded this prefetch.
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Icon prefetch failed");
            }
        }, token);
    }

    private void UpdateEmptyState(int visibleCount)
    {
        if (!_hasLoadedOnce)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        if (visibleCount > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Visible;

        if (HasSearchText)
        {
            EmptyTitle.Text = "No matches";
            EmptySubtitle.Text = $"Nothing on your desktop matches \"{_searchText.Trim()}\".";
            return;
        }

        if (_allItems.Count == 0)
        {
            EmptyTitle.Text = "Your desktop is clear";
            EmptySubtitle.Text = "Files and folders you add will appear here.";
            return;
        }

        EmptyTitle.Text = "This page is empty";
        EmptySubtitle.Text = "Try another page or adjust the icon size.";
    }

    private void UpdateSummary()
    {
        if (!_hasLoadedOnce)
        {
            ItemSummary = "Reading desktop...";
            return;
        }

        int selected = _updatingPage ? 0 : PageList.SelectedItems.Count;
        string pages = _pageCount == 1 ? "1 page" : $"{_pageCount} pages";

        if (selected > 0)
        {
            ItemSummary = selected == 1 ? "1 selected" : $"{selected} selected";
            return;
        }

        if (HasSearchText)
        {
            string results = _filteredItems.Count == 1 ? "1 result" : $"{_filteredItems.Count} results";
            ItemSummary = $"{results} - {pages}";
            return;
        }

        string items = _allItems.Count == 1 ? "1 item" : $"{_allItems.Count} items";
        ItemSummary = $"{items} - {pages}";
        Title = $"Desktop Drawer - {_allItems.Count} items";
    }

    private void GoToPage(int targetPage)
    {
        int target = Math.Clamp(targetPage, 0, Math.Max(0, _pageCount - 1));
        if (target == _pageIndex)
        {
            return;
        }

        _lastPageFlip = DateTime.UtcNow;
        _pageIndex = target;
        UpdatePage();
    }

    private void RecalculateGrid(bool force = false)
    {
        double availableWidth = PageArea.ActualWidth;
        double availableHeight = PageArea.ActualHeight;
        if (availableWidth < 80 || availableHeight < 80)
        {
            return;
        }

        int columns = Math.Max(2, (int)Math.Floor((availableWidth - 8) / (_tileWidth + 4)));
        int rows = Math.Max(1, (int)Math.Floor((availableHeight - 8) / (_tileHeight + 4)));

        if (!force && columns == _columns && rows == _rows)
        {
            return;
        }

        Columns = columns;
        Rows = rows;
        AppLog.Write($"Layout {ActualWidth:0}x{ActualHeight:0} dip, page {availableWidth:0}x{availableHeight:0}, grid {columns}x{rows}, tile {_tileWidth}x{_tileHeight}");

        if (_hasLoadedOnce)
        {
            UpdatePage();
        }
    }

    private void ApplyDensity(TileDensity density)
    {
        _settings.Density = density;

        switch (density)
        {
            case TileDensity.Compact:
                IconSize = 44;
                TileFontSize = 11;
                TileNameHeight = 28;
                _tileWidth = 108;
                _tileHeight = 116;
                break;
            case TileDensity.Large:
                IconSize = 96;
                TileFontSize = 13.5;
                TileNameHeight = 40;
                _tileWidth = 210;
                _tileHeight = 216;
                break;
            default:
                IconSize = 72;
                TileFontSize = 12.5;
                TileNameHeight = 36;
                _tileWidth = 168;
                _tileHeight = 168;
                break;
        }
    }

    private void SaveSettings()
    {
        _settingsService.Save(_settings);
    }

    private void UpdateSortLabel()
    {
        SortLabelText.Text = _settings.SortMode switch
        {
            SortMode.DateModified => "Date",
            SortMode.Type => "Type",
            SortMode.Size => "Size",
            _ => "Name"
        };
    }

    private void UpdateTrayText()
    {
        if (_trayIcon is null || _cleanedUp)
        {
            return;
        }

        _trayIcon.Text = $"Desktop Drawer - {_allItems.Count} items across {_pageCount} pages";
    }

    private void ShowToast(string message)
    {
        if (_cleanedUp)
        {
            return;
        }

        ToastText.Text = message;
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastHost.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
        ToastHost.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fadeOut.Completed += (_, _) => ToastHost.Visibility = Visibility.Collapsed;
        ToastHost.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private void Window_PreviewMouseActivity(object sender, MouseButtonEventArgs e)
    {
        ShowChrome();
    }

    private void ChromeTimer_Tick(object? sender, EventArgs e)
    {
        if (_cleanedUp)
        {
            return;
        }

        Point cursor = Mouse.GetPosition(this);
        if (cursor != _lastCursorPosition)
        {
            _lastCursorPosition = cursor;
            ShowChrome();
            return;
        }

        if (!_chromeVisible)
        {
            return;
        }

        if (_settingsOpen ||
            SearchBox.IsKeyboardFocusWithin ||
            _renamingItem is not null ||
            HeaderBar.IsMouseOver ||
            FooterBar.IsMouseOver)
        {
            _lastChromeActivity = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _lastChromeActivity >= ChromeIdleDelay)
        {
            HideChrome();
        }
    }

    private void ShowChrome()
    {
        if (_cleanedUp)
        {
            return;
        }

        _lastChromeActivity = DateTime.UtcNow;

        if (_chromeVisible)
        {
            return;
        }

        _chromeVisible = true;
        HeaderBar.IsHitTestVisible = true;
        FooterBar.IsHitTestVisible = true;
        AnimateChrome(1);
    }

    private void HideChrome()
    {
        if (_cleanedUp || !_chromeVisible)
        {
            return;
        }

        _chromeVisible = false;
        HeaderBar.IsHitTestVisible = false;
        FooterBar.IsHitTestVisible = true;
        AnimateChrome(0);
    }

    private void AnimateChrome(double targetOpacity)
    {
        double headerTarget = targetOpacity;
        double footerTarget = targetOpacity > 0 ? 1 : 0.7;

        var headerFade = new DoubleAnimation(headerTarget, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var footerFade = new DoubleAnimation(footerTarget, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        headerFade.Completed += (_, _) =>
        {
            HeaderBar.BeginAnimation(OpacityProperty, null);
            HeaderBar.Opacity = headerTarget;
        };
        footerFade.Completed += (_, _) =>
        {
            FooterBar.BeginAnimation(OpacityProperty, null);
            FooterBar.Opacity = footerTarget;
        };

        HeaderBar.BeginAnimation(OpacityProperty, headerFade, HandoffBehavior.SnapshotAndReplace);
        FooterBar.BeginAnimation(OpacityProperty, footerFade, HandoffBehavior.SnapshotAndReplace);
    }

    private void ResizeTimer_Tick(object? sender, EventArgs e)
    {
        _resizeTimer.Stop();
        RecalculateGrid();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (!_initializingSettings)
        {
            UpdateDesktopIconsUi();
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        _isExiting = true;
        Cleanup();
    }

    private void Cleanup()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        _prefetchCts?.Cancel();
        _chromeTimer.Stop();
        _desktopService.DesktopChanged -= OnDesktopChanged;
        _desktopService.Dispose();
        _iconService.Dispose();

        Application.Current.SessionEnding -= OnSessionEnding;

        try
        {
            // Exiting always brings the native desktop back, even if the icons were
            // hidden before this session started.
            _desktopIconsService.SetHidden(false);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not restore desktop icons on exit");
        }

        DisposeTray();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
