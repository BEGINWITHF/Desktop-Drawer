using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopDrawer.Helpers;
using DesktopDrawer.Interop;
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
    private readonly DispatcherTimer _embedTimer;
    private readonly DispatcherTimer _desktopZOrderTimer;

    private List<DesktopItem> _allItems = [];
    private List<DesktopItem> _filteredItems = [];
    private List<PageSlice> _pages = [];

    private int _pageIndex;
    private int _pageCount = 1;
    private int _columns = 8;
    private int _rows = 4;
    private double _iconSize = 66;
    private double _requestedIconSize = 72;
    private double _tileFontSize = 12.5;
    private double _tileNameHeight = 34;
    private double _tileWidth = 145;
    private double _tileHeight = 152;

    private string _searchText = string.Empty;
    private string _itemSummary = "Reading desktop...";
    private string _currentPageLabel = "Page 1 of 1";
    private bool _showPageDots;
    private string _currentGroupText = string.Empty;
    private bool _showGroupBadge;

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
    private bool _layoutEditing;
    private FrameworkElement? _dragElement;
    private Point _dragStartPoint;
    private Thickness _dragStartMargin;
    private readonly Dictionary<FrameworkElement, string> _chromeElements = new();
    private readonly Dictionary<FrameworkElement, ChromeOutlineAdorner> _chromeAdorners = new();
    private bool _embeddedInDesktop;
    private int _embedAttempts;

    /// <summary>
    /// Windows only composites per-pixel transparency for top-level windows, so
    /// the transparent build cannot also be a child of the desktop icon view.
    /// Going back to the embedded (opaque) desktop layer also requires the
    /// opaque window setup that used to live in MainWindow.xaml: AllowsTransparency
    /// off, a solid window background and a BackdropImage layer.
    /// </summary>
    private static readonly bool EmbedInDesktopLayer = false;
    private Point _lastCursorPosition;
    private DateTime _lastChromeActivity = DateTime.UtcNow;
    private DateTime _lastWatchdogCheck = DateTime.UtcNow;

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
        _embedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _embedTimer.Tick += EmbedTimer_Tick;
        _desktopZOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _desktopZOrderTimer.Tick += DesktopZOrderTimer_Tick;

        InitializeComponent();
        DataContext = this;

        PageList.RenderTransform = new TranslateTransform();
        ApplyDensity(_settings.Density);
        PageList.ContextMenu = BuildEmptyAreaMenu();
        UpdateSortLabel();
        CloseButton.ToolTip = _settings.CloseToTray ? "Close to tray" : "Close";
        InitializeChromeLayout();

        PreviewMouseDown += Window_PreviewMouseActivity;
        StateChanged += Window_StateChanged;
        LocationChanged += Window_LocationChanged;

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

    public string CurrentGroupText
    {
        get => _currentGroupText;
        private set => SetField(ref _currentGroupText, value);
    }

    public bool ShowGroupBadge
    {
        get => _showGroupBadge;
        private set => SetField(ref _showGroupBadge, value);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        long exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle,
            new IntPtr(exStyle | NativeMethods.WsExToolWindow));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWorkAreaBounds();

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
        ApplyChromeLayout();
        RecalculateGrid(force: true);
        await RefreshAsync();

        // Become part of the desktop itself instead of a floating window.
        if (EmbedInDesktopLayer)
        {
            StartDesktopEmbedding();
        }
        else
        {
            _desktopZOrderTimer.Start();
        }

        _initialized = true;
        _lastCursorPosition = Mouse.GetPosition(this);
        ShowChrome();
        _chromeTimer.Start();

        if (!EmbedInDesktopLayer)
        {
            DesktopHost.RaiseAboveDesktop(new WindowInteropHelper(this).Handle);
        }

        if (!_settings.HasLaunched)
        {
            _settings.HasLaunched = true;
            SaveSettings();
            ShowToast("Move the mouse to show controls. Scroll or press PageDown to flip pages.");
        }
    }

    private void DesktopZOrderTimer_Tick(object? sender, EventArgs e)
    {
        if (_cleanedUp || !IsVisible || WindowState != WindowState.Normal)
        {
            return;
        }

        try
        {
            // Keep the overlay at the bottom of the window stack: directly above the
            // desktop and below every application window, so it never covers software.
            DesktopHost.RaiseAboveDesktop(new WindowInteropHelper(this).Handle);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Could not keep the drawer above the desktop");
            _desktopZOrderTimer.Stop();
        }
    }

    private void StartDesktopEmbedding()
    {
        if (DesktopHost.TryEmbed(this))
        {
            _embeddedInDesktop = true;
            return;
        }

        // Explorer may not have created its desktop view yet right after sign-in.
        _embedTimer.Start();
    }

    private void EmbedTimer_Tick(object? sender, EventArgs e)
    {
        if (_cleanedUp || _embeddedInDesktop)
        {
            _embedTimer.Stop();
            return;
        }

        if (DesktopHost.TryEmbed(this))
        {
            _embeddedInDesktop = true;
            _embedTimer.Stop();
            return;
        }

        if (++_embedAttempts >= 20)
        {
            _embedTimer.Stop();
            AppLog.Write("Could not attach to the desktop; running as a normal window.");
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

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (!_initialized || _cleanedUp)
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        if (Math.Abs(Left - workArea.Left) > 0.5 || Math.Abs(Top - workArea.Top) > 0.5)
        {
            Left = workArea.Left;
            Top = workArea.Top;
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            ApplyWorkAreaBounds();
            return;
        }

        if (WindowState == WindowState.Minimized && !_isExiting && !_cleanedUp)
        {
            // "Show desktop" must not take the drawer away, so pop straight back.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isExiting && !_cleanedUp && WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                    ApplyWorkAreaBounds();
                }
            }));
        }
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
        _pages = BuildPages(capacity);

        int pageCount = Math.Max(1, _pages.Count);
        _pageCount = pageCount;
        _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);

        PageSlice slice = _pages.Count > 0 ? _pages[_pageIndex] : new PageSlice();
        List<DesktopItem> pageItems = slice.Items;

        CurrentGroupText = string.IsNullOrEmpty(slice.Group)
            ? string.Empty
            : $"{slice.Group} · {slice.GroupTotal}";
        ShowGroupBadge = !string.IsNullOrEmpty(slice.Group);

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
        ShowPageDots = pageCount > 1 && pageCount <= 12;

        if (ShowPageDots)
        {
            for (int i = 0; i < pageCount; i++)
            {
                PageSlice dot = _pages[i];
                PageDots.Add(new PageDot
                {
                    Index = i,
                    Label = (i + 1).ToString(),
                    IsCurrent = i == _pageIndex,
                    Tooltip = string.IsNullOrEmpty(dot.Group)
                        ? $"Page {i + 1} - {dot.Items.Count} items"
                        : $"{dot.Group} - page {dot.GroupPage} of {dot.GroupPages}"
                });
            }
        }

        CurrentPageLabel = BuildPageLabel(slice, pageCount);
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

        int columns = _settings.GridColumns > 0
            ? Math.Clamp(_settings.GridColumns, 2, 24)
            : Math.Max(2, (int)Math.Floor((availableWidth - 8) / (_tileWidth + 4)));

        int rows = _settings.GridRows > 0
            ? Math.Clamp(_settings.GridRows, 1, 16)
            : Math.Max(1, (int)Math.Floor((availableHeight - 8) / (_tileHeight + 4)));

        // Icons never grow past the cell they live in, so a denser grid shrinks them.
        double cellWidth = availableWidth / columns;
        double cellHeight = availableHeight / rows;
        double maxIcon = Math.Min(cellWidth * 0.55, cellHeight * 0.55);
        double iconSize = Math.Clamp(Math.Min(_requestedIconSize, maxIcon), 20, _requestedIconSize);

        IconSize = iconSize;
        TileFontSize = Math.Clamp(iconSize * 0.18, 10, 16);
        TileNameHeight = Math.Clamp(iconSize * 0.5, 24, 60);

        if (!force && columns == _columns && rows == _rows)
        {
            return;
        }

        Columns = columns;
        Rows = rows;
        AppLog.Write($"Layout {ActualWidth:0}x{ActualHeight:0} dip, page {availableWidth:0}x{availableHeight:0}, grid {columns}x{rows}, tile {_tileWidth}x{_tileHeight}, icon {iconSize:0}");

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
                _requestedIconSize = 44;
                _tileWidth = 108;
                _tileHeight = 116;
                break;
            case TileDensity.Large:
                _requestedIconSize = 96;
                _tileWidth = 210;
                _tileHeight = 216;
                break;
            default:
                _requestedIconSize = 72;
                _tileWidth = 168;
                _tileHeight = 168;
                break;
        }

        if (_settings.IconSize > 0)
        {
            _requestedIconSize = Math.Clamp(_settings.IconSize, 24, 160);
            _tileWidth = _requestedIconSize * 2.33;
            _tileHeight = _requestedIconSize * 2.33;
        }

        IconSize = _requestedIconSize;
        TileFontSize = Math.Clamp(_requestedIconSize * 0.18, 10, 16);
        TileNameHeight = Math.Clamp(_requestedIconSize * 0.5, 24, 60);
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

        if (DateTime.UtcNow - _lastWatchdogCheck >= TimeSpan.FromSeconds(30))
        {
            _lastWatchdogCheck = DateTime.UtcNow;
            IconWatchdog.EnsureRunning();
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
            _layoutEditing ||
            SearchBox.IsKeyboardFocusWithin ||
            _renamingItem is not null ||
            HeaderBar.IsMouseOver ||
            PagerBar.IsMouseOver ||
            SummaryText.IsMouseOver ||
            HintText.IsMouseOver ||
            GroupBadge.IsMouseOver)
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
            foreach (FrameworkElement element in new FrameworkElement[] { PagerBar, SummaryText, HintText })
            {
                element.BeginAnimation(OpacityProperty, null);
                element.Opacity = footerTarget;
            }
        };

        HeaderBar.BeginAnimation(OpacityProperty, headerFade, HandoffBehavior.SnapshotAndReplace);
        foreach (FrameworkElement element in new FrameworkElement[] { PagerBar, SummaryText, HintText })
        {
            element.BeginAnimation(OpacityProperty, footerFade, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void InitializeChromeLayout()
    {
        _chromeElements[HeaderBar] = "header";
        _chromeElements[PagerBar] = "pager";
        _chromeElements[SummaryText] = "summary";
        _chromeElements[HintText] = "hint";
        _chromeElements[GroupBadge] = "groups";

        foreach (FrameworkElement element in _chromeElements.Keys)
        {
            element.PreviewMouseLeftButtonDown += Chrome_PreviewMouseDown;
            element.PreviewMouseMove += Chrome_PreviewMouseMove;
            element.PreviewMouseLeftButtonUp += Chrome_PreviewMouseUp;
        }

        RootGrid.SizeChanged += (_, _) => ApplyChromeLayout();
    }

    private void ApplyChromeLayout()
    {
        double width = RootGrid.ActualWidth;
        double height = RootGrid.ActualHeight;
        if (width < 10 || height < 10)
        {
            return;
        }

        foreach ((FrameworkElement element, string key) in _chromeElements)
        {
            double elementWidth = element.ActualWidth > 0 ? element.ActualWidth : element.DesiredSize.Width;
            double elementHeight = element.ActualHeight > 0 ? element.ActualHeight : element.DesiredSize.Height;

            double x;
            double y;

            if (_settings.Layout.TryGetValue(key, out ChromePosition? saved) &&
                saved.X.HasValue &&
                saved.Y.HasValue)
            {
                x = saved.X.Value * width;
                y = saved.Y.Value * height;
            }
            else
            {
                (x, y) = key switch
                {
                    "pager" => ((width - elementWidth) / 2, height - elementHeight - 10),
                    "summary" => (16, height - elementHeight - 12),
                    "hint" => (width - elementWidth - 16, height - elementHeight - 12),
                    "groups" => (16, 62),
                    _ => (0d, 0d)
                };
            }

            x = Math.Clamp(x, 0, Math.Max(0, width - elementWidth));
            y = Math.Clamp(y, 0, Math.Max(0, height - elementHeight));
            element.Margin = new Thickness(x, y, 0, 0);
        }
    }

    private void Chrome_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_layoutEditing || sender is not FrameworkElement element)
        {
            return;
        }

        _dragElement = element;
        _dragStartPoint = e.GetPosition(RootGrid);
        _dragStartMargin = element.Margin;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void Chrome_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_layoutEditing || _dragElement is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point point = e.GetPosition(RootGrid);
        double width = RootGrid.ActualWidth;
        double height = RootGrid.ActualHeight;
        double elementWidth = _dragElement.ActualWidth;
        double elementHeight = _dragElement.ActualHeight;

        double x = _dragStartMargin.Left + (point.X - _dragStartPoint.X);
        double y = _dragStartMargin.Top + (point.Y - _dragStartPoint.Y);

        if (_dragElement == HeaderBar)
        {
            // The header spans the whole width, so only vertical movement matters.
            x = 0;
        }

        x = Math.Clamp(x, 0, Math.Max(0, width - elementWidth));
        y = Math.Clamp(y, 0, Math.Max(0, height - elementHeight));
        _dragElement.Margin = new Thickness(x, y, 0, 0);
        e.Handled = true;
    }

    private void Chrome_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragElement is null)
        {
            return;
        }

        FrameworkElement element = _dragElement;
        _dragElement = null;
        element.ReleaseMouseCapture();

        if (!_layoutEditing)
        {
            return;
        }

        if (_chromeElements.TryGetValue(element, out string? key))
        {
            double width = Math.Max(1, RootGrid.ActualWidth);
            double height = Math.Max(1, RootGrid.ActualHeight);

            if (!_settings.Layout.TryGetValue(key, out ChromePosition? position))
            {
                position = new ChromePosition();
                _settings.Layout[key] = position;
            }

            position.X = element.Margin.Left / width;
            position.Y = element.Margin.Top / height;
            SaveSettings();
        }

        e.Handled = true;
    }

    private void SetLayoutEditing(bool enabled)
    {
        if (_layoutEditing == enabled)
        {
            return;
        }

        _layoutEditing = enabled;

        foreach (FrameworkElement element in _chromeElements.Keys)
        {
            AdornerLayer? layer = AdornerLayer.GetAdornerLayer(element);

            if (enabled)
            {
                element.Cursor = Cursors.SizeAll;
                if (layer is not null && !_chromeAdorners.ContainsKey(element))
                {
                    var adorner = new ChromeOutlineAdorner(element);
                    layer.Add(adorner);
                    _chromeAdorners[element] = adorner;
                }
            }
            else
            {
                element.Cursor = Cursors.Arrow;
                if (_chromeAdorners.Remove(element, out ChromeOutlineAdorner? adorner))
                {
                    layer?.Remove(adorner);
                }
            }
        }

        ShowChrome();

        if (LayoutEditToggle.IsChecked != enabled)
        {
            LayoutEditToggle.IsChecked = enabled;
        }

        if (enabled)
        {
            ShowToast("Drag the header, pager, summary and hint. Press Esc when done.");
        }
    }

    private string BuildPageLabel(PageSlice slice, int pageCount)
    {
        if (!string.IsNullOrEmpty(slice.Group))
        {
            return slice.GroupPages > 1
                ? $"{slice.Group} · {slice.GroupPage}/{slice.GroupPages}"
                : slice.Group;
        }

        return pageCount <= 1 ? "1 page" : $"Page {_pageIndex + 1} of {pageCount}";
    }

    private List<PageSlice> BuildPages(int capacity)
    {
        var pages = new List<PageSlice>();

        if (_settings.GroupMode == GroupMode.None)
        {
            for (int i = 0; i < _filteredItems.Count; i += capacity)
            {
                var items = _filteredItems.Skip(i).Take(capacity).ToList();
                pages.Add(new PageSlice { Items = items, GroupTotal = _filteredItems.Count });
            }

            return pages;
        }

        List<IGrouping<string, DesktopItem>> groups = _filteredItems.GroupBy(GetGroupName).ToList();

        IEnumerable<IGrouping<string, DesktopItem>> ordered = _settings.GroupMode switch
        {
            GroupMode.Type => groups.OrderBy(group => TypeGroupOrder(group.Key)).ThenBy(group => group.Key),
            GroupMode.Name => groups.OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase),
            GroupMode.Date => groups.OrderBy(group => DateGroupOrder(group.Key)),
            _ => groups
        };

        foreach (IGrouping<string, DesktopItem> group in ordered)
        {
            List<DesktopItem> items = group.ToList();
            int groupPages = Math.Max(1, (items.Count + capacity - 1) / capacity);

            for (int page = 0; page < groupPages; page++)
            {
                pages.Add(new PageSlice
                {
                    Group = group.Key,
                    Items = items.Skip(page * capacity).Take(capacity).ToList(),
                    GroupPage = page + 1,
                    GroupPages = groupPages,
                    GroupTotal = items.Count
                });
            }
        }

        return pages;
    }

    private string GetGroupName(DesktopItem item) => _settings.GroupMode switch
    {
        GroupMode.Type => GetTypeGroup(item),
        GroupMode.Name => GetNameGroup(item),
        GroupMode.Date => GetDateGroup(item),
        _ => string.Empty
    };

    private static string GetTypeGroup(DesktopItem item)
    {
        if (item.IsFolder)
        {
            return "Folders";
        }

        return Path.GetExtension(item.Name).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" or ".psd" or ".tif" or ".tiff" => "Images",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" or ".flv" or ".m4v" => "Videos",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => "Audio",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".xz" or ".bz2" or ".iso" => "Archives",
            ".doc" or ".docx" or ".pdf" or ".txt" or ".md" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" or ".rtf" or ".odt" => "Documents",
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".lnk" => "Programs",
            _ => "Other"
        };
    }

    private static int TypeGroupOrder(string name) => name switch
    {
        "Folders" => 0,
        "Documents" => 1,
        "Images" => 2,
        "Videos" => 3,
        "Audio" => 4,
        "Archives" => 5,
        "Programs" => 6,
        _ => 7
    };

    private static string GetNameGroup(DesktopItem item)
    {
        string name = item.DisplayName.TrimStart('.');
        if (name.Length == 0)
        {
            return "#";
        }

        char first = char.ToUpperInvariant(name[0]);
        if (char.IsDigit(first))
        {
            return "#";
        }

        return char.IsLetter(first) ? first.ToString() : "…";
    }

    private static string GetDateGroup(DesktopItem item)
    {
        DateTime today = DateTime.Today;
        DateTime date = item.Modified.Date;

        if (date >= today)
        {
            return "Today";
        }

        if (date >= today.AddDays(-1))
        {
            return "Yesterday";
        }

        if (date >= today.AddDays(-7))
        {
            return "This week";
        }

        if (date >= today.AddDays(-31))
        {
            return "This month";
        }

        return date >= today.AddDays(-366) ? "This year" : "Earlier";
    }

    private static int DateGroupOrder(string name) => name switch
    {
        "Today" => 0,
        "Yesterday" => 1,
        "This week" => 2,
        "This month" => 3,
        "This year" => 4,
        _ => 5
    };

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
        _embedTimer.Stop();
        _desktopZOrderTimer.Stop();
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

/// <summary>
/// One visible page: a slice of a group (or of the whole list when grouping is off).
/// </summary>
internal sealed class PageSlice
{
    public string Group { get; init; } = string.Empty;
    public List<DesktopItem> Items { get; init; } = new();
    public int GroupPage { get; init; } = 1;
    public int GroupPages { get; init; } = 1;
    public int GroupTotal { get; init; }
}

/// <summary>
/// Dashed rectangle shown around a chrome element while the layout editor is on.
/// </summary>
internal sealed class ChromeOutlineAdorner : Adorner
{
    private static readonly Pen OutlinePen = CreatePen();

    public ChromeOutlineAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(AdornedElement.RenderSize);
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        drawingContext.DrawRoundedRectangle(null, OutlinePen, bounds, 10, 10);
    }

    private static Pen CreatePen()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0xC0, 0x5A, 0xA0, 0xFF));
        brush.Freeze();

        var pen = new Pen(brush, 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
        };
        pen.Freeze();
        return pen;
    }
}
