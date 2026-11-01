using System.Collections.ObjectModel;
using System.Text.Json;
using System.Diagnostics;
using MicaBrowser.Backdrop;
using MicaBrowser.Models;
using MicaBrowser.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace MicaBrowser;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _zoomOverlayTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    private readonly DispatcherTimer _extensionReloadTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _suspendTimer = new() { Interval = TimeSpan.FromSeconds(25) };
    private readonly DispatcherTimer _audioStateTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _historyFilterTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _suppressTabSelection;
    private bool _isFullScreen;
    private BrowserTab? _activeTab;
    private SettingsWindow? _settingsWindow;
    private Flyout? passwordFlyout;
    private Flyout? favoriteFlyout;
    private TaskManagerWindow? _taskManager;
    private ThemeMicaBackdrop? _backdrop;
    private ThemeAcrylicBackdrop? _acrylicBackdrop;
    private Border? _settingsHost;
    private SettingsWindow? _inlineSettings;
    private BrowserTab? _settingsTab;
    private ScrollViewer? _historyScroll;
    private ScrollViewer? _sidebarScroll;
    private bool _closingInlineSettings;
    private bool _browserSessionInitialized;
    private bool _forceExit;
    private Button? _translateButton;
    private Button? _proxyButton;
    private Button? _ghostButton;

    private bool IsInlineSettingsOpen =>
        _inlineSettings is not null && _settingsHost?.Visibility == Visibility.Visible;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private Border? _horizontalTabIndicator;
    private Border? _horizontalHoverIndicator;
    private Popup? _tabPreviewPopup;
    private ListViewItem? _tabPreviewContainer;
    private BrowserTab? _tabPreviewTab;
    private Image? _tabPreviewImage;
    private DispatcherTimer? _tabPreviewTimer;
    private DispatcherTimer? _tabPreviewHideTimer;
    private DispatcherTimer? _tabPreviewWatchTimer;
    private DispatcherTimer? _tabHoverPollTimer;
    private DateTime _tabPreviewHoverStartedAt = DateTime.MinValue;
    private static readonly TimeSpan TabPreviewHoverDelay = TimeSpan.FromMilliseconds(700);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    private Border? _verticalTabIndicator;
    private Border? _verticalUnderlineIndicator;
    private Storyboard? _tabIndicatorAnimation;
    private Storyboard? _verticalTabsAnimation;
    private Storyboard? _sidebarAnimation;
    private string _sidebarMode = "favorites";
    private bool _deferTabIndicator;
    private CancellationTokenSource? _tabIndicatorUpdateCts;
    private bool _horizontalSizeQueued;
    private bool _suppressAddressSuggestions;
    private bool _addressTyping;
    private int _addressSuggestionsVersion;

    public ObservableCollection<BrowserTab> Tabs { get; } = new();
    public ObservableCollection<FavoriteEntry> Favorites => App.Services.Favorites.Items;

    public void RefreshFavoritesPanel()
    {
        FavoritesList.ItemsSource = null;
        FavoritesList.ItemsSource = Favorites;
    }
    public ObservableCollection<AddressSuggestion> AddressSuggestions { get; } = new();
    public ObservableCollection<HistoryEntry> SessionHistory { get; } = new();
    public ObservableCollection<HistoryEntry> History => App.Services.History.Items;
    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        SuppressNewTabTooltips();
        EnableSidebarScrolling();
        InstallTranslateButton();
        InstallProxyButton();
        InstallGhostButton();
        InstallExtensionButtons();
        var settingsAccelerator = new KeyboardAccelerator
        {
            Key = (Windows.System.VirtualKey)0xBC,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        };
        settingsAccelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            OpenSettings("general");
        };
        RootGrid.KeyboardAccelerators.Add(settingsAccelerator);
        var translateAccelerator = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.T,
            Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift
        };
        translateAccelerator.Invoked += (sender, args) =>
        {
            args.Handled = true;
            _ = TranslatePageAsync("zh-CN", "中文（简体）");
        };
        RootGrid.KeyboardAccelerators.Add(translateAccelerator);

        LoadDownloadHistory();
        Downloads.CollectionChanged += (_, _) => SaveDownloadHistory();
        Closed += (_, _) => SaveDownloadHistory();
        if (!App.Services.Settings.StartupMaskEnabled && StartupMask is not null)
        {
            StartupMask.Visibility = Visibility.Collapsed;
        }
        Localization.Apply(RootGrid);
        AddressBox.PlaceholderText = Localization.T("搜索网页或输入网址");
        App.MainWindows.Add(this);
        _hwnd = WindowNative.GetWindowHandle(this);
        NativeWindowHelper.Attach(_hwnd);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "EXET";

        ConfigureWindow();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragSurface);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
        }

        // Diagnostic: native WM_NCHITTEST drag only for now.
        AttachWindowDrag(HorizontalTabStrip);
        // AttachWindowDrag(VerticalWindowTitleBar);
        _appWindow.Closing += OnAppWindowClosing;
        _appWindow.Changed += (_, _) => UpdateCaptionGlyph();
        UpdateCaptionGlyph();
        _statusTimer.Tick += (_, _) => ClearTransientStatus();
        _zoomOverlayTimer.Tick += (_, _) => HideZoomOverlay();
        BrowserHost.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(BrowserHost_PointerWheelChanged),
            true);
        _extensionReloadTimer.Tick += (_, _) =>
        {
            _extensionReloadTimer.Stop();
            ReloadAllTabs();
        };
        _suspendTimer.Tick += async (_, _) => await SuspendInactiveTabsAsync();
        _audioStateTimer.Tick += (_, _) => RefreshTabAudioStates();
        Tabs.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyUiFont);
        App.Services.Extensions.Mutated += OnExtensionsMutated;
        CreateTabIndicators();



        AddressBox.FontFamily = new FontFamily("Segoe UI");
        AddressDisplay.FontFamily = new FontFamily("Segoe UI");
        BuildAddressContextMenu();
        InstallDownloadDeleteButtons();
        ContentShell.SizeChanged += (_, _) => UpdateFloatingPanelSize();
        HistorySearchBox.TextChanged += HistorySearchBox_TextChanged;
        _historyFilterTimer.Tick += (_, _) =>
        {
            _historyFilterTimer.Stop();
            ApplyHistoryFilter();
        };
        HistoryAllTab.Click += HistoryAllTab_Click;
        HistoryRecentTab.Click += HistoryRecentTab_Click;

        ApplyAppearance();
        if (!string.IsNullOrWhiteSpace(App.Services.Settings.UiFontFamily))
        {
            ApplyUiFont();
        }
        ApplyVerticalTabs();
        ApplyToolbarSettings();
        AttachToolbarContextMenus();
        ApplyPerformanceSettings();
        // Tab/WebView2 creation is started by InitializeBrowserSession() once
        // the window is actually being shown. In preload mode the window is
        // created hidden, so doing this from RootGrid.Loaded would create a
        // WebView2 against a zero-sized hidden surface.
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        RootGrid.Loaded += RootGrid_Loaded;
        RootGrid.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler((_, e) =>
        {
            var point = e.GetPosition(RootGrid);
            if (point.Y > 42) return;
            var source = e.OriginalSource as DependencyObject;
            while (source is not null)
            {
                if (source is Button or ListViewItem or TextBox) return;
                source = VisualTreeHelper.GetParent(source);
            }
            ToggleWindowMaximize();
            e.Handled = true;
        }), true);
        if (Environment.GetEnvironmentVariable("EXET_OPEN_SETTINGS") == "1")
        {
            RootGrid.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() => OpenSettings("general"));
        }

    }

    private void ConfigureWindow()
    {
        if (_appWindow is null)
        {
            return;
        }

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        const int width = 1440;
        const int height = 920;
        var x = work.X + Math.Max(0, (work.Width - width) / 2);
        var y = work.Y + Math.Max(0, (work.Height - height) / 2);
        _appWindow.MoveAndResize(new RectInt32(x, y, Math.Min(width, work.Width), Math.Min(height, work.Height)));

        var titleBar = _appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = false;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void UpdateCaptionGlyph()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter &&
            MaximizeCaptionIcon is not null)
        {
            MaximizeCaptionIcon.Glyph = presenter.State == OverlappedPresenterState.Maximized
                ? "\uE923"
                : "\uE922";
        }
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateCaptionButtonTheme();
        _activeTab?.WebView?.Focus(FocusState.Programmatic);
        PlayStartupMaskAnimation();
        DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(false));
    }

    private async void PlayStartupMaskAnimation()
    {
        if (StartupMask is null)
        {
            return;
        }

        if (!App.Services.Settings.StartupMaskEnabled)
        {
            StartupMask.Opacity = 0;
            StartupMask.IsHitTestVisible = false;
            StartupMask.Visibility = Visibility.Collapsed;
            return;
        }

        StartupMask.Visibility = Visibility.Visible;
        StartupMask.Opacity = 1;
        StartupMask.IsHitTestVisible = true;
        await Task.Delay(800);
        if (StartupMask.XamlRoot is null)
        {
            return;
        }

        var fade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, StartupMask);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) =>
        {
            StartupMask.Opacity = 0;
            StartupMask.IsHitTestVisible = false;
            StartupMask.Visibility = Visibility.Collapsed;
        };
        storyboard.Begin();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Keep the Mica configuration active even when another window has
        // focus. Windows otherwise significantly darkens the material, which
        // looks wrong for a browser window.
        _backdrop?.SetInputActive(true);
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            UpdateCaptionButtonTheme();
        }
        else
        {
            _tabHoverPollTimer?.Stop();
            HideTabPreview();
        }

        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _tabHoverPollTimer?.Start();
        }
    }

    private bool RestoreSession()
    {
        var settings = App.Services.Settings;
        if (App.MainWindows.Count != 1 ||
            !settings.RestoreTabs ||
            settings.LastSession.Count == 0)
        {
            return false;
        }

        var entries = settings.LastSession
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Url))
            .ToList();
        if (entries.Count == 0)
        {
            return false;
        }

        BrowserTab? active = null;
        var activeIndex = Math.Clamp(settings.LastSessionActiveIndex, 0, entries.Count - 1);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var tab = CreateTab(
                entry.Url,
                false,
                false,
                entry.IsHomePage || IsHomeUrl(entry.Url));
            if (i == activeIndex)
            {
                active = tab;
            }
        }

        if (active is not null)
        {
            SelectTab(active);
        }

        return Tabs.Count > 0;
    }

    private void SaveSession()
    {
        if (!ReferenceEquals(App.MainWindow, this))
        {
            return;
        }

        var settings = App.Services.Settings;
        var entries = new List<SessionEntry>();
        var activeIndex = 0;
        foreach (var tab in Tabs)
        {
            if (tab.IsPrivate || string.IsNullOrWhiteSpace(tab.Url))
            {
                continue;
            }

            if (!Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            if (ReferenceEquals(tab, _activeTab))
            {
                activeIndex = entries.Count;
            }

            entries.Add(new SessionEntry
            {
                Url = tab.Url,
                Title = tab.Title,
                IsHomePage = tab.IsHomePage
            });
        }

        settings.LastSession = entries;
        settings.LastSessionActiveIndex = Math.Clamp(activeIndex, 0, Math.Max(0, entries.Count - 1));
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceExit || Environment.HasShutdownStarted ||
            !App.Services.Settings.KeepRunningInBackground)
        {
            return;
        }

        args.Cancel = true;
        SaveSession();
        sender.Hide();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        try
        {
            SaveSession();
            if (App.Services.Settings.ClearCookiesOnExit)
            {
                foreach (var tab in Tabs)
                {
                    try
                    {
                        tab.Core?.CookieManager.DeleteAllCookies();
                    }
                    catch
                    {
                        // Best effort during shutdown.
                    }
                }
            }

            foreach (var tab in Tabs.ToList())
            {
                tab.Dispose();
            }

            App.Services.Extensions.Mutated -= OnExtensionsMutated;
            App.Services.SettingsStore.Save();
            App.Services.History.Save();
            App.Services.Favorites.Save();
        }
        catch
        {
            // Best effort during shutdown.
        }

        ClosePdfPreviews();
        _settingsWindow?.Close();
        _settingsWindow = null;
        App.MainWindows.Remove(this);
        App.MainWindow = App.MainWindows.FirstOrDefault();
        if (App.MainWindows.Count == 0)
        {
            App.Services.Dispose();
            Application.Current.Exit();
        }
    }

    private void OnExtensionsMutated(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(NotifyExtensionsChanged);
    }

    public void ApplyAppearance()
    {
        if (Environment.GetEnvironmentVariable("EXET_SOLID") == "1")
        {
            SystemBackdrop = null;
            _backdrop = null;
            _acrylicBackdrop = null;
            RootGrid.Background = new SolidColorBrush(
                AppSurface.IsDark(App.Services.Settings.NightMode, App.Services.Settings.ThemeMode, ElementTheme.Default)
                    ? Color.FromArgb(255, 32, 32, 32)
                    : Color.FromArgb(255, 243, 243, 243));
            return;
        }
        var settings = App.Services.Settings;
        var theme = settings.NightMode
            ? ElementTheme.Dark
            : settings.ThemeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

        RootGrid.RequestedTheme = theme;
        RefreshTabThemes(theme);
        DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(false));
        ThemeAccent.Apply(RootGrid, settings.AccentColor);
        UpdateToolbarHoverResources(ThemeAccent.Parse(settings.AccentColor));
        UpdateNewTabHoverResources(ThemeAccent.Parse(settings.AccentColor));
        UpdateTabIndicatorColor();
        var useDarkSurface = AppSurface.IsDark(settings.NightMode, settings.ThemeMode, theme);
        var effect = settings.MicaMaterial;
        var material = effect == "Alt" ? MicaKind.BaseAlt : MicaKind.Base;
        var micaEffect = effect is "Mica" or "Alt";
        var acrylicEffect = effect is "Acrylic" or "AcrylicThin" or "Blur" or "Smoke" or "Rainbow";
        var micaActive = micaEffect && ThemeMicaBackdrop.IsSupported;
        var acrylicActive = acrylicEffect && ThemeAcrylicBackdrop.IsSupported;

        if (micaActive)
        {
            if (_acrylicBackdrop is not null)
            {
                SystemBackdrop = null;
                _acrylicBackdrop = null;
            }

            if (_backdrop is null)
            {
                _backdrop = new ThemeMicaBackdrop
                {
                    Theme = theme,
                    Kind = material,
                    Transparency = settings.MicaTransparency,
                    AccentColor = ThemeAccent.Parse(settings.AccentColor),
                    UseDarkTint = useDarkSurface,
                    UseCustomTint = settings.BackdropUseCustomTint,
                    CustomTintColor = ThemeAccent.Parse(settings.BackdropTintColor),
                    CustomTintOpacity = (float)settings.BackdropTintOpacity,
                    CustomLuminosityOpacity = (float)settings.BackdropLuminosityOpacity
                };
                SystemBackdrop = _backdrop;
            }
            else
            {
                _backdrop.UpdateMaterial(material);
                _backdrop.UpdateTheme(theme);
                _backdrop.UpdateDarkTint(useDarkSurface);
                _backdrop.UpdateCustomization(
                    settings.BackdropUseCustomTint,
                    ThemeAccent.Parse(settings.BackdropTintColor),
                    (float)settings.BackdropTintOpacity,
                    (float)settings.BackdropLuminosityOpacity,
                    settings.MicaTransparency);
                _backdrop.UpdateAccent(ThemeAccent.Parse(settings.AccentColor));
            }
        }
        else if (acrylicActive)
        {
            if (_backdrop is not null)
            {
                SystemBackdrop = null;
                _backdrop = null;
            }

            var acrylicKind = effect == "AcrylicThin"
                ? DesktopAcrylicKind.Thin
                : DesktopAcrylicKind.Base;
            var tint = settings.BackdropUseCustomTint
                ? ThemeAccent.Parse(settings.BackdropTintColor)
                : effect switch
            {
                "Acrylic" => ThemeAccent.Parse(settings.AcrylicTintColor),
                "Smoke" => useDarkSurface
                    ? Color.FromArgb(255, 18, 21, 27)
                    : Color.FromArgb(255, 238, 242, 248),
                "Rainbow" => useDarkSurface
                    ? Color.FromArgb(255, 27, 31, 42)
                    : Color.FromArgb(255, 236, 240, 248),
                _ => useDarkSurface
                    ? Color.FromArgb(80, 20, 24, 30)
                    : Color.FromArgb(80, 245, 248, 252)
            };
            var opacity = MaterialEffects.GetOpacity(effect, settings.MicaTransparency);
            var tintOpacity = settings.BackdropUseCustomTint
                ? (float)settings.BackdropTintOpacity
                : opacity.Tint;
            var luminosityOpacity = settings.BackdropUseCustomTint
                ? (float)settings.BackdropLuminosityOpacity
                : opacity.Luminosity;

            if (_acrylicBackdrop is null)
            {
                _acrylicBackdrop = new ThemeAcrylicBackdrop
                {
                    Theme = theme,
                    Kind = acrylicKind,
                    UseCustomTint = settings.BackdropUseCustomTint,
                    TintColor = tint,
                    TintOpacity = tintOpacity,
                    LuminosityOpacity = luminosityOpacity
                };
                SystemBackdrop = _acrylicBackdrop;
            }
            else
            {
                _acrylicBackdrop.UpdateTheme(theme);
                _acrylicBackdrop.UpdateCustomization(
                    settings.BackdropUseCustomTint, tint, tintOpacity, luminosityOpacity, acrylicKind);
            }
        }
        else
        {
            SystemBackdrop = null;
            _backdrop = null;
            _acrylicBackdrop = null;
        }

        MaterialEffects.Apply(
            MicaFallbackBorder,
            effect,
            useDarkSurface,
            micaActive || acrylicActive,
            MaterialEffects.GetOpacity(effect, settings.MicaTransparency).Overlay);

        if (micaActive)
        {
            var alpha = (byte)Math.Round(255 * MaterialEffects.GetMicaScrimAlpha(effect, settings.MicaTransparency));
            var alt = effect == "Alt";
            if (useDarkSurface)
            {
                // Keep dark Mica readable. The old full-strength black scrim
                // made the whole window look completely black at low
                // transparency values.
                alpha = 0;
                MicaFallbackBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    alpha,
                    alt ? (byte)18 : (byte)22,
                    alt ? (byte)20 : (byte)24,
                    alt ? (byte)24 : (byte)28));
            }
            else
            {
                MicaFallbackBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    alpha,
                    alt ? (byte)224 : (byte)235,
                    alt ? (byte)229 : (byte)239,
                    alt ? (byte)235 : (byte)245));
            }
            MicaFallbackBorder.Visibility = Visibility.Collapsed;
        }

        StatusBar.Visibility = Visibility.Collapsed;
        ApplyToolbarSettings();
        ApplyNightModeToWebViews();
        UpdateCaptionButtonTheme();
        UpdateAddressDisplay(AddressBox.Text);
        _settingsWindow?.ApplyAppearance();
    }

    public void ApplyVerticalTabs(bool animate = false)
    {
        var settings = App.Services.Settings;
        var enabled = settings.VerticalTabs;
        var iconsOnly = enabled && settings.VerticalTabsIconsOnly;
        var width = iconsOnly ? 48 : settings.VerticalTabWidth;
        var wasVisible = VerticalTabPane.Visibility == Visibility.Visible;
        var previousWidth = VerticalTabPane.ActualWidth > 0 ? VerticalTabPane.ActualWidth : width;

        _verticalTabsAnimation?.Stop();
        VerticalWindowTitleBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled)
        {
            HorizontalTabStrip.Visibility = Visibility.Collapsed;
            VerticalTabPane.Visibility = Visibility.Visible;
            VerticalTabPane.Opacity = 1;
            var widthChange = animate &&
                              wasVisible &&
                              App.Services.Settings.AnimationDuration > 0 &&
                              Math.Abs(previousWidth - width) > 1;
            if (widthChange)
            {
                AnimateVerticalTabWidth(previousWidth, width);
            }
            else
            {
                VerticalTabsColumn.Width = new GridLength(width);
                VerticalTabPane.Width = width;
                EnsureTabPaneTransform().X =
                    animate && App.Services.Settings.AnimationDuration > 0 ? width : 0;
                if (animate && App.Services.Settings.AnimationDuration > 0)
                {
                    AnimateVerticalTabs(enabled: true, width, width);
                }
            }
        }
        else
        {
            if (animate && wasVisible && App.Services.Settings.AnimationDuration > 0)
            {
                HorizontalTabStrip.Visibility = Visibility.Collapsed;
                AnimateVerticalTabsOut(previousWidth);
            }
            else
            {
                HorizontalTabStrip.Visibility = Visibility.Visible;
                VerticalWindowTitleBar.Visibility = Visibility.Collapsed;
                VerticalTabsColumn.Width = new GridLength(0);
                VerticalTabPane.Visibility = Visibility.Collapsed;
                VerticalTabPane.Width = width;
                VerticalTabPane.Opacity = 1;
                EnsureTabPaneTransform().X = 0;
                RefreshHorizontalTabLayout();
            }
        }
        SetTitleBar(App.Services.Settings.VerticalTabs
            ? VerticalWindowTitleBar
            : TitleBarDragSurface);
        VerticalTabList.Visibility = iconsOnly ? Visibility.Collapsed : Visibility.Visible;
        VerticalIconTabList.Visibility = iconsOnly ? Visibility.Visible : Visibility.Collapsed;

        VerticalTabsHeaderTitle.Visibility = Visibility.Collapsed;
        VerticalNewTabLabel.Text = Localization.T("新建标签页");
        VerticalNewTabHeaderButton.Visibility = iconsOnly ? Visibility.Collapsed : Visibility.Visible;
        VerticalTabsHeaderGrid.Padding = iconsOnly
            ? new Thickness(0)
            : new Thickness(8, 0, 4, 0);
        VerticalTabLayoutHeaderButton.Visibility = iconsOnly ? Visibility.Collapsed : Visibility.Visible;
        VerticalTabLayoutFooterButton.Visibility = Visibility.Collapsed;
        VerticalNewTabContent.Margin = iconsOnly
            ? new Thickness(0)
            : new Thickness(8, 0, 8, 0);
        VerticalNewTabContent.HorizontalAlignment = iconsOnly
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;
        HorizontalTabScrollLeftButton.Visibility = Visibility.Collapsed;
        HorizontalTabScrollRightButton.Visibility = Visibility.Collapsed;
        ToolbarRow.Padding = new Thickness(4, 0, 8, 0);
        Grid.SetColumn(VerticalTabsCollapseButton, iconsOnly ? 0 : 3);
        Grid.SetColumnSpan(VerticalTabsCollapseButton, iconsOnly ? 4 : 1);
        VerticalTabsCollapseButton.HorizontalAlignment = iconsOnly
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Right;
        VerticalTabsCollapseIcon.Glyph = iconsOnly ? "\uE76C" : "\uE76B";
        ToolTipService.SetToolTip(
            VerticalTabsCollapseButton,
            iconsOnly ? "展开垂直标签页" : "仅显示标签图标");
        if (VerticalNewTabLabel is not null)
        {
            VerticalNewTabLabel.Visibility = iconsOnly ? Visibility.Collapsed : Visibility.Visible;
        }

        VerticalNewTabButton.HorizontalContentAlignment = iconsOnly
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;

        foreach (var tab in Tabs)
        {
            tab.ShowTitle = !iconsOnly;
        }

        if (enabled && _activeTab is not null)
        {
            _suppressTabSelection = true;
            VerticalTabList.SelectedItem = _activeTab;
            _suppressTabSelection = false;
        }

        DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(false));
    }

    private TranslateTransform EnsureTabPaneTransform()
    {
        if (VerticalTabPane.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            VerticalTabPane.RenderTransform = transform;
        }

        return transform;
    }

    private void AnimateVerticalTabWidth(double fromWidth, double targetWidth)
    {
        // Width storyboards can throw "Invalid attribute value Unknown for
        // property Width" on some WinUI/Composition builds and leave the whole
        // window without hit-testing. Snap the layout width and animate only
        // composition-safe properties.
        var transform = EnsureTabPaneTransform();
        VerticalTabsColumn.Width = new GridLength(targetWidth);
        VerticalTabPane.Visibility = Visibility.Visible;
        VerticalTabPane.Width = targetWidth;
        VerticalTabPane.Opacity = 0;
        transform.X = targetWidth > fromWidth ? 10 : -10;
        HideTabIndicators();

        void Finish()
        {
            VerticalTabPane.Opacity = 1;
            transform.X = 0;
            DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(false));
        }

        var durationMs = Math.Max(0, App.Services.Settings.AnimationDuration);
        if (durationMs <= 0)
        {
            Finish();
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs + 40));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(fade, VerticalTabPane);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var slide = new DoubleAnimation
        {
            From = transform.X,
            To = 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "X");
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Completed += (_, _) => Finish();
        _verticalTabsAnimation = storyboard;
        storyboard.Begin();
    }

    private void AnimateVerticalTabsOut(double fromWidth)
    {
        var transform = EnsureTabPaneTransform();
        transform.X = 0;
        VerticalTabsColumn.Width = new GridLength(fromWidth);
        VerticalTabPane.Visibility = Visibility.Visible;
        VerticalTabPane.Width = fromWidth;
        VerticalTabPane.Opacity = 1;
        HorizontalTabStrip.Visibility = Visibility.Visible;
        HorizontalTabStrip.Opacity = 0;
        HideTabIndicators();
        var finished = false;

        void Finish()
        {
            if (finished || App.Services.Settings.VerticalTabs)
            {
                return;
            }

            finished = true;
            transform.X = 0;
            VerticalTabsColumn.Width = new GridLength(0);
            VerticalTabPane.Visibility = Visibility.Collapsed;
            VerticalTabPane.Width = Math.Max(0, fromWidth);
            VerticalTabPane.Opacity = 1;
            HorizontalTabStrip.Visibility = Visibility.Visible;
            HorizontalTabStrip.Opacity = 1;
            RefreshHorizontalTabLayout();
        }

        var durationMs = Math.Max(0, App.Services.Settings.AnimationDuration);
        if (durationMs <= 0)
        {
            Finish();
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs + 40));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(fade, VerticalTabPane);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var slide = new DoubleAnimation
        {
            From = 0,
            To = -Math.Min(40, fromWidth),
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "X");
        var horizontalFade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(horizontalFade, HorizontalTabStrip);
        Storyboard.SetTargetProperty(horizontalFade, "Opacity");
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Children.Add(horizontalFade);
        storyboard.Completed += (_, _) => Finish();
        _verticalTabsAnimation = storyboard;
        storyboard.Begin();

        var fallback = DispatcherQueue.CreateTimer();
        fallback.Interval = TimeSpan.FromMilliseconds(durationMs + 180);
        fallback.IsRepeating = false;
        fallback.Tick += (_, _) => Finish();
        fallback.Start();
    }

    private void AnimateVerticalTabs(bool enabled, double fromWidth, double targetWidth)
    {
        var transform = EnsureTabPaneTransform();
        var startX = enabled ? targetWidth : 0;
        var endX = enabled ? 0 : Math.Max(fromWidth, targetWidth);
        VerticalTabPane.Width = enabled ? targetWidth : Math.Max(fromWidth, targetWidth);
        VerticalTabPane.Opacity = 1;
        transform.X = startX;

        if (_horizontalTabIndicator is not null)
        {
            _horizontalTabIndicator.Visibility = Visibility.Collapsed;
        }

        if (_verticalTabIndicator is not null)
        {
            _verticalTabIndicator.Visibility = Visibility.Collapsed;
        }

        void Finish()
        {
            transform.X = 0;
            if (enabled)
            {
                VerticalTabsColumn.Width = new GridLength(targetWidth);
                VerticalTabPane.Visibility = Visibility.Visible;
                VerticalTabPane.Width = targetWidth;
            }
            else
            {
                VerticalTabsColumn.Width = new GridLength(0);
                VerticalTabPane.Visibility = Visibility.Collapsed;
                VerticalTabPane.Width = targetWidth;
            }

            DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(false));
        }

        var durationMs = Math.Max(0, App.Services.Settings.AnimationDuration);
        if (durationMs <= 0)
        {
            transform.X = endX;
            Finish();
            return;
        }

        var animation = new DoubleAnimation
        {
            From = startX,
            To = endX,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs + 60)),
            EasingFunction = new BackEase
            {
                EasingMode = EasingMode.EaseOut,
                Amplitude = 0.35
            }
        };
        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, "X");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => Finish();
        _verticalTabsAnimation = storyboard;
        storyboard.Begin();
    }

    private void CreateTabIndicators()
    {
        _horizontalTabIndicator = CreateTabIndicator();
        Grid.SetColumn(_horizontalTabIndicator, 0);
        Grid.SetColumnSpan(_horizontalTabIndicator, 3);
        Canvas.SetZIndex(_horizontalTabIndicator, 10);
        HorizontalTabStrip.Children.Add(_horizontalTabIndicator);

        _horizontalHoverIndicator = CreateTabIndicator();
        _horizontalHoverIndicator.BorderThickness = new Thickness(0);
        Grid.SetColumn(_horizontalHoverIndicator, 0);
        Grid.SetColumnSpan(_horizontalHoverIndicator, 3);
        Canvas.SetZIndex(_horizontalHoverIndicator, 9);
        HorizontalTabStrip.Children.Add(_horizontalHoverIndicator);
        HorizontalTabList.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(HorizontalTabList_PointerMoved),
            true);
        HorizontalTabList.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(TabList_PointerPressedForMenu),
            true);
        VerticalTabList.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(TabList_PointerPressedForMenu),
            true);
        HorizontalTabList.PointerExited += (_, _) =>
        {
            HideHorizontalHoverIndicator();
            ScheduleTabPreviewHide();
        };

        _verticalTabIndicator = CreateTabIndicator();
        // The indicator spans the whole rail so its render transform can use
        // coordinates relative to the rail root. If it starts in row 1, the
        // row offset is applied twice and the mask appears one row too low.
        Grid.SetRow(_verticalTabIndicator, 0);
        Grid.SetRowSpan(_verticalTabIndicator, 3);
        Canvas.SetZIndex(_verticalTabIndicator, 10);
        VerticalTabsLayoutGrid.Children.Add(_verticalTabIndicator);

        _verticalUnderlineIndicator = CreateTabIndicator();
        _verticalUnderlineIndicator.BorderThickness = new Thickness(0);
        _verticalUnderlineIndicator.CornerRadius = new CornerRadius(1);
        Grid.SetRow(_verticalUnderlineIndicator, 0);
        Grid.SetRowSpan(_verticalUnderlineIndicator, 3);
        Canvas.SetZIndex(_verticalUnderlineIndicator, 11);
        VerticalTabsLayoutGrid.Children.Add(_verticalUnderlineIndicator);

        Tabs.CollectionChanged += (_, _) => QueueHorizontalTabSizing();
        HorizontalTabStrip.SizeChanged += (_, _) =>
        {
            UpdateTabIndicator(false);
            QueueHorizontalTabSizing();
        };
        _tabHoverPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _tabHoverPollTimer.Tick += TabHoverPollTimer_Tick;
        _tabHoverPollTimer.Start();
        VerticalTabsLayoutGrid.SizeChanged += (_, _) => UpdateTabIndicator(false);
        HorizontalTabList.ContainerContentChanging += (_, _) => ScheduleTabIndicatorUpdate(false);
        HorizontalTabList.ContainerContentChanging += HorizontalTabList_ContainerContentChanging;
        VerticalTabList.ContainerContentChanging += (_, _) => ScheduleTabIndicatorUpdate(false);
        UpdateTabIndicatorColor();
    }

    private static Border CreateTabIndicator() => new()
    {
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(0, 0, 0, 2),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        RenderTransform = new TranslateTransform()
    };

    public void UpdateTabIndicatorColor()
    {
        var settings = App.Services.Settings;
        var dark = settings.NightMode ||
                   settings.ThemeMode == "Dark" ||
                   (settings.ThemeMode == "System" && RootGrid.ActualTheme == ElementTheme.Dark);
        var brush = new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : Windows.UI.Color.FromArgb(255, 0, 0, 0))
        {
            Opacity = dark ? 0.17 : 0.115
        };
        var accent = new SolidColorBrush(ThemeAccent.Parse(settings.AccentColor));
        if (_horizontalTabIndicator is not null)
        {
            _horizontalTabIndicator.Background = brush;
            _horizontalTabIndicator.BorderBrush = accent;
        }

        if (_horizontalHoverIndicator is not null)
        {
            _horizontalHoverIndicator.Background = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 0, 0, 0))
            {
                Opacity = dark ? 0.08 : 0.055
            };
        }

        if (_verticalTabIndicator is not null)
        {
            _verticalTabIndicator.Background = brush;
            _verticalTabIndicator.BorderBrush = accent;
        }
        if (_verticalUnderlineIndicator is not null)
        {
            _verticalUnderlineIndicator.Background = accent;
        }
    }

    private void ScheduleTabIndicatorUpdate(bool animate)
    {
        if (_deferTabIndicator)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(animate));
        _tabIndicatorUpdateCts?.Cancel();
        var cts = new CancellationTokenSource();
        _tabIndicatorUpdateCts = cts;
        var correctionDelay = Math.Max(120, App.Services.Settings.AnimationDuration + 80);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(correctionDelay, cts.Token);
                if (!cts.IsCancellationRequested)
                {
                    DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(false));
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void HideTabIndicators()
    {
        if (_horizontalTabIndicator is not null)
        {
            _horizontalTabIndicator.Visibility = Visibility.Collapsed;
        }

        if (_verticalTabIndicator is not null)
        {
            _verticalTabIndicator.Visibility = Visibility.Collapsed;
        }

        if (_verticalUnderlineIndicator is not null)
        {
            _verticalUnderlineIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private async Task RevealNewTabIndicatorAsync()
    {
        // Give the tab item enough time to enter the visual tree, then wait
        // for the icon list itself (not the collapsed list view) to have a
        // real size. Positioning before layout is what put the mask at (0,0).
        await Task.Delay(40);
        var settings = App.Services.Settings;
        for (var attempt = 0; attempt < 18; attempt++)
        {
            var ready = settings.VerticalTabs
                ? (settings.VerticalTabsIconsOnly
                    ? VerticalIconTabList.ActualHeight > 0 && VerticalIconTabList.ActualWidth > 0
                    : VerticalTabList.ContainerFromItem(_activeTab) is ListViewItem { ActualHeight: > 0 })
                : HorizontalTabList.ContainerFromItem(_activeTab) is ListViewItem { ActualHeight: > 0 };
            if (ready)
            {
                break;
            }

            await Task.Delay(16);
        }

        // Let the new tab item finish its reveal/layout before the selection
        // mask fades in; otherwise the mask appears before the tab itself.
        await Task.Delay(110);
        DispatcherQueue.TryEnqueue(() =>
        {
            var indicator = App.Services.Settings.VerticalTabs
                ? _verticalTabIndicator
                : _horizontalTabIndicator;
            if (indicator is not null)
            {
                indicator.Opacity = 0;
            }
            _deferTabIndicator = false;
            if (indicator is null)
            {
                return;
            }

            UpdateTabIndicator(false);
            if (indicator.Visibility != Visibility.Visible)
            {
                // One more layout pass; never fade in at the wrong origin.
                DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(false));
            }

            indicator.Opacity = 0;
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(
                    Math.Max(100, App.Services.Settings.AnimationDuration))),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, indicator);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Begin();
        });
    }

    private void HorizontalTabList_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (App.Services.Settings.VerticalTabs || _horizontalHoverIndicator is null)
        {
            return;
        }

        var node = e.OriginalSource as DependencyObject;
        while (node is not null && node is not ListViewItem)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        if (node is not ListViewItem container)
        {
            HideHorizontalHoverIndicator();
            ScheduleTabPreviewHide();
            return;
        }

        var point = container.TransformToVisual(HorizontalTabStrip)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        if (_horizontalHoverIndicator.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            _horizontalHoverIndicator.RenderTransform = transform;
        }

        var width = Math.Min(240, Math.Max(4, container.ActualWidth));
        var height = Math.Min(32, Math.Max(4, container.ActualHeight));
        transform.X = point.X + Math.Max(0, (container.ActualWidth - width) / 2);
        transform.Y = point.Y + Math.Max(0, (container.ActualHeight - height) / 2);
        _horizontalHoverIndicator.Width = width;
        _horizontalHoverIndicator.Height = height;
        _horizontalHoverIndicator.Visibility = Visibility.Visible;
        ScheduleTabPreview(container);
    }

    private void HorizontalTabList_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem item)
        {
            return;
        }

        item.AddHandler(
            UIElement.PointerEnteredEvent,
            new PointerEventHandler(TabItem_PointerEntered),
            true);
        item.AddHandler(
            UIElement.PointerExitedEvent,
            new PointerEventHandler(TabItem_PointerExited),
            true);
    }

    private void TabItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ListViewItem item)
        {
            ScheduleTabPreview(item);
        }
    }

    private void TabItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ScheduleTabPreviewHide();
    }

    private void TabHoverPollTimer_Tick(object? sender, object e)
    {
        if (Tabs.Count == 0)
        {
            HideTabPreview();
            return;
        }

        if (GetForegroundWindow() != WindowNative.GetWindowHandle(this) ||
            !GetCursorPos(out var cursor) ||
            !GetWindowRect(WindowNative.GetWindowHandle(this), out var windowRect))
        {
            HideTabPreview();
            return;
        }

        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        var vertical = App.Services.Settings.VerticalTabs;
        var tabList = vertical ? VerticalTabList : HorizontalTabList;
        if (tabList.Visibility != Visibility.Visible)
        {
            HideTabPreview();
            return;
        }

        var listPoint = tabList.TransformToVisual(RootGrid)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        var listLeft = windowRect.Left + listPoint.X * scale;
        var listTop = windowRect.Top + listPoint.Y * scale;
        var listRight = listLeft + tabList.ActualWidth * scale;
        var listBottom = listTop + tabList.ActualHeight * scale;
        if (cursor.X < listLeft || cursor.X > listRight ||
            cursor.Y < listTop || cursor.Y > listBottom)
        {
            HideTabPreview();
            return;
        }

        ListViewItem? hovered = null;
        foreach (var tab in Tabs)
        {
            if (tabList.ContainerFromItem(tab) is not ListViewItem container)
            {
                continue;
            }

            try
            {
                var point = container.TransformToVisual(RootGrid)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                var left = windowRect.Left + point.X * scale;
                var top = windowRect.Top + point.Y * scale;
                var right = left + container.ActualWidth * scale;
                var bottom = top + container.ActualHeight * scale;
                if (cursor.X >= left && cursor.X <= right &&
                    cursor.Y >= top && cursor.Y <= bottom)
                {
                    hovered = container;
                    break;
                }
            }
            catch
            {
                // Container can be recycled during the scan.
            }
        }

        if (hovered is null)
        {
            HideTabPreview();
            return;
        }

        ScheduleTabPreview(hovered);
    }

    private void ScheduleTabPreview(ListViewItem container)
    {
        _tabPreviewHideTimer?.Stop();
        if (ReferenceEquals(container, _tabPreviewContainer))
        {
            return;
        }

        _tabPreviewContainer = container;
        _tabPreviewTab = container.Content as BrowserTab;
        _tabPreviewHoverStartedAt = DateTime.UtcNow;
        _tabPreviewTimer ??= new DispatcherTimer
        {
            Interval = TabPreviewHoverDelay
        };
        _tabPreviewTimer.Stop();
        _tabPreviewTimer.Tick -= TabPreviewTimer_Tick;
        _tabPreviewTimer.Tick += TabPreviewTimer_Tick;
        _tabPreviewTimer.Start();
    }

    private void TabPreviewTimer_Tick(object? sender, object e)
    {
        _tabPreviewTimer?.Stop();
        if (_tabPreviewContainer is null || _tabPreviewTab is null)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - _tabPreviewHoverStartedAt;
        if (elapsed < TabPreviewHoverDelay)
        {
            _tabPreviewTimer.Interval = TabPreviewHoverDelay - elapsed;
            _tabPreviewTimer.Start();
            return;
        }

        ShowTabPreview(_tabPreviewTab, _tabPreviewContainer);
    }

    private static async Task CacheTabPreviewAsync(BrowserTab tab)
    {
        var bitmap = await CaptureTabPreviewAsync(tab);
        if (bitmap is not null)
        {
            tab.PreviewImage = bitmap;
        }
    }

    private static async Task<BitmapImage?> CaptureTabPreviewAsync(BrowserTab tab)
    {
        if (tab.Core is null)
        {
            return null;
        }

        try
        {
            var json = await tab.Core.CallDevToolsProtocolMethodAsync(
                "Page.captureScreenshot",
                "{\"format\":\"png\",\"captureBeyondViewport\":false}");
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataElement))
            {
                return null;
            }

            var bytes = Convert.FromBase64String(dataElement.GetString() ?? string.Empty);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await tab.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }

    private async void ShowTabPreview(BrowserTab tab, ListViewItem container)
    {
        HideTabPreview();
        _tabPreviewTab = tab;
        _tabPreviewContainer = container;
        var previewDark = AppSurface.IsDark(
            App.Services.Settings.NightMode,
            App.Services.Settings.ThemeMode,
            RootGrid.RequestedTheme);
        var previewText = new SolidColorBrush(previewDark
            ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : Windows.UI.Color.FromArgb(255, 0, 0, 0));
        var image = new Image
        {
            Height = 150,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (tab.Favicon is not null)
        {
            image.Source = tab.Favicon;
        }
        _tabPreviewImage = image;
        var title = new TextBlock
        {
            Text = tab.Title,
            Foreground = previewText,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        var url = new TextBlock
        {
            Text = tab.Url,
            Foreground = previewText,
            FontSize = 11,
            Opacity = 0.65,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        var content = new StackPanel { Width = 300, Spacing = 6 };
        content.Children.Add(image);
        content.Children.Add(title);
        content.Children.Add(url);
        var card = new Border
        {
            Background = new SolidColorBrush(previewDark
                ? Windows.UI.Color.FromArgb(255, 32, 34, 40)
                : Windows.UI.Color.FromArgb(255, 248, 249, 252)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(64, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = content
        };

        _tabPreviewPopup = new Popup
        {
            Child = card,
            IsLightDismissEnabled = false,
            XamlRoot = RootGrid.XamlRoot
        };
        var origin = container.TransformToVisual(RootGrid)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        var x = Math.Clamp(origin.X, 8, Math.Max(8, RootGrid.ActualWidth - 308));
        var y = origin.Y + container.ActualHeight + 6;
        if (App.Services.Settings.VerticalTabs)
        {
            x = Math.Clamp(origin.X + container.ActualWidth + 10, 8,
                Math.Max(8, RootGrid.ActualWidth - 308));
            y = Math.Clamp(origin.Y, 8, Math.Max(8, RootGrid.ActualHeight - 260));
        }

        _tabPreviewPopup.HorizontalOffset = x;
        _tabPreviewPopup.VerticalOffset = y;
        card.PointerEntered += (_, _) => _tabPreviewHideTimer?.Stop();
        card.PointerExited += (_, _) => ScheduleTabPreviewHide();

        if (tab.PreviewImage is not null && !ReferenceEquals(tab, _activeTab))
        {
            image.Source = tab.PreviewImage;
            image.Stretch = Stretch.UniformToFill;
        }
        else if (tab.Core is not null)
        {
            var wasSuspended = tab.IsSuspended;
            if (wasSuspended)
            {
                tab.Core.Resume();
                tab.IsSuspended = false;
            }

            var bitmap = await CaptureTabPreviewAsync(tab);
            if (bitmap is not null &&
                ReferenceEquals(_tabPreviewImage, image) &&
                ReferenceEquals(_tabPreviewTab, tab))
            {
                image.Source = bitmap;
                image.Stretch = Stretch.UniformToFill;
                tab.PreviewImage = bitmap;
            }

            if (wasSuspended && !ReferenceEquals(tab, _activeTab))
            {
                try
                {
                    if (await tab.Core.TrySuspendAsync())
                    {
                        tab.IsSuspended = true;
                    }
                }
                catch
                {
                    tab.IsSuspended = false;
                }
            }
        }

        if (!ReferenceEquals(_tabPreviewTab, tab) || _tabPreviewContainer is null)
        {
            return;
        }

        _tabPreviewPopup.IsOpen = true;
    }

    private void ScheduleTabPreviewHide()
    {
        _tabPreviewHoverStartedAt = DateTime.MinValue;
        _tabPreviewHideTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _tabPreviewHideTimer.Stop();
        _tabPreviewHideTimer.Tick -= TabPreviewHideTimer_Tick;
        _tabPreviewHideTimer.Tick += TabPreviewHideTimer_Tick;
        _tabPreviewHideTimer.Start();
    }

    private void TabPreviewHideTimer_Tick(object? sender, object e)
    {
        _tabPreviewHideTimer?.Stop();
        HideTabPreview();
    }

    private void StartTabPreviewWatch()
    {
        _tabPreviewWatchTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _tabPreviewWatchTimer.Tick -= TabPreviewWatchTimer_Tick;
        _tabPreviewWatchTimer.Tick += TabPreviewWatchTimer_Tick;
        _tabPreviewWatchTimer.Start();
    }

    private void TabPreviewWatchTimer_Tick(object? sender, object e)
    {
        if (_tabPreviewPopup is null || !_tabPreviewPopup.IsOpen)
        {
            _tabPreviewWatchTimer?.Stop();
            return;
        }

        if (GetForegroundWindow() != WindowNative.GetWindowHandle(this) ||
            !GetCursorPos(out var cursor) ||
            _appWindow is null)
        {
            HideTabPreview();
            return;
        }

        var position = _appWindow.Position;
        var size = _appWindow.Size;
        if (cursor.X < position.X || cursor.X > position.X + size.Width ||
            cursor.Y < position.Y || cursor.Y > position.Y + size.Height)
        {
            HideTabPreview();
        }
    }

    private void HideTabPreview()
    {
        _tabPreviewTimer?.Stop();
        _tabPreviewHideTimer?.Stop();
        _tabPreviewWatchTimer?.Stop();
        _tabPreviewContainer = null;
        _tabPreviewTab = null;
        _tabPreviewHoverStartedAt = DateTime.MinValue;
        if (_tabPreviewPopup is not null)
        {
            _tabPreviewPopup.IsOpen = false;
            _tabPreviewPopup = null;
        }
        _tabPreviewImage = null;
    }

    private void HideHorizontalHoverIndicator()
    {
        if (_horizontalHoverIndicator is not null)
        {
            _horizontalHoverIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void QueueHorizontalTabSizing()
    {
        if (_horizontalSizeQueued)
        {
            return;
        }

        _horizontalSizeQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _horizontalSizeQueued = false;
            UpdateHorizontalTabSizes();
        });
    }

    private void HorizontalTabList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHorizontalTabSizes();
    }

    private void UpdateHorizontalTabSizes()
    {
        if (App.Services.Settings.VerticalTabs || HorizontalTabList is null)
        {
            return;
        }

        var count = Tabs.Count;
        var available = Math.Max(0, HorizontalTabList.ActualWidth - 100);
        if (count == 0 || available <= 0)
        {
            return;
        }

        var width = Math.Min(240, Math.Max(40, available / count - 6));
        foreach (var tab in Tabs)
        {
            tab.HorizontalTabWidth = width;
            tab.ShowTitle = width >= 92;
        }

        var overflow = count * (width + 4) > available + 2;
        HorizontalTabScrollLeftButton.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        HorizontalTabScrollRightButton.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;

        var listWidth = HorizontalTabList.ActualWidth;
        HorizontalTabList.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(
                0, 0, Math.Max(0, listWidth - 52), HorizontalTabList.ActualHeight)
        };

        var listOrigin = HorizontalTabList.TransformToVisual(HorizontalTabStrip)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        var tabsWidth = count * (width + 4);
        var targetX = listOrigin.X + 4 + tabsWidth + 2;
        var maxX = listOrigin.X + listWidth - 44;
        if (overflow || count > 8)
        {
            targetX = maxX;
        }

        HorizontalNewTabTransform.X = Math.Clamp(targetX, 4, Math.Max(4, maxX));
        var newTabWidth = HorizontalNewTabButton.ActualWidth > 0
            ? HorizontalNewTabButton.ActualWidth
            : 36;
        TitleBarDragSurface.Width = Math.Max(
            0,
            listOrigin.X + listWidth - HorizontalNewTabTransform.X - newTabWidth);
    }

    private void HorizontalTabScrollLeft_Click(object sender, RoutedEventArgs e) =>
        ScrollHorizontalTabs(-160);

    private void HorizontalTabScrollRight_Click(object sender, RoutedEventArgs e) =>
        ScrollHorizontalTabs(160);

    private void ScrollHorizontalTabs(double delta)
    {
        var viewer = FindScrollViewer(HorizontalTabList);
        viewer?.ChangeView(viewer.HorizontalOffset + delta, null, null, true);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        if (node is ScrollViewer viewer)
        {
            return viewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(node, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void HorizontalTabStrip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!App.Services.Settings.VerticalTabs)
        {
            UpdateTabIndicator(false);
        }
    }

    private void UpdateCaptionRegions()
    {
        if (_appWindow is null)
        {
            return;
        }

        try
        {
            var source = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
            if (App.Services.Settings.VerticalTabs || RootGrid.ActualWidth <= 0)
            {
                source.SetRegionRects(NonClientRegionKind.Caption, Array.Empty<RectInt32>());
                source.SetRegionRects(NonClientRegionKind.Passthrough, Array.Empty<RectInt32>());
                return;
            }

            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            var width = (int)Math.Ceiling(RootGrid.ActualWidth * scale);
            var height = (int)Math.Ceiling(40 * scale);
            source.SetRegionRects(
                NonClientRegionKind.Caption,
                new[] { new RectInt32(0, 0, width, height) });

            var passthrough = new List<RectInt32>();
            AddPassthroughRegion(HorizontalTabList, passthrough, scale);
            AddPassthroughRegion(HorizontalNewTabButton, passthrough, scale);
            AddPassthroughRegion(CustomCaptionButtons, passthrough, scale);
            source.SetRegionRects(NonClientRegionKind.Passthrough, passthrough.ToArray());
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not update non-client caption regions.", ex);
        }
    }

    private void AddPassthroughRegion(
        FrameworkElement element,
        List<RectInt32> regions,
        double scale)
    {
        if (element.Visibility != Visibility.Visible ||
            element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        try
        {
            var point = element.TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            regions.Add(new RectInt32(
                (int)Math.Floor(point.X * scale),
                (int)Math.Floor(point.Y * scale),
                (int)Math.Ceiling(element.ActualWidth * scale),
                (int)Math.Ceiling(element.ActualHeight * scale)));
        }
        catch
        {
        }
    }

    private void RefreshHorizontalTabLayout()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            HorizontalTabList.UpdateLayout();
            UpdateHorizontalTabSizes();
            UpdateTabIndicator(false);
            DispatcherQueue.TryEnqueue(() => UpdateTabIndicator(false));
        });
    }

    private void UpdateTabIndicator(bool animate)
    {
        if (_deferTabIndicator)
        {
            HideTabIndicators();
            return;
        }

        if (_horizontalTabIndicator is null || _verticalTabIndicator is null || _activeTab is null)
        {
            return;
        }

        var settings = App.Services.Settings;
        if (!settings.VerticalTabs)
        {
            _verticalTabIndicator.Visibility = Visibility.Collapsed;
            if (_verticalUnderlineIndicator is not null)
            {
                _verticalUnderlineIndicator.Visibility = Visibility.Collapsed;
            }

            UpdateTabIndicatorColor();
            _horizontalTabIndicator.BorderThickness = new Thickness(0);
            PositionIndicator(
                _horizontalTabIndicator,
                HorizontalTabList,
                HorizontalTabStrip,
                new Thickness(0),
                animate,
                fixedHeight: 32);
            return;
        }

        _horizontalTabIndicator.Visibility = Visibility.Collapsed;
        HideHorizontalHoverIndicator();
        UpdateTabIndicatorColor();
        if (settings.VerticalTabsIconsOnly)
        {
            _verticalTabIndicator.BorderThickness = new Thickness(0);
            PositionIconTabIndicator(animate);
            return;
        }

        // Expanded vertical tabs use only the soft selection mask; the
        // underline belongs to the compact icon rail.
        _verticalTabIndicator.BorderThickness = new Thickness(0);
        if (_verticalUnderlineIndicator is not null)
        {
            _verticalUnderlineIndicator.Visibility = Visibility.Collapsed;
        }
        PositionIndicator(
            _verticalTabIndicator,
            VerticalTabList,
            VerticalTabsLayoutGrid,
            new Thickness(0),
            animate);
    }

    private void PositionIconTabIndicator(bool animate)
    {
        var index = _activeTab is null ? -1 : Tabs.IndexOf(_activeTab);
        if (index < 0 || VerticalIconTabList.Visibility != Visibility.Visible)
        {
            _verticalTabIndicator.Visibility = Visibility.Collapsed;
            if (_verticalUnderlineIndicator is not null)
            {
                _verticalUnderlineIndicator.Visibility = Visibility.Collapsed;
            }
            return;
        }

        try
        {
            var point = VerticalIconTabList
                .TransformToVisual(VerticalTabsLayoutGrid)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            const double itemHeight = 38;
            const double spacing = 3;
            var width = 40.0;
            var height = 38.0;
            var centerX = point.X + 20;
            var x = point.X;
            var y = point.Y + index * (itemHeight + spacing);
            if (VerticalIconTabList.ContainerFromIndex(index) is FrameworkElement container)
            {
                var containerOrigin = container
                    .TransformToVisual(VerticalTabsLayoutGrid)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                width = container.ActualWidth;
                height = container.ActualHeight;
                centerX = containerOrigin.X + width / 2;
                x = containerOrigin.X;
                y = containerOrigin.Y;
            }
            AnimateIndicator(
                _verticalTabIndicator,
                x,
                y,
                width,
                height,
                animate && _verticalTabIndicator.Visibility == Visibility.Visible);

            if (_verticalUnderlineIndicator is not null)
            {
                const double barWidth = 12;
                _verticalUnderlineIndicator.Width = barWidth;
                _verticalUnderlineIndicator.Height = 2;
                if (_verticalUnderlineIndicator.RenderTransform is TranslateTransform barTransform)
                {
                    barTransform.X = centerX - barWidth / 2;
                    barTransform.Y = y + height - 2;
                }
                _verticalUnderlineIndicator.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            _verticalTabIndicator.Visibility = Visibility.Collapsed;
            if (_verticalUnderlineIndicator is not null)
            {
                _verticalUnderlineIndicator.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void PositionIndicator(
        Border indicator,
        ListView list,
        FrameworkElement host,
        Thickness inset,
        bool animate,
        double? fixedWidth = null,
        double? fixedHeight = null)
    {
        var selectedIndex = ReferenceEquals(list, VerticalTabList)
            ? VerticalTabList.SelectedIndex
            : HorizontalTabList.SelectedIndex;
        if (_activeTab is null ||
            selectedIndex < 0 ||
            list.ContainerFromIndex(selectedIndex) is not ListViewItem container ||
            container.ActualWidth <= 0 ||
            container.ActualHeight <= 0)
        {
            indicator.Visibility = Visibility.Collapsed;
            return;
        }

        var point = container.TransformToVisual(host).TransformPoint(new Windows.Foundation.Point(0, 0));
        var width = fixedWidth is > 0
            ? Math.Min(fixedWidth.Value, Math.Max(4, container.ActualWidth - 2))
            : Math.Max(4, container.ActualWidth - inset.Left - inset.Right);
        var x = fixedWidth is > 0
            ? point.X + Math.Max(0, (container.ActualWidth - width) / 2)
            : point.X + inset.Left;
        var height = fixedHeight is > 0
            ? Math.Min(fixedHeight.Value, Math.Max(4, container.ActualHeight - 2))
            : Math.Max(4, container.ActualHeight - inset.Top - inset.Bottom);
        var y = fixedHeight is > 0
            ? point.Y + Math.Max(0, (container.ActualHeight - height) / 2)
            : point.Y + inset.Top;
        AnimateIndicator(
            indicator,
            x,
            y,
            width,
            height,
            animate && indicator.Visibility == Visibility.Visible);
    }

    private void AnimateIndicator(
        Border indicator,
        double x,
        double y,
        double width,
        double height,
        bool animate)
    {
        if (indicator.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            indicator.RenderTransform = transform;
        }

        var durationMs = App.Services.Settings.AnimationDuration;
        if (!animate || durationMs <= 0)
        {
            transform.X = x;
            transform.Y = y;
            indicator.Width = width;
            indicator.Height = height;
            indicator.Visibility = Visibility.Visible;
            return;
        }

        var fromX = transform.X;
        var fromY = transform.Y;
        indicator.Width = width;
        indicator.Height = height;
        indicator.Visibility = Visibility.Visible;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var storyboard = new Storyboard();

        void Add(string property, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        Add("X", fromX, x);
        Add("Y", fromY, y);

        _tabIndicatorAnimation?.Stop();
        _tabIndicatorAnimation = storyboard;
        storyboard.Begin();
    }

    private SolidColorBrush CreateChromeMaterialBrush()
    {
        var settings = App.Services.Settings;
        var dark = settings.NightMode ||
                   settings.ThemeMode == "Dark" ||
                   (settings.ThemeMode == "System" && RootGrid.ActualTheme == ElementTheme.Dark);
        var effect = settings.MicaMaterial;
        if (effect == "Acrylic")
        {
            var accent = ThemeAccent.Parse(settings.AcrylicTintColor);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(165, accent.R, accent.G, accent.B));
        }

        if (effect == "Rainbow")
        {
            return new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(145, 82, 92, 178)
                : Windows.UI.Color.FromArgb(145, 176, 154, 238));
        }

        if (effect is "Mica" or "Alt")
        {
            // Let the official system Mica material show through the chrome
            // instead of covering it with a custom tint.
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        return new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(175, 20, 22, 26)
            : Windows.UI.Color.FromArgb(175, 250, 250, 252));
    }

    private Brush? CreateChromeForegroundBrush()
    {
        if (CreateChromeMaterialBrush() is not SolidColorBrush background || background.Color.A < 40)
        {
            return null;
        }

        var color = background.Color;
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        return new SolidColorBrush(luminance < 0.55
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black);
    }

    private Brush CreateMenuSurfaceBrush()
    {
        var settings = App.Services.Settings;
        var dark = AppSurface.IsDark(
            settings.NightMode,
            settings.ThemeMode,
            RootGrid.RequestedTheme);
        return AppSurface.CreatePopup(
            dark,
            settings.MicaMaterial,
            ThemeAccent.Parse(settings.AcrylicTintColor));
    }

    private Style CreateMicaStyleFlyoutStyle()
    {
        var settings = App.Services.Settings;
        var dark = AppSurface.IsDark(
            settings.NightMode,
            settings.ThemeMode,
            RootGrid.RequestedTheme);
        var surface = new AcrylicBrush
        {
            TintColor = dark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                : Windows.UI.Color.FromArgb(255, 243, 243, 243),
            TintOpacity = 0.16,
            TintLuminosityOpacity = 0.90,
            FallbackColor = dark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                : Windows.UI.Color.FromArgb(255, 243, 243, 243)
        };
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.BackgroundProperty, surface));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(44, 128, 128, 128))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(14)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        return style;
    }

    private Style CreateLibraryFlyoutStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(48, 128, 128, 128))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(14)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        return style;
    }

    private void ApplyMicaToFlyout(Flyout flyout)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot)
                    .LastOrDefault(p => p.Child is FlyoutPresenter);
                if (popup is null)
                {
                    return;
                }

                popup.SystemBackdrop = new MicaBackdrop
                {
                    Kind = App.Services.Settings.MicaMaterial == "Alt"
                        ? MicaKind.BaseAlt
                        : MicaKind.Base
                };
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not apply Mica to flyout.", ex);
            }
        });
    }

    private Style CreateMaterialFlyoutStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.BackgroundProperty, CreateMenuSurfaceBrush()));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(48, 128, 128, 128))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        return style;
    }

    private Brush CreateMenuScrimBrush()
    {
        var settings = App.Services.Settings;
        var dark = AppSurface.IsDark(
            settings.NightMode,
            settings.ThemeMode,
            RootGrid.RequestedTheme);
        return new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(96, 28, 30, 36)
            : Windows.UI.Color.FromArgb(104, 248, 249, 252));
    }

    private SystemBackdrop CreatePopupBackdrop()
    {
        var settings = App.Services.Settings;
        var effect = settings.MicaMaterial;
        if (effect is "Mica" or "Alt")
        {
            return new ThemeMicaBackdrop
            {
                Theme = RootGrid.RequestedTheme,
                Kind = effect == "Alt" ? MicaKind.BaseAlt : MicaKind.Base,
                Transparency = settings.MicaTransparency,
                AccentColor = ThemeAccent.Parse(settings.AccentColor),
                UseDarkTint = AppSurface.IsDark(
                    settings.NightMode,
                    settings.ThemeMode,
                    RootGrid.RequestedTheme)
            };
        }

        return new DesktopAcrylicBackdrop();
    }

    private Style CreateMaterialMenuStyle()
    {
        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(MenuFlyoutPresenter.SystemBackdropProperty, CreatePopupBackdrop()));
        style.Setters.Add(new Setter(Control.BackgroundProperty, CreateMenuScrimBrush()));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            new SolidColorBrush(Windows.UI.Color.FromArgb(48, 128, 128, 128))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 340.0));
        return style;
    }

    public void ApplyToolbarSettings()
    {
        var s = App.Services.Settings;
        var configured = new[]
        {
            s.ShowHomeButton, s.ShowFavoriteButton, s.ShowShieldButton,
            s.ShowPasswordsButton, s.ShowFavoritesButton, s.ShowExtensionsButton,
            s.ShowDownloadsButton, s.ShowHistoryButton,
            s.ShowTranslateButton, s.ShowProxyButton, s.ShowGhostButton
        };
        var allEnabled = configured.All(value => value);

        static Visibility Show(bool enabled) =>
            enabled ? Visibility.Visible : Visibility.Collapsed;

        HomeButton.Visibility = Show(s.ShowHomeButton);
        FavoriteButton.Visibility = Show(s.ShowFavoriteButton);
        ShieldButton.Visibility = Show(s.ShowShieldButton);
        PasswordsButton.Visibility = Show(s.ShowPasswordsButton);
        FavoritesButton.Visibility = Show(s.ShowFavoritesButton);
        ExtensionsButton.Visibility = Show(s.ShowExtensionsButton);
        DownloadsButton.Visibility = Show(s.ShowDownloadsButton);
        HistoryButton.Visibility = Show(s.ShowHistoryButton);
        if (_translateButton is not null) _translateButton.Visibility = Show(s.ShowTranslateButton);
        if (_proxyButton is not null) _proxyButton.Visibility = Show(s.ShowProxyButton);
        if (_ghostButton is not null) _ghostButton.Visibility = Show(s.ShowGhostButton);

        var optional = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase)
        {
            ["Favorite"] = FavoriteButton,
            ["Shield"] = ShieldButton,
            ["Passwords"] = PasswordsButton,
            ["Favorites"] = FavoritesButton,
            ["Extensions"] = ExtensionsButton,
            ["Downloads"] = DownloadsButton,
            ["Translate"] = _translateButton,
            ["Proxy"] = _proxyButton,
            ["Ghost"] = _ghostButton,
            ["History"] = HistoryButton
        };
        var defaultOrder = new[]
        {
            "Favorite", "Shield", "Passwords", "Favorites", "History", "Extensions", "Downloads",
            "Translate", "Proxy", "Ghost"
        };
        var configuredOrder = (s.ToolbarOrder ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        foreach (var key in defaultOrder)
        {
            if (!configuredOrder.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                configuredOrder.Add(key);
            }
        }

        var column = 5;
        foreach (var key in configuredOrder)
        {
            if (optional.TryGetValue(key, out var button) && button is not null && column <= 14)
            {
                Grid.SetColumn(button, column++);
            }
        }

        ToolbarExpandButton.Visibility = allEnabled && !HasPopupExtensions
            ? Visibility.Collapsed
            : Visibility.Visible;
        ToolbarExpandIcon.Glyph = "\uE70D";
        ToolTipService.SetToolTip(ToolbarExpandButton, "展开隐藏的工具栏按钮");
    }

    private void AttachToolbarContextMenus()
    {
        var settings = App.Services.Settings;
        Attach(HomeButton, "主页", () => settings.ShowHomeButton, value => settings.ShowHomeButton = value);
        Attach(FavoriteButton, "收藏此页", () => settings.ShowFavoriteButton, value => settings.ShowFavoriteButton = value);
        Attach(ShieldButton, "广告拦截", () => settings.ShowShieldButton, value => settings.ShowShieldButton = value);
        Attach(PasswordsButton, "密码管理器", () => settings.ShowPasswordsButton, value => settings.ShowPasswordsButton = value);
        Attach(FavoritesButton, "收藏夹", () => settings.ShowFavoritesButton, value => settings.ShowFavoritesButton = value);
        Attach(ExtensionsButton, "扩展", () => settings.ShowExtensionsButton, value => settings.ShowExtensionsButton = value);
        Attach(DownloadsButton, "下载", () => settings.ShowDownloadsButton, value => settings.ShowDownloadsButton = value);
        Attach(HistoryButton, "历史记录", () => settings.ShowHistoryButton, value => settings.ShowHistoryButton = value);
        if (_translateButton is not null)
            Attach(_translateButton, "翻译", () => settings.ShowTranslateButton, value => settings.ShowTranslateButton = value);
        if (_proxyButton is not null)
            Attach(_proxyButton, "代理", () => settings.ShowProxyButton, value => settings.ShowProxyButton = value);
        if (_ghostButton is not null)
            Attach(_ghostButton, "Ghost Downloader", () => settings.ShowGhostButton, value => settings.ShowGhostButton = value);

        void Attach(Button button, string label, Func<bool> getValue, Action<bool> setValue)
        {
            var flyout = new MenuFlyout
            {
                MenuFlyoutPresenterStyle = CreateMaterialMenuStyle()
            };
            var toggle = new MenuFlyoutItem();
            flyout.Opening += (_, _) =>
            {
                toggle.Text = getValue()
                    ? $"\u9690\u85cf\u201c{label}\u201d\u5230\u5c55\u5f00\u5de5\u5177\u680f"
                    : $"\u5728\u5de5\u5177\u680f\u663e\u793a\u201c{label}\u201d";
                toggle.Icon = null;
            };
            toggle.Click += (_, _) =>
            {
                setValue(!getValue());
                App.Services.SettingsStore.Save();
                ApplyToolbarSettings();
            };
            flyout.Items.Add(toggle);
            flyout.Items.Add(new MenuFlyoutSeparator());
            var manage = new MenuFlyoutItem { Text = "\u5de5\u5177\u680f\u8bbe\u7f6e\u2026" };
            manage.Click += (_, _) => OpenSettings("appearance");
            flyout.Items.Add(manage);
            button.ContextFlyout = flyout;
        }
    }

    private void InstallDownloadDeleteButtons()
    {
        DownloadsList.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue || args.Item is not DownloadItem item) return;
            var container = args.ItemContainer;
            container.Loaded += (_, _) => AttachDownloadDeleteButton(container, item);
        };
    }

    private void AttachDownloadDeleteButton(DependencyObject container, DownloadItem item)
    {
        var cancel = FindDescendant<Button>(container, button =>
            string.Equals(button.Content?.ToString(), "取消", StringComparison.Ordinal));
        if (cancel?.Parent is not Grid actions) return;
        if (actions.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "删除", StringComparison.Ordinal)))
        {
            return;
        }

        var delete = new Button
        {
            Content = "删除",
            FontSize = 11,
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            Tag = item.FilePath,
            Visibility = item.CancelVisibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible
        };
        ToolTipService.SetToolTip(delete, "从下载列表移除（不删除文件）");
        Grid.SetColumn(delete, 4);
        delete.Click += DeleteDownload_Click;
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DownloadItem.CancelVisibility) or nameof(DownloadItem.IsComplete) or nameof(DownloadItem.IsFailed))
            {
                delete.Visibility = item.CancelVisibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        };
        actions.Children.Add(delete);
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T candidate && predicate(candidate)) return candidate;
            var nested = FindDescendant(child, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void BuildAddressContextMenu()
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = CreateMaterialMenuStyle() };
        var cut = new MenuFlyoutItem { Text = "剪切" };
        cut.Click += (_, _) => AddressBox.CutSelectionToClipboard();
        var copy = new MenuFlyoutItem { Text = "复制" };
        copy.Click += (_, _) => AddressBox.CopySelectionToClipboard();
        var paste = new MenuFlyoutItem { Text = "粘贴" };
        paste.Click += (_, _) => AddressBox.PasteFromClipboard();
        var search = new MenuFlyoutItem { Text = "在网页中搜索" };
        search.Click += (_, _) =>
        {
            var text = AddressBox.SelectedText?.Trim() ?? string.Empty;
            if (text.Length > 0) NavigateActive(SearchUri(text));
        };
        var selectAll = new MenuFlyoutItem { Text = "全选" };
        selectAll.Click += (_, _) => AddressBox.SelectAll();
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(search);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(selectAll);
        menu.Opening += (_, _) =>
        {
            var hasSelection = AddressBox.SelectionLength > 0;
            cut.IsEnabled = hasSelection;
            copy.IsEnabled = hasSelection;
            search.IsEnabled = hasSelection;
        };
        AddressBox.ContextFlyout = menu;
    }

    public void ApplyBrowserZoom()
    {
        var percent = Math.Clamp(App.Services.Settings.BrowserZoom, 25, 300);
        foreach (var tab in Tabs)
        {
            ApplyPageZoom(tab.Core);
        }

        if (ZoomOverlay is not null)
        {
            ZoomOverlayText.Text = $"{percent}%";
        }
    }

    private void ApplyPageZoom(CoreWebView2? core)
    {
        if (core is null)
        {
            return;
        }

        var factor = Math.Clamp(App.Services.Settings.BrowserZoom, 25, 300) / 100.0;
        var value = factor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _ = core.ExecuteScriptAsync(
            $"(function(){{ document.documentElement.style.zoom = '{value}'; }})()");
    }

    public void SetBrowserZoom(int percent, bool showOverlay = true)
    {
        var value = Math.Clamp(percent, 25, 300);
        App.Services.Settings.BrowserZoom = value;
        App.Services.SettingsStore.Save();
        ApplyBrowserZoom();
        if (showOverlay)
        {
            ShowZoomOverlay();
        }
    }

    private void BrowserHost_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if ((e.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) == 0)
        {
            return;
        }

        var delta = e.GetCurrentPoint(BrowserHost).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        e.Handled = true;
        SetBrowserZoom(App.Services.Settings.BrowserZoom + (delta > 0 ? 10 : -10));
    }

    private void ShowZoomOverlay()
    {
        if (ZoomOverlay is null)
        {
            return;
        }

        ZoomOverlayText.Text = $"{App.Services.Settings.BrowserZoom}%";
        ZoomOverlay.Visibility = Visibility.Visible;
        ZoomOverlay.Opacity = 0;
        ZoomOverlayTransform.Y = -8;

        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(fade, ZoomOverlay);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = -8,
            To = 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(slide, ZoomOverlayTransform);
        Storyboard.SetTargetProperty(slide, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();

        _zoomOverlayTimer.Stop();
        _zoomOverlayTimer.Start();
    }

    private void HideZoomOverlay()
    {
        _zoomOverlayTimer.Stop();
        if (ZoomOverlay is not null)
        {
            ZoomOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public void ApplyPerformanceSettings()
    {
        var enabled = App.Services.Settings.SuspendBackgroundTabs;
        foreach (var tab in Tabs)
        {
            if (tab.Core is null || ReferenceEquals(tab, _activeTab))
            {
                continue;
            }

            if (!enabled)
            {
                if (tab.IsSuspended)
                {
                    tab.Core.Resume();
                    tab.IsSuspended = false;
                }
            }
        }

        if (!enabled)
        {
            _suspendTimer.Stop();
        }
        else
        {
            _suspendTimer.Start();
        }
    }

    private void UpdateCaptionButtonTheme()
    {
        if (_appWindow is null)
        {
            return;
        }

        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var titleBar = _appWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Color.FromArgb(255, 32, 32, 32);
        titleBar.ButtonInactiveForegroundColor = dark
            ? Color.FromArgb(255, 160, 160, 160)
            : Color.FromArgb(255, 96, 96, 96);
    }

    public void InitializeBrowserSession()
    {
        if (_browserSessionInitialized) return;
        _browserSessionInitialized = true;
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                if (Environment.GetEnvironmentVariable("EXET_NO_WEBVIEW") == "1")
                {
                    return;
                }
                if (!RestoreSession())
                {
                    if (App.Services.Settings.StartupBehavior == "HomePage")
                    {
                        var home = GetHomePage();
                        CreateTab(string.IsNullOrWhiteSpace(home) ? null : home, true);
                    }
                    else
                    {
                        CreateTab(null, true);
                    }
                }
                UpdateStatusBar();
            });
    }

    private BrowserTab CreateTab(string? url, bool activate, bool isPrivate = false, bool isHomeRequest = false)
    {
        var tab = new BrowserTab
        {
            IsPrivate = isPrivate,
            Title = Localization.T(isPrivate ? "无痕标签页" : "新标签页"),
            ShowTitle = !(App.Services.Settings.VerticalTabs && App.Services.Settings.VerticalTabsIconsOnly)
        };
        if (activate)
        {
            _deferTabIndicator = true;
            HideTabIndicators();
        }

        // New tabs always append after the newest tab, never between the
        // active tab and the compact overflow controls.
        Tabs.Add(tab);
        if (activate)
        {
            SelectTab(tab);
            _ = RevealNewTabIndicatorAsync();
        }

        var target = string.IsNullOrWhiteSpace(url) ? GetHomePage() : url;
        if (!string.IsNullOrWhiteSpace(target))
        {
            var isHome = isHomeRequest || string.IsNullOrWhiteSpace(url) || IsHomeUrl(target);
            tab.IsHomePage = isHome;
            _ = NavigateAsync(tab, target, true, isHome);
        }

        return tab;
    }

    private string GetHomePage()
    {
        var home = App.Services.Settings.HomePage?.Trim() ?? string.Empty;
        return home;
    }

    private bool IsHomeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var home = GetHomePage().TrimEnd('/');
        var candidate = url.Trim().TrimEnd('/');
        return home.Length > 0 && string.Equals(candidate, home, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectTab(BrowserTab tab)
    {
        if (tab is null || !Tabs.Contains(tab))
        {
            return;
        }

        if (ReferenceEquals(_activeTab, tab))
        {
            // Settings is an overlay, not a tab: the original tab is still
            // active. Re-clicking that tab must dismiss the overlay.
            if (!tab.IsSettings && _settingsHost is not null &&
                _settingsHost.Visibility == Visibility.Visible)
            {
                _settingsHost.Visibility = Visibility.Collapsed;
                BrowserHost.Visibility = Visibility.Visible;
                SyncSelection();
                UpdateAddressBar();
            }
            else
            {
                SyncSelection();
                UpdateAddressBar();
            }
            return;
        }

        if (_activeTab is not null)
        {
            if (!ReferenceEquals(_activeTab, tab))
            {
                _ = CacheTabPreviewAsync(_activeTab);
            }

            _activeTab.IsActive = false;
        }

        _activeTab = tab;
        tab.IsActive = true;
        _addressTyping = false;
        HideAddressSuggestions();
        if (!tab.IsSettings && _settingsHost is not null &&
            _settingsHost.Visibility == Visibility.Visible)
        {
            _settingsHost.Visibility = Visibility.Collapsed;
            BrowserHost.Visibility = Visibility.Visible;
        }
        if (tab.IsSuspended && tab.Core is not null)
        {
            tab.Core.Resume();
            tab.IsSuspended = false;
        }

        foreach (var candidate in Tabs)
        {
            if (candidate.WebView is not null)
            {
                candidate.WebView.Visibility = ReferenceEquals(candidate, tab)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        UpdateAddressBar();
        UpdateNavigationState();
        UpdateFavoriteState();
        UpdateShieldState();
        UpdateStatusBar();
        SyncSelection();
        UpdateWindowTitle();

        if (!tab.IsSettings)
        {
            tab.WebView?.Focus(FocusState.Programmatic);
        }

        ApplyPerformanceSettings();
        ScheduleTabIndicatorUpdate(true);
    }

    private void SyncSelection()
    {
        _suppressTabSelection = true;
        try
        {
            HorizontalTabList.SelectedItem = _activeTab;
            VerticalTabList.SelectedItem = _activeTab;
        }
        finally
        {
            _suppressTabSelection = false;
        }
    }

    public string? GetActiveUrl() =>
        _activeTab is not null && !string.IsNullOrWhiteSpace(_activeTab.Url)
            ? _activeTab.Url
            : null;

    private void UpdateWindowTitle()
    {
        Title = _activeTab is null || string.IsNullOrWhiteSpace(_activeTab.Title)
            ? "EXET"
            : $"{_activeTab.Title} - EXET";
    }

    private void UpdateNavigationState()
    {
        BackButton.IsEnabled = _activeTab?.CanGoBack == true;
        ForwardButton.IsEnabled = _activeTab?.CanGoForward == true;
        ReloadButton.IsEnabled = _activeTab is not null;
    }

    private void UpdateAddressBar()
    {
        HideAddressSuggestions();
        if (_activeTab is null)
        {
            SetAddressBoxText(string.Empty);
            return;
        }

        if (_activeTab.IsSettings)
        {
            SetAddressBoxText("exet://settings");
            return;
        }

        SetAddressBoxText(_activeTab.IsHomePage || IsHomeUrl(_activeTab.Url)
            ? string.Empty
            : _activeTab.Url);
    }

    private void SetAddressBoxText(string text)
    {
        _suppressAddressSuggestions = true;
        try
        {
            AddressBox.Text = text;
            UpdateAddressDisplay(text);
        }
        finally
        {
            _suppressAddressSuggestions = false;
        }
    }

    private void UpdateFavoriteState()
    {
        var isFavorite = _activeTab is not null &&
                         !string.IsNullOrWhiteSpace(_activeTab.Url) &&
                         App.Services.Favorites.Contains(_activeTab.Url);
        FavoriteIcon.Glyph = isFavorite ? "\uE735" : "\uE734";
    }

    private void UpdateShieldState()
    {
        var count = _activeTab?.BlockedCount ?? 0;
        ShieldBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShieldCountText.Text = count > 99 ? "99+" : count.ToString();
        ToolTipService.SetToolTip(
            ShieldButton,
            count > 0 ? $"广告拦截 · 本页已拦截 {count} 项" : "广告拦截");
    }

    private void UpdateStatusBar()
    {
        StatusBar.Visibility = Visibility.Collapsed;
        var count = _activeTab?.BlockedCount ?? 0;
        StatusAdBlockText.Text = $"广告拦截 · 本页 {count} · 累计 {App.Services.AdBlock.TotalBlocked:N0}";
        var isPrivate = _activeTab?.IsPrivate == true;
        StatusModeText.Text = isPrivate ? "EXET · 无痕" : "EXET";
        StatusPrivateText.Text = isPrivate ? "无痕模式" : string.Empty;
    }

    public void SetTransientStatus(string message, int seconds = 4)
    {
        StatusBar.Visibility = Visibility.Visible;
        StatusAdBlockText.Text = message;
        _statusTimer.Stop();
        _statusTimer.Interval = TimeSpan.FromSeconds(seconds);
        _statusTimer.Start();
        _audioStateTimer.Start();
    }

    private void ClearTransientStatus()
    {
        _statusTimer.Stop();
        UpdateStatusBar();
    }

    private void ReloadAllTabs()
    {
        foreach (var tab in Tabs)
        {
            try
            {
                tab.Core?.Reload();
            }
            catch
            {
                // Ignore tabs that are still starting.
            }
        }
    }

    private void UpdateLoadingState(bool loading)
    {
        AddressLoading.IsActive = loading;
        AddressLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshTabAudioStates()
    {
        foreach (var tab in Tabs)
        {
            var playing = tab.Core?.IsDocumentPlayingAudio == true;
            if (tab.IsAudioPlaying != playing)
            {
                tab.IsAudioPlaying = playing;
            }

            if (tab.Core is not null && tab.IsMuted != tab.Core.IsMuted)
            {
                tab.IsMuted = tab.Core.IsMuted;
            }
        }
    }

    private void UpdateNewTabHoverResources(Color accent)
    {
        var hover = new SolidColorBrush(Color.FromArgb(31, accent.R, accent.G, accent.B));
        var pressed = new SolidColorBrush(Color.FromArgb(51, accent.R, accent.G, accent.B));
        VerticalNewTabButton.Resources["ButtonBackgroundPointerOver"] = hover;
        VerticalNewTabButton.Resources["ButtonBackgroundPressed"] = pressed;
        HorizontalNewTabButton.Resources["ButtonBackgroundPointerOver"] = hover;
        HorizontalNewTabButton.Resources["ButtonBackgroundPressed"] = pressed;
    }

    private void UpdateToolbarHoverResources(Color accent)
    {
        var hover = new SolidColorBrush(Color.FromArgb(31, accent.R, accent.G, accent.B));
        var pressed = new SolidColorBrush(Color.FromArgb(51, accent.R, accent.G, accent.B));
        ToolbarRow.Resources["ButtonBackgroundPointerOver"] = hover;
        ToolbarRow.Resources["ButtonBackgroundPressed"] = pressed;
    }

    public const string DefaultUiFontFamily = "MiSans";

    public void ApplyPrivacySettings()
    {
        foreach (var tab in Tabs)
        {
            if (tab.Core is not null)
            {
                ApplyPrivacySettingsToCore(tab.Core);
            }
        }
    }

    private static void ApplyPrivacySettingsToCore(Microsoft.Web.WebView2.Core.CoreWebView2 core)
    {
        var settings = App.Services.Settings;
        core.Settings.IsScriptEnabled = !settings.DisableJavaScript;
        core.Settings.IsGeneralAutofillEnabled = !settings.BlockAutofill;
        core.Settings.IsPasswordAutosaveEnabled = !settings.BlockAutofill;
    }

    private void RefreshTabThemes(ElementTheme theme)
    {
        HorizontalTabList.RequestedTheme = theme;
        VerticalTabList.RequestedTheme = theme;
        ToolbarRow.RequestedTheme = theme;

        foreach (var tab in Tabs)
        {
            if (HorizontalTabList.ContainerFromItem(tab) is ListViewItem horizontal)
            {
                horizontal.RequestedTheme = theme;
            }
            if (VerticalTabList.ContainerFromItem(tab) is ListViewItem vertical)
            {
                vertical.RequestedTheme = theme;
            }
        }
    }

    public void ApplyUiFont()
    {
        var family = string.IsNullOrWhiteSpace(App.Services.Settings.UiFontFamily)
            ? DefaultUiFontFamily
            : App.Services.Settings.UiFontFamily;
        ApplyFontTree(RootGrid, family, App.Services.Settings.UiFontWeight);
        ApplyWebFontToWebViews();
    }

    public static void ApplyFontTree(DependencyObject root, string family, int weight = 400)
    {
        var name = string.IsNullOrWhiteSpace(family) ? DefaultUiFontFamily : family.Trim();
        var fontWeight = new Windows.UI.Text.FontWeight((ushort)Math.Clamp(weight, 100, 900));
        ApplyFontNode(root, new FontFamily(name), fontWeight);
    }

    private static void ApplyFontNode(DependencyObject node, FontFamily family, Windows.UI.Text.FontWeight weight)
    {
        if (node is FontIcon or PathIcon or Microsoft.UI.Xaml.Controls.WebView2)
        {
            return;
        }

        if (node is TextBlock text &&
            !text.FontFamily.Source.Contains("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase) &&
            !text.FontFamily.Source.Contains("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
        {
            text.FontFamily = family;
            text.FontWeight = weight;
        }
        else if (node is RichTextBlock richText)
        {
            richText.FontFamily = family;
            richText.FontWeight = weight;
        }
        else if (node is Control control &&
            !control.FontFamily.Source.Contains("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase) &&
            !control.FontFamily.Source.Contains("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
        {
            control.FontFamily = family;
            control.FontWeight = weight;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            ApplyFontNode(VisualTreeHelper.GetChild(node, i), family, weight);
        }
    }

    private BrowserTab? FindTabById(string? id) =>
        Tabs.FirstOrDefault(tab => string.Equals(tab.Id, id, StringComparison.OrdinalIgnoreCase));

    public void ReopenSettings(string section)
    {
        var old = _settingsWindow;
        _settingsWindow = null;
        old?.Close();
        DispatcherQueue.TryEnqueue(() => OpenSettings(section));
    }

    private static void PlayBounceIn(FrameworkElement element, double durationMs = 260)
    {
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        var scale = element.RenderTransform as ScaleTransform;
        if (scale is null)
        {
            scale = new ScaleTransform { ScaleX = 0.985, ScaleY = 0.985 };
            element.RenderTransform = scale;
        }
        else
        {
            scale.ScaleX = 0.985;
            scale.ScaleY = 0.985;
        }

        var easing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 };
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var scaleX = new DoubleAnimation { From = 0.985, To = 1, Duration = duration, EasingFunction = easing };
        var scaleY = new DoubleAnimation { From = 0.985, To = 1, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(scaleX, scale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTarget(scaleY, scale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Begin();
    }

    private void OpenSettings(string section)
    {
        CloseSidebarImmediate();
        EnsureSettingsHost();
        if (_settingsHost is null)
        {
            _settingsWindow = new SettingsWindow(this, section);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Activate();
            return;
        }

        if (_inlineSettings is null)
        {
            _inlineSettings = new SettingsWindow(this, section);
            var content = _inlineSettings.Content as UIElement;
            _inlineSettings.Content = null;
            var panel = new Grid();
            if (content is not null) panel.Children.Add(content);
            var close = new Button
            {
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 12, 0),
                Content = new FontIcon
                {
                    Glyph = "\uE711",
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe Fluent Icons")
                }
            };
            ToolTipService.SetToolTip(close, "关闭设置");
            close.Click += (_, _) => CloseInlineSettings();
            panel.Children.Add(close);
            _settingsHost.Child = panel;
            _inlineSettings.Closed += (_, _) =>
            {
                if (!_closingInlineSettings) CloseInlineSettings();
            };
        }
        else
        {
            _inlineSettings.SelectSection(section);
        }

        _settingsHost.Visibility = Visibility.Visible;
        BrowserHost.Visibility = Visibility.Collapsed;
        PlayBounceIn(_settingsHost);
        SetAddressBoxText("exet://settings");
    }




    private void InstallProxyButton()
    {
        if (ToolbarRow is null) return;
        var button = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = "\uE774",
                FontSize = 15,
                FontFamily = new FontFamily("Segoe Fluent Icons")
            }
        };
        ToolTipService.SetToolTip(button, "网络代理");
        _proxyButton = button;
        button.Click += ProxyButton_Click;
        ToolbarRow.ColumnDefinitions.Insert(13, new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(button, 13);
        Grid.SetColumn(ToolbarExpandButton, 14);
        Grid.SetColumn(MenuButton, 15);
        Grid.SetColumnSpan(ZoomOverlay, 16);
        ToolbarRow.Children.Add(button);
    }

    private void InstallGhostButton()
    {
        if (ToolbarRow is null) return;
        var button = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = "\uE753",
                FontSize = 15,
                FontFamily = new FontFamily("Segoe Fluent Icons")
            }
        };
        ToolTipService.SetToolTip(button, "Ghost Downloader");
        _ghostButton = button;
        button.Click += GhostButton_Click;
        ToolbarRow.ColumnDefinitions.Insert(14, new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(button, 14);
        Grid.SetColumn(ToolbarExpandButton, 15);
        Grid.SetColumn(MenuButton, 16);
        Grid.SetColumnSpan(ZoomOverlay, 17);
        ToolbarRow.Children.Add(button);
    }

    private void SuppressNewTabTooltips()
    {
        // WinUI appends the Ctrl+T accelerator text to these tooltips; the
        // accelerator is shown at arbitrary positions after keyboard use.
        ToolTipService.SetToolTip(VerticalNewTabButton, null);
        ToolTipService.SetToolTip(VerticalNewTabHeaderButton, null);
        ToolTipService.SetToolTip(HorizontalNewTabButton, null);
    }

    private void EnableSidebarScrolling()
    {
        if (SidebarPane?.Child is not UIElement content) return;
        SidebarPane.Child = null;
        _sidebarScroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _sidebarScroll.PointerWheelChanged += (_, e) =>
        {
            var delta = e.GetCurrentPoint(_sidebarScroll).Properties.MouseWheelDelta;
            _sidebarScroll.ChangeView(null, Math.Max(0, _sidebarScroll.VerticalOffset - delta), null, true);
            e.Handled = true;
        };
        SidebarPane.Child = _sidebarScroll;
    }

    private void InstallTranslateButton()
    {
        if (ToolbarRow is null) return;
        var button = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Content = new FontIcon
            {
                Glyph = "\uE8C1",
                FontSize = 15,
                FontFamily = new FontFamily("Segoe Fluent Icons")
            }
        };
        ToolTipService.SetToolTip(button, "翻译网页");
        _translateButton = button;
        button.Click += TranslateButton_Click;
        button.RightTapped += (_, _) => ShowTranslateMenu(button);
        ToolbarRow.ColumnDefinitions.Insert(12, new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(button, 12);
        Grid.SetColumn(ToolbarExpandButton, 13);
        Grid.SetColumn(MenuButton, 14);
        Grid.SetColumnSpan(ZoomOverlay, 15);
        ToolbarRow.Children.Add(button);
    }

    private void WrapHistoryPanelInScrollViewer()
    {
        if (_historyScroll is not null) return;
        if (HistoryGroupedPanel.Parent is not Panel parent) return;
        Logger.Info($"History panel parent={parent.GetType().Name}");
        var index = parent.Children.IndexOf(HistoryGroupedPanel);
        if (index < 0) return;
        var margin = HistoryGroupedPanel.Margin;
        parent.Children.Remove(HistoryGroupedPanel);
        HistoryGroupedPanel.Margin = new Thickness(0);
        _historyScroll = new ScrollViewer
        {
            Margin = margin,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollMode = ScrollMode.Disabled,
            Visibility = HistoryGroupedPanel.Visibility,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = HistoryGroupedPanel
        };
        parent.Children.Insert(Math.Min(index, parent.Children.Count), _historyScroll);
        SidebarPane.SizeChanged += (_, _) => UpdateHistoryScrollHeight();
    }

    private void UpdateHistoryScrollHeight()
    {
        if (_historyScroll is null) return;
        var available = SidebarPane.ActualHeight - 100;
        if (available > 80)
        {
            _historyScroll.Height = available;
            Logger.Info($"History scroll height={available}");
        }
    }

    private void EnsureSettingsHost()
    {
        if (_settingsHost is not null) return;
        if (ContentShell is null) return;
        _settingsHost = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(_settingsHost, Math.Max(0, Grid.GetColumn(BrowserHost)));
        Grid.SetColumnSpan(_settingsHost, 1);
        Canvas.SetZIndex(_settingsHost, 200);
        ContentShell.Children.Add(_settingsHost);
    }

    private void CloseInlineSettings()
    {
        if (_settingsHost is null) return;
        _settingsHost.Visibility = Visibility.Collapsed;
        BrowserHost.Visibility = Visibility.Visible;
        if (_inlineSettings is not null)
        {
            _closingInlineSettings = true;
            _inlineSettings.Close();
            _inlineSettings = null;
            _closingInlineSettings = false;
        }
        var url = _activeTab?.Core?.Source ?? string.Empty;
        SetAddressBoxText(url);
    }

    private void ExitApplication()
    {
        _forceExit = true;
        Close();
    }

    private void ToggleFullScreen()
    {
        if (_appWindow is null)
        {
            return;
        }

        _isFullScreen = !_isFullScreen;
        _appWindow.SetPresenter(_isFullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
    }

    private void OpenDownloadFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetTransientStatus($"无法打开文件：{ex.Message}");
        }
    }

    private void RevealDownloadFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetTransientStatus($"无法打开文件夹：{ex.Message}");
        }
    }

    private sealed class StoredDownload
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public long ReceivedBytes { get; set; }
        public long TotalBytes { get; set; }
        public string State { get; set; } = "";
        public bool IsComplete { get; set; }
        public bool IsFailed { get; set; }
        public DateTime StartedAt { get; set; }
    }

    private static string DownloadHistoryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EXET",
            "downloads.json");

    private void LoadDownloadHistory()
    {
        try
        {
            if (!File.Exists(DownloadHistoryPath))
            {
                return;
            }
            var records = JsonSerializer.Deserialize<List<StoredDownload>>(
                File.ReadAllText(DownloadHistoryPath)) ?? new();
            foreach (var record in records.Take(200))
            {
                if (string.IsNullOrWhiteSpace(record.FilePath) ||
                    Downloads.Any(item => string.Equals(item.FilePath, record.FilePath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                Downloads.Add(new DownloadItem
                {
                    FileName = string.IsNullOrWhiteSpace(record.FileName)
                        ? Path.GetFileName(record.FilePath)
                        : record.FileName,
                    FilePath = record.FilePath,
                    SourceUrl = record.SourceUrl,
                    ReceivedBytes = record.ReceivedBytes,
                    TotalBytes = record.TotalBytes,
                    State = record.IsComplete ? "已完成" : "上次未完成",
                    IsComplete = record.IsComplete,
                    IsFailed = !record.IsComplete,
                    StartedAt = record.StartedAt == default ? DateTime.Now : record.StartedAt
                });
            }
        }
        catch
        {
        }
    }

    private void SaveDownloadHistory()
    {
        try
        {
            var directory = Path.GetDirectoryName(DownloadHistoryPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var records = Downloads.Take(200).Select(item => new StoredDownload
            {
                FileName = item.FileName,
                FilePath = item.FilePath,
                SourceUrl = item.SourceUrl,
                ReceivedBytes = item.ReceivedBytes,
                TotalBytes = item.TotalBytes,
                State = item.State,
                IsComplete = item.IsComplete,
                IsFailed = item.IsFailed,
                StartedAt = item.StartedAt
            }).ToList();
            var temp = DownloadHistoryPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(records));
            File.Move(temp, DownloadHistoryPath, true);
        }
        catch
        {
        }
    }
}



