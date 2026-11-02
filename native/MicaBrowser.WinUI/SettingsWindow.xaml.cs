using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MicaBrowser.Backdrop;
using MicaBrowser.Models;
using MicaBrowser.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace MicaBrowser;

public sealed partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private readonly AppWindow _appWindow;
    private bool _loading = true;
    private readonly List<(Button Card, string Tag)> _materialCards = new();
    private bool _syncingAcrylicColor;
    private bool _settingsNavCollapsed;
    private Storyboard? _settingsNavAnimation;

    public ObservableCollection<FavoriteEntry> Favorites => App.Services.Favorites.Items;
    public ObservableCollection<string> FavoriteFolders { get; } = new();
    public ObservableCollection<FavoriteEntry> FavoriteFolderItems { get; } = new();
    private string? _currentFavoriteFolder;
    public ObservableCollection<DownloadItem> Downloads => _owner.Downloads;
    public ObservableCollection<HistoryEntry> History => App.Services.History.Items;
    private bool _extensionsReady;
    private string _settingsHistoryQuery = "";
    private string _settingsFavoriteQuery = "";
    private ThemeMicaBackdrop? _backdrop;
    private ThemeAcrylicBackdrop? _acrylicBackdrop;
    private Storyboard? _sectionAnimation;

    public ObservableCollection<SavedLogin> Passwords => App.Services.Passwords.Items;

    public SettingsWindow(MainWindow owner, string section)
    {
        _owner = owner;
        InitializeComponent();
        SettingsNav.IsItemClickEnabled = true;
        SettingsNav.ItemClick += SettingsNav_ItemClick;
        BuildBackdropCustomizationPanel();
        BuildPrelaunchCard();
        BuildProxyCard();
        BuildClashCard();
        BuildUserAgentCard();
        BuildSectionGroupHeader();
        BuildAdditionalToolbarChecks();
        InstallSettingsSearch();
        AppearanceSection.Loaded += (_, _) => DispatcherQueue.TryEnqueue(BuildNightModeProxy);
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeWindowHelper.Attach(hwnd);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);
        ConfigureWindow();
        LoadValues();
        SelectSection(section);
        ApplyAppearance();

        App.Services.AdBlock.StatsChanged += AdBlock_StatsChanged;
        App.Services.AdBlock.Changed += AdBlock_Changed;
        App.Services.Extensions.Changed += Extensions_Changed;
        Closed += SettingsWindow_Closed;
        RootGrid.Loaded += (_, _) =>
        {
            Localization.Apply(RootGrid);
            UpdateCaptionButtons();
        };
    }

    private void ConfigureWindow()
    {
        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        const int width = 1100;
        const int height = 760;
        var x = work.X + Math.Max(0, (work.Width - width) / 2);
        var y = work.Y + Math.Max(0, (work.Height - height) / 2);
        _appWindow.MoveAndResize(new RectInt32(x, y, Math.Min(width, work.Width), Math.Min(height, work.Height)));
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    public void ApplyAppearance()
    {
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
        ApplyControlTheme(RootGrid, theme);
        ThemeAccent.Apply(RootGrid, settings.AccentColor);

        var useDarkSurface = AppSurface.IsDark(settings.NightMode, settings.ThemeMode, theme);
        var effect = settings.MicaMaterial;
        var kind = effect == "Alt" ? MicaKind.BaseAlt : MicaKind.Base;
        var micaEffect = effect is "Mica" or "Alt";
        var acrylicEffect = effect is "Acrylic" or "AcrylicThin" or "Blur" or "Smoke" or "Rainbow";
        var micaActive = micaEffect && ThemeMicaBackdrop.IsSupported;
        var acrylicActive = acrylicEffect && ThemeAcrylicBackdrop.IsSupported;
        var legacyActive = false;
        var settingsHwnd = WindowNative.GetWindowHandle(this);
        if (!micaActive && !acrylicActive && (micaEffect || acrylicEffect))
        {
            legacyActive = LegacyAcrylicHelper.Apply(
                settingsHwnd, effect, useDarkSurface, settings.MicaTransparency);
        }
        else
        {
            LegacyAcrylicHelper.Disable(settingsHwnd);
        }

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
                    Kind = kind,
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
                _backdrop.UpdateMaterial(kind);
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

        if (ThemeModeCombo is not null)
        {
            _loading = true;
            ThemeModeCombo.SelectedIndex = settings.ThemeMode switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };
            _loading = false;
        }

        MaterialEffects.Apply(
            MicaFallbackBorder,
            effect,
            useDarkSurface,
            micaActive || acrylicActive || legacyActive,
            MaterialEffects.GetOpacity(effect, settings.MicaTransparency).Overlay);

        if (effect == "Solid")
        {
            var solid = new SolidColorBrush(useDarkSurface
                ? Windows.UI.Color.FromArgb(255, 24, 26, 30)
                : Windows.UI.Color.FromArgb(255, 248, 249, 251));
            RootGrid.Background = solid;
            MicaFallbackBorder.Background = solid;
            MicaFallbackBorder.Visibility = Visibility.Visible;
        }
        else
        {
            RootGrid.Background = new SolidColorBrush(Colors.Transparent);
        }

        if (micaActive)
        {
            var alpha = (byte)Math.Round(255 * MaterialEffects.GetMicaScrimAlpha(effect, settings.MicaTransparency));
            var alt = effect == "Alt";
            if (useDarkSurface)
            {
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

        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        if (RootGrid.XamlRoot is null)
        {
            return;
        }

        var scale = RootGrid.XamlRoot.RasterizationScale;
        var inset = _appWindow.TitleBar.RightInset / Math.Max(1.0, scale);
        CaptionSpacerColumn.Width = new GridLength(Math.Max(140, inset));
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        _appWindow.TitleBar.ButtonForegroundColor = dark ? Colors.White : Color.FromArgb(255, 32, 32, 32);
        _appWindow.TitleBar.ButtonInactiveForegroundColor = dark
            ? Color.FromArgb(255, 160, 160, 160)
            : Color.FromArgb(255, 96, 96, 96);
    }

    private void LoadValues()
    {
        var settings = App.Services.Settings;
        SearchEngineCombo.SelectedIndex = settings.SearchEngine switch
        {
            "Baidu" => 1,
            "Google" => 2,
            "DuckDuckGo" => 3,
            "Sogou" => 4,
            "Yandex" => 5,
            "Custom" => 6,
            _ => 0
        };
        CustomSearchUrlBox.Text = settings.CustomSearchUrl;
        CustomSearchSettings.Visibility = settings.SearchEngine == "Custom"
            ? Visibility.Visible
            : Visibility.Collapsed;
        StartupBehaviorCombo.SelectedIndex = settings.StartupBehavior switch
        {
            "HomePage" => 1,
            _ => 0
        };
        RestoreTabsSwitch.IsOn = settings.RestoreTabs;
        VerticalTabsSwitch.IsOn = settings.VerticalTabs;
        VerticalIconsOnlySwitch.IsOn = settings.VerticalTabsIconsOnly;
        VerticalTabWidthSlider.Value = settings.VerticalTabWidth;
        VerticalTabWidthText.Text = $"{settings.VerticalTabWidth} px";
        SuspendTabsSwitch.IsOn = settings.SuspendBackgroundTabs;
        DownloadFolderBox.Text = settings.DownloadFolder;
        AskWhereToSaveSwitch.IsOn = settings.AskWhereToSave;
        DefaultFavoriteFolderBox.Text = settings.DefaultFavoriteFolder;
        RefreshFavoriteFolders();
        HomePageBox.Text = settings.HomePage;
        ThemeModeCombo.SelectedIndex = settings.ThemeMode switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        NightModeSwitch.IsOn = settings.NightMode;
        MicaMaterialCombo.SelectedIndex = settings.MicaMaterial switch
        {
            "Alt" => 1,
            "Acrylic" or "Blur" or "Smoke" or "Rainbow" => 2,
            "AcrylicThin" => 3,
            _ => 0
        };
        AcrylicColorBox.Text = settings.AcrylicTintColor;
        AcrylicColorPreview.Background = new SolidColorBrush(ThemeAccent.Parse(settings.AcrylicTintColor));
        AcrylicColorPicker.Color = ThemeAccent.Parse(settings.AcrylicTintColor);
        UpdateMaterialOptions(settings.MicaMaterial);
        TransparencySlider.Value = settings.MicaTransparency;
        TransparencyValueText.Text = $"{settings.MicaTransparency}%";
        AnimationSpeedSlider.Value = settings.AnimationDuration;
        AnimationSpeedText.Text = settings.AnimationDuration == 0
            ? "关闭"
            : $"{settings.AnimationDuration} ms";
        StartupMaskSwitch.IsOn = settings.StartupMaskEnabled;
        AccentCombo.SelectedIndex = Array.FindIndex(
            ThemeAccent.Presets,
            preset => string.Equals(preset.Hex, settings.AccentColor, StringComparison.OrdinalIgnoreCase));
        SetToolbarChecks(settings);
        RefreshToolbarOrder();
        LanguageCombo.SelectedIndex = string.Equals(
            settings.Language,
            "en-US",
            StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        PasswordAutofillSwitch.IsOn = settings.PasswordAutofill;
        DoNotTrackSwitch.IsOn = settings.DoNotTrack;
        BlockTrackersSwitch.IsOn = settings.BlockTrackers;
        ForceDarkPagesSwitch.IsOn = settings.ForceDarkPages;
        DisableJavaScriptSwitch.IsOn = settings.DisableJavaScript;
        BlockAutofillSwitch.IsOn = settings.BlockAutofill;
        ClearCookiesOnExitSwitch.IsOn = settings.ClearCookiesOnExit;
        DenySensitivePermissionsSwitch.IsOn = settings.DenySensitivePermissions;
        AdGuardSwitch.IsOn = App.Services.AdBlock.Enabled;
        AdGuardStatusText.Text = App.Services.AdBlock.IsReady
            ? "已启用 · 请求级拦截"
            : "规则加载中…";
        ExtensionsList.ItemsSource = App.Services.Extensions.Items;
        RefreshAboutInfo();
        RefreshAdGuardStats();
        UpdateExtensionStatus();
        UpdatePasswordStatus();
        UiFontCombo.SelectedIndex = -1;
        UiFontCombo.Text = string.IsNullOrWhiteSpace(settings.UiFontFamily)
            ? MainWindow.DefaultUiFontFamily
            : settings.UiFontFamily;
        UiFontWeightCombo.SelectedIndex = settings.UiFontWeight switch
        {
            300 => 0,
            500 => 2,
            600 => 3,
            700 => 4,
            _ => 1
        };
        _loading = false;
        MainWindow.ApplyFontTree(RootGrid, settings.UiFontFamily, settings.UiFontWeight);
    }

    public void SelectSection(string section)
    {
        var index = section switch
        {
            "favorites" => 1,
            "history" => 2,
            "downloads" => 3,
            "appearance" => 4,
            "adguard" => 5,
            "privacy" => 6,
            "passwords" => 7,
            "extensions" => 8,
            "about" => 9,
            _ => 0
        };

        _loading = true;
        SettingsNav.SelectedIndex = index;
        _loading = false;
        ShowSection(index);
    }

    private void SettingsNav_ItemClick(object sender, ItemClickEventArgs e)
    {
        var index = SettingsNav.Items.IndexOf(e.ClickedItem);
        if (index < 0) return;
        SettingsNav.SelectedIndex = index;
        ShowSection(index);
    }

    private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowSection(SettingsNav.SelectedIndex);
    }

    private void ShowSection(int index)
    {
        GeneralSection.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesSection.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsSection.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSection.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
        AdGuardSection.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        PrivacySection.Visibility = index == 6 ? Visibility.Visible : Visibility.Collapsed;
        PasswordsSection.Visibility = index == 7 ? Visibility.Visible : Visibility.Collapsed;
        ExtensionsSection.Visibility = index == 8 ? Visibility.Visible : Visibility.Collapsed;
        AboutSection.Visibility = index == 9 ? Visibility.Visible : Visibility.Collapsed;

        if (index == 5)
        {
            RefreshAdGuardStats();
        }
        else if (index == 7)
        {
            UpdatePasswordStatus();
        }
        else if (index == 8)
        {
            _ = RefreshExtensionsAsync();
        }
        else if (index == 9)
        {
            RefreshAboutInfo();
        }
        else if (index == 2)
        {
            UpdateHistoryEmptyState();
        RefreshSettingsHistoryGroups();
        }

        var section = index switch
        {
            1 => FavoritesSection,
            2 => HistorySection,
            3 => DownloadsSection,
            4 => AppearanceSection,
            5 => AdGuardSection,
            6 => PrivacySection,
            7 => PasswordsSection,
            8 => ExtensionsSection,
            9 => AboutSection,
            _ => GeneralSection
        };
        AnimateSection(section);
    }

    private void RefreshAboutInfo()
    {
        if (AboutVersionText is null)
        {
            return;
        }

        AboutVersionText.Text = $"版本 {AppVersion.Display}";
        var environment = App.Services.BrowserEnvironment;
        AboutRuntimeText.Text = $"网页内核：WebView2 {environment.RuntimeVersion}";
        AboutOsText.Text = $"Windows：{RuntimeInformation.OSDescription} · {RuntimeInformation.ProcessArchitecture}";
        AboutDotNetText.Text = $".NET：{RuntimeInformation.FrameworkDescription}";
        AboutDataText.Text = $"数据目录：{App.Services.Paths.Root}";
        AboutAdBlockText.Text = $"广告过滤规则：{App.Services.AdBlock.RuleCount:N0} 条";
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(App.Services.Paths.Root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _owner.SetTransientStatus($"无法打开数据目录：{ex.Message}");
        }
    }

    private void CopyEnvironmentInfo_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine,
            $"EXET {AppVersion.Display}",
            AboutRuntimeText.Text,
            AboutOsText.Text,
            AboutDotNetText.Text,
            AboutDataText.Text,
            AboutAdBlockText.Text);
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        _owner.SetTransientStatus("环境信息已复制");
    }

    private void AnimateSection(FrameworkElement section)
    {
        _sectionAnimation?.Stop();
        if (section.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            section.RenderTransform = transform;
        }

        var milliseconds = App.Services.Settings.AnimationDuration;
        if (milliseconds <= 0)
        {
            section.Opacity = 1;
            transform.Y = 0;
            return;
        }

        transform.Y = 18;
        section.Opacity = 0;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(milliseconds));

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(fade, section);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = 18,
            To = 0,
            Duration = duration,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 }
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Completed += (_, _) =>
        {
            section.Opacity = 1;
            transform.Y = 0;
            _sectionAnimation = null;
        };
        _sectionAnimation = storyboard;
        storyboard.Begin();
    }

    private void SetToolbarChecks(AppSettings settings)
    {
        ShowHomeButtonCheck.IsChecked = settings.ShowHomeButton;
        ShowFavoriteButtonCheck.IsChecked = settings.ShowFavoriteButton;
        ShowShieldButtonCheck.IsChecked = settings.ShowShieldButton;
        ShowPasswordsButtonCheck.IsChecked = settings.ShowPasswordsButton;
        ShowFavoritesButtonCheck.IsChecked = settings.ShowFavoritesButton;
        ShowExtensionsButtonCheck.IsChecked = settings.ShowExtensionsButton;
        ShowDownloadsButtonCheck.IsChecked = settings.ShowDownloadsButton;
        ShowHistoryButtonCheck.IsChecked = settings.ShowHistoryButton;
    }

    private void SaveHomePage_Click(object sender, RoutedEventArgs e)
    {
        var value = HomePageBox.Text.Trim();
        if (value.Length > 0 &&
            !value.Contains("://", StringComparison.Ordinal) &&
            !value.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        App.Services.Settings.HomePage = value;
        HomePageBox.Text = value;
        App.Services.SettingsStore.Save();
        _owner.SetTransientStatus("主页设置已更新");
    }

    private void HistorySettingsOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
        {
            _owner.OpenUrlFromSettings(url);
        }
    }

    private void RemoveHistorySettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
        {
            var entry = App.Services.History.Items.FirstOrDefault(
                item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                App.Services.History.Remove(entry);
            }
            UpdateHistoryEmptyState();
        }
    }

    private void InstallSettingsSearch()
    {
        var historySearch = new TextBox
        {
            PlaceholderText = "搜索历史记录",
            Height = 34,
            Margin = new Thickness(0, 4, 0, 0)
        };
        historySearch.TextChanged += (_, _) =>
        {
            _settingsHistoryQuery = historySearch.Text.Trim();
            RefreshSettingsHistoryGroups();
        };
        if (HistorySection is not null)
        {
            HistorySection.Children.Insert(Math.Min(1, HistorySection.Children.Count), historySearch);
        }

        var favoriteSearch = new TextBox
        {
            PlaceholderText = "搜索收藏夹",
            Height = 34,
            Margin = new Thickness(0, 4, 0, 0)
        };
        favoriteSearch.TextChanged += (_, _) =>
        {
            _settingsFavoriteQuery = favoriteSearch.Text.Trim();
            if (_settingsFavoriteQuery.Length == 0)
            {
                ShowFavoriteFolderList();
            }
            else
            {
                ApplyFavoriteSearch();
            }
        };
        if (FavoritesSection is not null)
        {
            FavoritesSection.Children.Insert(Math.Min(1, FavoritesSection.Children.Count), favoriteSearch);
        }
    }

    private void ApplyFavoriteSearch()
    {
        var query = _settingsFavoriteQuery;
        if (query.Length == 0) return;
        FavoriteFoldersList.Visibility = Visibility.Collapsed;
        FavoriteFolderItemsHeader.Visibility = Visibility.Visible;
        SettingsFavoritesList.Visibility = Visibility.Visible;
        CurrentFavoriteFolderText.Text = "搜索结果";
        FavoriteFolderItems.Clear();
        foreach (var entry in App.Services.Favorites.Items.Where(item =>
                     item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     item.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     item.Folder.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            FavoriteFolderItems.Add(entry);
        }
    }

    private void RefreshSettingsHistoryGroups()
    {
        if (SettingsHistoryGrouped is null)
        {
            return;
        }

        SettingsHistoryGrouped.Children.Clear();
        var items = App.Services.History.Items.OrderByDescending(item => item.VisitedAt).ToList();
        var query = _settingsHistoryQuery;
        if (query.Length > 0)
        {
            items = items.Where(item =>
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Host.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (items.Count == 0)
        {
            SettingsHistoryGrouped.Children.Add(new TextBlock
            {
                Text = query.Length > 0 ? "没有匹配的历史记录" : "还没有浏览记录",
                FontSize = 12,
                Opacity = 0.6,
                Margin = new Thickness(4, 12, 4, 4)
            });
            return;
        }

        string? lastLabel = null;
        foreach (var entry in items)
        {
            var today = DateTime.Today;
            var label = entry.VisitedAt.Date == today
                ? "今天"
                : entry.VisitedAt.Date == today.AddDays(-1)
                    ? "昨天"
                    : entry.VisitedAt.ToString("M月d日 dddd");
            if (label != lastLabel)
            {
                lastLabel = label;
                SettingsHistoryGrouped.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(4, 14, 4, 4)
                });
            }

            var row = new Grid { Height = 44, ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var open = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 2, 4, 2),
                Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = entry.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = $"{entry.Host}  {entry.VisitedAt:HH:mm}", FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            };
            open.Click += (_, _) =>
            {
                _owner.OpenUrlFromSettings(entry.Url);
                Close();
            };
            row.Children.Add(open);
            var remove = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 11 },
                Tag = entry.Url
            };
            remove.Click += (_, _) =>
            {
                App.Services.History.Remove(entry);
                RefreshSettingsHistoryGroups();
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            SettingsHistoryGrouped.Children.Add(row);
        }
    }

    private void UpdateHistoryEmptyState()
    {
        if (HistoryEmptyText is not null)
        {
            HistoryEmptyText.Visibility = Visibility.Collapsed;
        }
    }

    private async void ClearHistorySettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清除全部浏览历史？",
            Content = "此操作无法撤销。",
            PrimaryButtonText = "清除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Services.History.Clear();
            UpdateHistoryEmptyState();
            _owner.SetTransientStatus("浏览历史已清除");
        }
    }

    private async void ClearFavoritesSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清除全部收藏？",
            Content = "此操作无法撤销。",
            PrimaryButtonText = "清除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Services.Favorites.Clear();
            _owner.SetTransientStatus("收藏夹已清空");
        }
    }



    private void StartupMaskSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Services.Settings.StartupMaskEnabled = StartupMaskSwitch.IsOn;
        App.Services.SettingsStore.Save();
    }

    private void AnimationSpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var value = (int)Math.Round(e.NewValue / 10.0) * 10;
        App.Services.Settings.AnimationDuration = value;
        App.Services.SettingsStore.Save();
        AnimationSpeedText.Text = value == 0 ? "关闭" : $"{value} ms";
    }

    private void NightModeSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Services.Settings.NightMode = NightModeSwitch.IsOn;
        App.Services.SettingsStore.Save();
        ApplyAppearance();
        _owner.ApplyAppearance();
    }

    private void TransparencySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var value = (int)Math.Round(e.NewValue);
        App.Services.Settings.MicaTransparency = value;
        TransparencyValueText.Text = $"{value}%";
        App.Services.SettingsStore.Save();
        ApplyAppearance();
        _owner.ApplyAppearance();
    }

    private void AccentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (AccentCombo.SelectedItem is ComboBoxItem item && item.Tag is string color)
        {
            App.Services.Settings.AccentColor = color;
            App.Services.SettingsStore.Save();
            ApplyAppearance();
            _owner.ApplyAppearance();
        }
    }

    private void VerticalTabWidthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var value = (int)Math.Round(e.NewValue / 10.0) * 10;
        App.Services.Settings.VerticalTabWidth = value;
        App.Services.SettingsStore.Save();
        VerticalTabWidthText.Text = $"{value} px";
        _owner.ApplyVerticalTabs();
    }

    private void VerticalIconsOnlySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Services.Settings.VerticalTabsIconsOnly = VerticalIconsOnlySwitch.IsOn;
        App.Services.SettingsStore.Save();
        _owner.ApplyVerticalTabs(true);
    }

    private void SuspendTabsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Services.Settings.SuspendBackgroundTabs = SuspendTabsSwitch.IsOn;
        App.Services.SettingsStore.Save();
        _owner.ApplyPerformanceSettings();
    }


    private void ToolbarButtonToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox check || check.Tag is not string key)
        {
            return;
        }

        var settings = App.Services.Settings;
        var value = check.IsChecked == true;
        switch (key)
        {
            case "ShowHomeButton": settings.ShowHomeButton = value; break;
            case "ShowFavoriteButton": settings.ShowFavoriteButton = value; break;
            case "ShowShieldButton": settings.ShowShieldButton = value; break;
            case "ShowPasswordsButton": settings.ShowPasswordsButton = value; break;
            case "ShowFavoritesButton": settings.ShowFavoritesButton = value; break;
            case "ShowExtensionsButton": settings.ShowExtensionsButton = value; break;
            case "ShowDownloadsButton": settings.ShowDownloadsButton = value; break;
            case "ShowHistoryButton": settings.ShowHistoryButton = value; break;
            case "ShowGhostButton": settings.ShowGhostButton = value; break;
            case "ShowProxyButton": settings.ShowProxyButton = value; break;
            case "ShowTranslateButton": settings.ShowTranslateButton = value; break;
        }

        App.Services.SettingsStore.Save();
        _owner.ApplyToolbarSettings();
    }

    private async void ChooseDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                DownloadFolderBox.Text = folder.Path;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not choose the download folder.", ex);
        }
    }

    private void ApplyControlTheme(DependencyObject node, ElementTheme theme)
    {
        if (node is ComboBox combo)
        {
            combo.RequestedTheme = theme;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            ApplyControlTheme(VisualTreeHelper.GetChild(node, i), theme);
        }
    }

    private void RefreshFavoriteFoldersHint()
    {
        if (FavoriteFoldersHint is null)
        {
            return;
        }

        var folders = App.Services.Favorites.GetFolders(App.Services.Settings.DefaultFavoriteFolder);
        FavoriteFoldersHint.Text = "当前分类：" + string.Join("  ·  ", folders);
    }

    private void RefreshFavoriteFolders()
    {
        var folders = App.Services.Favorites.GetFolders(App.Services.Settings.DefaultFavoriteFolder);
        FavoriteFolders.Clear();
        foreach (var folder in folders)
        {
            FavoriteFolders.Add(folder);
        }

        RefreshFavoriteFoldersHint();
        if (_settingsFavoriteQuery.Length > 0)
        {
            ApplyFavoriteSearch();
        }
        else if (_currentFavoriteFolder is not null && folders.Contains(_currentFavoriteFolder))
        {
            ShowFavoriteFolder(_currentFavoriteFolder);
        }
        else
        {
            ShowFavoriteFolderList();
        }
    }

    private void ShowFavoriteFolderList()
    {
        _currentFavoriteFolder = null;
        FavoriteFoldersList.SelectedIndex = -1;
        FavoriteFoldersList.Visibility = Visibility.Visible;
        FavoriteFolderItemsHeader.Visibility = Visibility.Collapsed;
        SettingsFavoritesList.Visibility = Visibility.Collapsed;
        FavoriteFolderItems.Clear();
    }

    private void ShowFavoriteFolder(string folder)
    {
        _currentFavoriteFolder = folder;
        FavoriteFoldersList.Visibility = Visibility.Collapsed;
        FavoriteFolderItemsHeader.Visibility = Visibility.Visible;
        SettingsFavoritesList.Visibility = Visibility.Visible;
        CurrentFavoriteFolderText.Text = folder;

        FavoriteFolderItems.Clear();
        foreach (var entry in App.Services.Favorites.Items.Where(
                     item => string.Equals(item.Folder, folder, StringComparison.OrdinalIgnoreCase)))
        {
            FavoriteFolderItems.Add(entry);
        }
    }

    private void FavoriteFoldersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FavoriteFoldersList.SelectedItem is string folder)
        {
            ShowFavoriteFolder(folder);
        }
    }

    private void FavoriteFolderBack_Click(object sender, RoutedEventArgs e)
    {
        ShowFavoriteFolderList();
    }

    private void RenameFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string folder } anchor)
        {
            return;
        }

        var box = new TextBox { Text = folder, Width = 220, Margin = new Thickness(8) };
        var save = new Button { Content = "保存", Margin = new Thickness(8, 0, 8, 8) };
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(save);
        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        save.Click += (_, _) =>
        {
            if (App.Services.Favorites.RenameFolder(folder, box.Text, App.Services.Settings.DefaultFavoriteFolder))
            {
                flyout.Hide();
                RefreshFavoriteFolders();
            }
        };
        flyout.ShowAt(anchor);
        box.Focus(FocusState.Programmatic);
    }

    private async void DeleteFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string folder } element)
        {
            return;
        }

        var defaultFolder = App.Services.Settings.DefaultFavoriteFolder;
        if (string.Equals(folder, defaultFolder, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(folder, "收藏夹", StringComparison.OrdinalIgnoreCase))
        {
            var dialog = new ContentDialog
            {
                Title = "默认收藏夹不可删除",
                Content = $"“{folder}”是当前默认收藏夹，不能删除。",
                CloseButtonText = "知道了",
                XamlRoot = RootGrid.XamlRoot
            };
            await dialog.ShowAsync();
            return;
        }

        if (!App.Services.Favorites.DeleteFolder(folder, defaultFolder))
        {
            _owner.SetTransientStatus("该文件夹不能删除");
            return;
        }

        RefreshFavoriteFolders();
        _owner.SetTransientStatus($"已删除文件夹：{folder}，其中的收藏已移回默认文件夹");
    }

    private void NewFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor)
        {
            return;
        }

        var nameBox = new TextBox
        {
            PlaceholderText = "文件夹名称",
            FontSize = 13
        };
        var create = new Button
        {
            Content = "创建",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var panel = new StackPanel { Width = 260, Spacing = 10 };
        panel.Children.Add(nameBox);
        panel.Children.Add(create);
        var flyout = new Flyout
        {
            Content = panel,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        create.Click += (_, _) =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                return;
            }

            App.Services.Favorites.CreateFolder(name);
            flyout.Hide();
            RefreshFavoriteFolders();
            _owner.SetTransientStatus($"已新建文件夹：{name}");
        };
        flyout.ShowAt(anchor);
    }

    private void CreateFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = NewFavoriteFolderBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        App.Services.Favorites.CreateFolder(name);
        NewFavoriteFolderBox.Text = string.Empty;
        RefreshFavoriteFolders();
        _owner.SetTransientStatus($"已新建收藏夹分类：{name}");
    }

    private void ChangeFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } button)
        {
            return;
        }

        var entry = App.Services.Favorites.Find(url);
        if (entry is null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var folder in App.Services.Favorites.GetFolders(App.Services.Settings.DefaultFavoriteFolder))
        {
            var item = new MenuFlyoutItem { Text = folder };
            var target = folder;
            item.Click += (_, _) =>
            {
                App.Services.Favorites.SetFolder(entry, target);
                RefreshFavoriteFolders();
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(button);
    }

    private void SaveDefaultFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        App.Services.Settings.DefaultFavoriteFolder = DefaultFavoriteFolderBox.Text.Trim();
        App.Services.SettingsStore.Save();
        DefaultFavoriteFolderBox.Text = App.Services.Settings.DefaultFavoriteFolder;
        RefreshFavoriteFolders();
        _owner.SetTransientStatus("默认收藏夹已更新");
    }

    private void SaveDownloadSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.Settings;
        settings.DownloadFolder = DownloadFolderBox.Text.Trim();
        settings.AskWhereToSave = AskWhereToSaveSwitch.IsOn;
        App.Services.SettingsStore.Save();
        _owner.SetTransientStatus("下载设置已保存");
    }

    private void SettingsNavPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SettingsNavPane.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
        };
    }

    private void SettingsNavToggle_Click(object sender, RoutedEventArgs e)
    {
        _settingsNavCollapsed = !_settingsNavCollapsed;
        AnimateSettingsNav(_settingsNavCollapsed);
    }

    private void AnimateSettingsNav(bool collapse)
    {
        _settingsNavAnimation?.Stop();
        var width = collapse ? 64.0 : 224.0;
        SettingsNavPane.Visibility = Visibility.Visible;
        SettingsNavColumn.Width = new GridLength(width);
        SettingsNavPane.Width = width;
        SettingsNavPane.Opacity = 1;
        SettingsNavTransform.X = 0;
        SettingsNavRoot.Margin = collapse
            ? new Thickness(4, 18, 4, 14)
            : new Thickness(14, 18, 14, 14);
        SettingsNavHeader.HorizontalAlignment = collapse
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;
        SettingsNavHeader.Margin = collapse
            ? new Thickness(0, 0, 0, 18)
            : new Thickness(8, 0, 0, 18);
        SettingsNavHeaderText.Visibility = collapse
            ? Visibility.Collapsed
            : Visibility.Visible;
        ToolTipService.SetToolTip(
            SettingsNavToggleButton,
            collapse ? "展开设置导航" : "折叠设置导航");

        var durationMs = Math.Max(160, App.Services.Settings.AnimationDuration);
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();

        foreach (var item in SettingsNav.Items.OfType<ListViewItem>())
        {
            item.HorizontalContentAlignment = collapse
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            if (item.Content is StackPanel panel)
            {
                foreach (var label in panel.Children.OfType<TextBlock>())
                {
                    label.Visibility = collapse
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }

                if (panel.RenderTransform is not TranslateTransform transform)
                {
                    transform = new TranslateTransform();
                    panel.RenderTransform = transform;
                }

                if (durationMs <= 0)
                {
                    transform.X = collapse ? -22 : 0;
                }
                else
                {
                    var slide = new DoubleAnimation
                    {
                        To = collapse ? -22 : 0,
                        Duration = duration,
                        EasingFunction = easing
                    };
                    Storyboard.SetTarget(slide, transform);
                    Storyboard.SetTargetProperty(slide, "X");
                    storyboard.Children.Add(slide);
                }
            }
        }

        if (storyboard.Children.Count > 0)
        {
            _settingsNavAnimation = storyboard;
            storyboard.Begin();
        }
    }

    private static readonly string[] ToolbarOrderKeys =
    {
        "Favorite", "Shield", "Passwords", "Favorites", "History", "Extensions", "Downloads",
        "Translate", "Proxy", "Ghost"
    };

    private void RefreshToolbarOrder()
    {
        if (ToolbarOrderPanel is null)
        {
            return;
        }

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Favorite"] = "收藏当前页",
            ["Shield"] = "广告拦截",
            ["Passwords"] = "密码",
            ["Favorites"] = "收藏夹",
            ["History"] = "历史记录",
            ["Extensions"] = "扩展",
            ["Downloads"] = "下载",
            ["Translate"] = "翻译",
            ["Proxy"] = "代理",
            ["Ghost"] = "Ghost Downloader"
        };
        var order = App.Services.Settings.ToolbarOrder?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList() ?? new List<string>();
        foreach (var key in ToolbarOrderKeys)
        {
            if (!order.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                order.Add(key);
            }
        }

        ToolbarOrderPanel.Children.Clear();
        for (var i = 0; i < order.Count; i++)
        {
            var key = order[i];
            var row = new Grid { ColumnSpacing = 4, Height = 32 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = labels.TryGetValue(key, out var label) ? label : key,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            var up = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = "\uE70E", FontSize = 10, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets") },
                IsEnabled = i > 0
            };
            Grid.SetColumn(up, 1);
            var keyForUp = key;
            up.Click += (_, _) => MoveToolbarButton(keyForUp, -1);
            row.Children.Add(up);

            var down = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = "\uE70D", FontSize = 10, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets") },
                IsEnabled = i < order.Count - 1
            };
            Grid.SetColumn(down, 2);
            var keyForDown = key;
            down.Click += (_, _) => MoveToolbarButton(keyForDown, 1);
            row.Children.Add(down);
            ToolbarOrderPanel.Children.Add(row);
        }
    }

    private void MoveToolbarButton(string key, int delta)
    {
        var order = App.Services.Settings.ToolbarOrder?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList() ?? new List<string>();
        foreach (var known in ToolbarOrderKeys)
        {
            if (!order.Contains(known, StringComparer.OrdinalIgnoreCase))
            {
                order.Add(known);
            }
        }

        var index = order.FindIndex(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
        var target = index + delta;
        if (index < 0 || target < 0 || target >= order.Count)
        {
            return;
        }

        (order[index], order[target]) = (order[target], order[index]);
        App.Services.Settings.ToolbarOrder = string.Join(',', order);
        App.Services.SettingsStore.Save();
        _owner.ApplyToolbarSettings();
        RefreshToolbarOrder();
    }

    private static void RestartApplication()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return;

        // The running instance owns the single-instance mutex. Launch a
        // detached helper that waits for this process to exit before starting
        // the new EXET, otherwise the new process just signals the old one and
        // exits before the restart can happen.
        var info = new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c ping 127.0.0.1 -n 2 >nul & start \"\" \"{executable}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(info);
        Environment.Exit(0);
    }

    private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string language)
        {
            App.Services.Settings.Language = language;
            App.Services.SettingsStore.Save();

            var dialog = new ContentDialog
            {
                Title = "需要重启",
                Content = "界面语言已经保存，需要重新启动 EXET 才能应用。",
                PrimaryButtonText = "立即重启",
                CloseButtonText = "稍后",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            try
            {
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    RestartApplication();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not show the language restart prompt.", ex);
                _owner.SetTransientStatus("语言已保存，请手动重新启动 EXET");
            }
        }
    }

    private async void EditFavoriteSettings_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string url)
        {
            return;
        }

        var entry = App.Services.Favorites.Find(url);
        if (entry is null)
        {
            return;
        }

        var titleBox = new TextBox { Header = "标题", Text = entry.Title };
        var folderBox = new TextBox { Header = "收藏夹", Text = entry.Folder };
        var urlBox = new TextBox { Header = "网址", Text = entry.Url };
        var panel = new StackPanel { Spacing = 10, Width = 420 };
        panel.Children.Add(titleBox);
        panel.Children.Add(folderBox);
        panel.Children.Add(urlBox);

        var dialog = new ContentDialog
        {
            Title = "编辑收藏",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            entry.Title = string.IsNullOrWhiteSpace(titleBox.Text) ? entry.Url : titleBox.Text.Trim();
            entry.Folder = string.IsNullOrWhiteSpace(folderBox.Text) ? entry.Folder : folderBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(urlBox.Text))
            {
                entry.Url = urlBox.Text.Trim();
            }

            var index = App.Services.Favorites.Items.IndexOf(entry);
            if (index >= 0)
            {
                App.Services.Favorites.Items.RemoveAt(index);
                App.Services.Favorites.Items.Insert(index, entry);
            }
            App.Services.Favorites.Save();
            _owner.RefreshFavoritesPanel();
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not edit favorite.", ex);
        }
    }

    private void DeleteFavoriteSettings_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string url)
        {
            return;
        }

        var entry = App.Services.Favorites.Find(url);
        if (entry is not null)
        {
            App.Services.Favorites.Remove(entry);
            _owner.RefreshFavoritesPanel();
            RefreshFavoriteFolders();
        }
    }

    private void StartupBehaviorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (StartupBehaviorCombo.SelectedItem is ComboBoxItem item && item.Tag is string behavior)
        {
            App.Services.Settings.StartupBehavior = behavior;
            App.Services.SettingsStore.Save();
        }
    }

    private void RestoreTabsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Services.Settings.RestoreTabs = RestoreTabsSwitch.IsOn;
        App.Services.SettingsStore.Save();
    }

    private void SearchEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (SearchEngineCombo.SelectedItem is ComboBoxItem item && item.Tag is string engine)
        {
            App.Services.Settings.SearchEngine = engine;
            CustomSearchSettings.Visibility = engine == "Custom"
                ? Visibility.Visible
                : Visibility.Collapsed;
            App.Services.SettingsStore.Save();
        }
    }

    private void SaveCustomSearch_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.Settings;
        var url = CustomSearchUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            return;
        }

        if (!url.Contains("{query}", StringComparison.OrdinalIgnoreCase))
        {
            url += url.Contains('?') ? "&q={query}" : "?q={query}";
        }

        if (!Uri.TryCreate(url.Replace("{query}", "test", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out _))
        {
            CustomSearchUrlBox.Text = url;
            return;
        }

        settings.CustomSearchUrl = url;
        settings.SearchEngine = "Custom";
        CustomSearchUrlBox.Text = url;
        CustomSearchSettings.Visibility = Visibility.Visible;
        SearchEngineCombo.SelectedIndex = 6;
        App.Services.SettingsStore.Save();
        _owner.SetTransientStatus("自定义搜索引擎已保存");
    }

    private void VerticalTabsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Services.Settings.VerticalTabs = VerticalTabsSwitch.IsOn;
        App.Services.SettingsStore.Save();
        _owner.ApplyVerticalTabs(true);
    }

    private void ThemeModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (ThemeModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            App.Services.Settings.ThemeMode = theme;
            if (App.Services.Settings.NightMode)
            {
                // Picking a theme should win over the forced dark UI, otherwise
                // switching from dark to light appears to do nothing.
                App.Services.Settings.NightMode = false;
                _loading = true;
                NightModeSwitch.IsOn = false;
                _loading = false;
            }
            App.Services.SettingsStore.Save();
            ApplyAppearance();
            _owner.ApplyAppearance();
        }
    }

    private void UiFontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (UiFontCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ApplyUiFontSetting(tag);
        }
    }

    private void UiFontCombo_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var family = UiFontCombo.Text;
        if (UiFontCombo.SelectedItem is ComboBoxItem item &&
            string.Equals(family, item.Content?.ToString(), StringComparison.OrdinalIgnoreCase) &&
            item.Tag is string tag)
        {
            family = tag;
        }
        ApplyUiFontSetting(family);
    }

    private void UiFontCombo_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ApplyUiFontSetting(UiFontCombo.Text);
            e.Handled = true;
        }
    }

    private void ApplyUiFontSetting(string family)
    {
        var normalized = family?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = MainWindow.DefaultUiFontFamily;
        }

        if (App.Services.Settings.UiFontFamily != normalized)
        {
            App.Services.Settings.UiFontFamily = normalized;
            App.Services.SettingsStore.Save();
            _owner.SetTransientStatus("界面字体已更新");
        }

        _owner.ApplyUiFont();
        MainWindow.ApplyFontTree(RootGrid, normalized, App.Services.Settings.UiFontWeight);
    }

    private void UiFontWeightCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (UiFontWeightCombo.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string tag ||
            !int.TryParse(tag, out var weight))
        {
            return;
        }

        App.Services.Settings.UiFontWeight = weight;
        App.Services.SettingsStore.Save();
        _owner.ApplyUiFont();
        MainWindow.ApplyFontTree(RootGrid, App.Services.Settings.UiFontFamily, weight);
    }

    private void MicaMaterialCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (MicaMaterialCombo.SelectedItem is ComboBoxItem item && item.Tag is string material)
        {
            App.Services.Settings.MicaMaterial = material;
            UpdateMaterialOptions(material);
            if (material == "Acrylic")
            {
                AcrylicColorPicker.Color = ThemeAccent.Parse(App.Services.Settings.AcrylicTintColor);
            }
            App.Services.SettingsStore.Save();
            ApplyAppearance();
            _owner.ApplyAppearance();
        }
    }

    private void BuildMaterialCards()
    {
        MicaMaterialCombo.Visibility = Visibility.Collapsed;
        var micaSupported = ThemeMicaBackdrop.IsSupported;
        var settings = App.Services.Settings;
        if (!micaSupported && settings.MicaMaterial is "Mica" or "Alt")
        {
            settings.MicaMaterial = "Acrylic";
            App.Services.SettingsStore.Save();
        }

        var cards = new Grid { ColumnSpacing = 8, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        var rows = micaSupported ? 3 : 2;
        for (var i = 0; i < rows; i++)
        {
            cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        if (micaSupported)
        {
            AddMaterialCard(cards, 0, 0, "Mica", "Mica", "柔和云母");
            AddMaterialCard(cards, 0, 1, "Alt", "Mica Alt", "层次云母");
            row = 1;
        }
        AddMaterialCard(cards, row, 0, "Acrylic", "亚克力", "磨砂玻璃");
        AddMaterialCard(cards, row, 1, "AcrylicThin", "细亚克力", "更透亮的磨砂");
        AddMaterialCard(cards, row + 1, 0, "Solid", "纯色", "不使用透明材质");
        AddMaterialCard(cards, row + 1, 1, "Rainbow", "炫彩", "彩色渐变叠加");
        AcrylicColorPanel.Children.Add(cards);
        RefreshMaterialCards();
    }

    private void AddMaterialCard(Grid parent, int row, int column, string tag, string title, string subtitle)
    {
        var button = new Button
        {
            Tag = tag,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = subtitle, FontSize = 11, Opacity = 0.65 }
                }
            }
        };
        button.Click += (_, _) =>
        {
            App.Services.Settings.MicaMaterial = tag;
            App.Services.SettingsStore.Save();
            RefreshMaterialCards();
            ApplyAppearance();
            _owner.ApplyAppearance();
        };
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        parent.Children.Add(button);
        _materialCards.Add((button, tag));
    }

    private void RefreshMaterialCards()
    {
        var selected = App.Services.Settings.MicaMaterial;
        var accent = new SolidColorBrush(ThemeAccent.Parse(App.Services.Settings.AccentColor));
        var clear = new SolidColorBrush(Colors.Transparent);
        foreach (var (card, tag) in _materialCards)
        {
            card.BorderBrush = string.Equals(tag, selected, StringComparison.OrdinalIgnoreCase) ? accent : clear;
        }
    }

    private void BuildPrelaunchCard()
    {
        if (GeneralSection is null) return;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "预启动",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var toggle = new ToggleSwitch
        {
            OnContent = "开启",
            OffContent = "关闭",
            IsOn = App.Services.Settings.PrelaunchEnabled,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        toggle.Toggled += (_, _) =>
        {
            if (_loading) return;
            App.Services.Settings.PrelaunchEnabled = toggle.IsOn;
            App.Services.SettingsStore.Save();
            PrelaunchService.Sync(toggle.IsOn);
        };
        grid.Children.Add(toggle);
        var text = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = "开机预启动",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        text.Children.Add(new TextBlock
        {
            Text = "登录 Windows 后后台预热 EXET；之后点图标只唤醒已有进程，启动更快。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        panel.Children.Add(grid);
        var backgroundGrid = new Grid { ColumnSpacing = 18, Margin = new Thickness(0, 8, 0, 0) };
        backgroundGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        backgroundGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var backgroundText = new TextBlock
        {
            Text = "关闭窗口后保持后台运行",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        backgroundGrid.Children.Add(backgroundText);
        var backgroundToggle = new ToggleSwitch
        {
            OnContent = "开启",
            OffContent = "关闭",
            IsOn = App.Services.Settings.KeepRunningInBackground
        };
        backgroundToggle.Toggled += (_, _) =>
        {
            if (_loading) return;
            App.Services.Settings.KeepRunningInBackground = backgroundToggle.IsOn;
            App.Services.SettingsStore.Save();
        };
        Grid.SetColumn(backgroundToggle, 1);
        backgroundGrid.Children.Add(backgroundToggle);
        panel.Children.Add(backgroundGrid);

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = panel
        };
        GeneralSection.Children.Insert(Math.Min(2, GeneralSection.Children.Count), card);
    }

    private void BuildProxyCard()
    {
        if (GeneralSection is null) return;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "网络代理",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var mode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add(new ComboBoxItem { Content = "跟随系统", Tag = "System" });
        mode.Items.Add(new ComboBoxItem { Content = "直连（不使用代理）", Tag = "Direct" });
        mode.Items.Add(new ComboBoxItem { Content = "手动代理", Tag = "Manual" });
        var settings = App.Services.Settings;
        mode.SelectedIndex = settings.ProxyMode switch
        {
            "Direct" => 1,
            "Manual" => 2,
            _ => 0
        };

        var server = new TextBox
        {
            Text = settings.ProxyServer ?? string.Empty,
            PlaceholderText = "http://127.0.0.1:7897",
            IsEnabled = mode.SelectedIndex == 2
        };
        var bypass = new TextBox
        {
            Text = settings.ProxyBypass ?? string.Empty,
            PlaceholderText = "可选：localhost;127.0.0.1;*.local",
            IsEnabled = mode.SelectedIndex == 2
        };

        mode.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var tag = (mode.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
            settings.ProxyMode = tag;
            server.IsEnabled = tag == "Manual";
            bypass.IsEnabled = tag == "Manual";
            App.Services.SettingsStore.Save();
        };
        server.LostFocus += (_, _) =>
        {
            settings.ProxyServer = server.Text.Trim();
            App.Services.SettingsStore.Save();
        };
        bypass.LostFocus += (_, _) =>
        {
            settings.ProxyBypass = bypass.Text.Trim();
            App.Services.SettingsStore.Save();
        };

        panel.Children.Add(mode);
        panel.Children.Add(new TextBlock
        {
            Text = "手动代理地址",
            FontSize = 12,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });
        panel.Children.Add(server);
        panel.Children.Add(new TextBlock
        {
            Text = "不走代理的地址",
            FontSize = 12,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });
        panel.Children.Add(bypass);
        panel.Children.Add(new TextBlock
        {
            Text = "修改后重启 EXET 生效。系统代理异常时可切到“直连”。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = panel
        };
        GeneralSection.Children.Insert(Math.Min(3, GeneralSection.Children.Count), card);
    }

    private void BuildClashCard()
    {
        if (GeneralSection is null) return;
        var settings = App.Services.Settings;
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = "Clash Meta 内核",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "内置 Mihomo Meta 内核，支持 Hysteria2 / VLESS / Trojan 等节点。选择配置后可在浏览器内启动，代理模式选择“Clash Meta”即可走本机混合端口。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var enabled = new ToggleSwitch
        {
            Header = "启用并随浏览器启动",
            IsOn = settings.ClashEnabled,
            OnContent = "开启",
            OffContent = "关闭"
        };
        enabled.Toggled += (_, _) =>
        {
            settings.ClashEnabled = enabled.IsOn;
            App.Services.SettingsStore.Save();
        };
        panel.Children.Add(enabled);

        var configBox = new TextBox
        {
            Header = "配置文件（YAML）",
            Text = string.IsNullOrWhiteSpace(settings.ClashConfigPath)
                ? ClashService.ResolveConfig(settings)
                : settings.ClashConfigPath,
            PlaceholderText = "选择 Clash / mihomo 的 config.yaml"
        };
        var browse = new Button { Content = "浏览…", VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker();
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                picker.FileTypeFilter.Add(".yaml");
                picker.FileTypeFilter.Add(".yml");
                var file = await picker.PickSingleFileAsync();
                if (file is not null) configBox.Text = file.Path;
            }
            catch (Exception ex)
            {
                Logger.Warn("Clash config picker failed.", ex);
            }
        };
        var configRow = new Grid { ColumnSpacing = 8 };
        configRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        configRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        configRow.Children.Add(configBox);
        Grid.SetColumn(browse, 1);
        configRow.Children.Add(browse);
        panel.Children.Add(configRow);

        var portBox = new NumberBox
        {
            Header = "混合端口",
            Value = settings.ClashMixedPort,
            Minimum = 1024,
            Maximum = 65535,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        var apiBox = new NumberBox
        {
            Header = "控制端口",
            Value = settings.ClashApiPort,
            Minimum = 1024,
            Maximum = 65535,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        var secretBox = new PasswordBox
        {
            Header = "API 密钥（可选）",
            Password = settings.ClashSecret
        };
        var ports = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        ports.Children.Add(portBox);
        ports.Children.Add(apiBox);
        panel.Children.Add(ports);
        panel.Children.Add(secretBox);

        var status = new TextBlock
        {
            Text = App.Services.Clash.StatusText(settings),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        panel.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = new Button { Content = "保存配置" };
        var start = new Button { Content = "启动内核" };
        var stop = new Button { Content = "停止内核" };
        buttons.Children.Add(save);
        buttons.Children.Add(start);
        buttons.Children.Add(stop);
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = "内置内核：" + ClashService.BundledCore,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        });

        void Save()
        {
            settings.ClashEnabled = enabled.IsOn;
            settings.ClashConfigPath = configBox.Text.Trim();
            settings.ClashMixedPort = (int)Math.Clamp(double.IsNaN(portBox.Value) ? 7890 : portBox.Value, 1024, 65535);
            settings.ClashApiPort = (int)Math.Clamp(double.IsNaN(apiBox.Value) ? 9090 : apiBox.Value, 1024, 65535);
            settings.ClashSecret = secretBox.Password;
            App.Services.SettingsStore.Save();
            status.Text = "配置已保存";
        }
        save.Click += (_, _) => Save();
        start.Click += async (_, _) =>
        {
            Save();
            try
            {
                await App.Services.Clash.StartAsync(settings);
                status.Text = App.Services.Clash.StatusText(settings);
            }
            catch (Exception ex)
            {
                Logger.Warn("Clash start failed.", ex);
                status.Text = ex.Message;
            }
        };
        stop.Click += (_, _) =>
        {
            App.Services.Clash.Stop();
            status.Text = App.Services.Clash.StatusText(settings);
        };

        GeneralSection.Children.Insert(Math.Min(1, GeneralSection.Children.Count), new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = panel
        });
    }

    private void BuildUserAgentCard()
    {
        if (GeneralSection is null) return;
        var settings = App.Services.Settings;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "浏览器标识",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "切换后对新请求生效；已打开的页面刷新一次即可。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });

        var mode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add(new ComboBoxItem { Content = "电脑（桌面）", Tag = "Desktop" });
        mode.Items.Add(new ComboBoxItem { Content = "手机", Tag = "Mobile" });
        mode.Items.Add(new ComboBoxItem { Content = "平板", Tag = "Tablet" });
        mode.Items.Add(new ComboBoxItem { Content = "自定义", Tag = "Custom" });
        mode.SelectedIndex = settings.UserAgentMode switch
        {
            "Mobile" => 1,
            "Tablet" => 2,
            "Custom" => 3,
            _ => 0
        };

        var custom = new TextBox
        {
            Text = settings.UserAgentCustom ?? string.Empty,
            PlaceholderText = "自定义 User-Agent",
            IsEnabled = mode.SelectedIndex == 3
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var tag = (mode.SelectedItem as ComboBoxItem)?.Tag as string ?? "Desktop";
            settings.UserAgentMode = tag;
            custom.IsEnabled = tag == "Custom";
            App.Services.SettingsStore.Save();
            App.MainWindow?.ApplyUserAgent();
        };
        custom.LostFocus += (_, _) =>
        {
            settings.UserAgentCustom = custom.Text.Trim();
            App.Services.SettingsStore.Save();
            if (settings.UserAgentMode == "Custom")
            {
                App.MainWindow?.ApplyUserAgent();
            }
        };

        panel.Children.Add(mode);
        panel.Children.Add(custom);

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = panel
        };
        GeneralSection.Children.Insert(Math.Min(4, GeneralSection.Children.Count), card);
    }

    private StackPanel? _toolbarExtraRow;

    private void BuildAdditionalToolbarChecks()
    {
        if (ShowDownloadsButtonCheck is null) return;
        _toolbarExtraRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        _toolbarExtraRow.Children.Add(CreateToolbarCheck("翻译", App.Services.Settings.ShowTranslateButton,
            value => App.Services.Settings.ShowTranslateButton = value));
        _toolbarExtraRow.Children.Add(CreateToolbarCheck("代理", App.Services.Settings.ShowProxyButton,
            value => App.Services.Settings.ShowProxyButton = value));
        _toolbarExtraRow.Children.Add(CreateToolbarCheck("Ghost Downloader", App.Services.Settings.ShowGhostButton,
            value => App.Services.Settings.ShowGhostButton = value));
        ShowDownloadsButtonCheck.Loaded += (_, _) => AttachToolbarExtraRow();
        if (ShowDownloadsButtonCheck.IsLoaded) AttachToolbarExtraRow();
    }

    private void AttachToolbarExtraRow()
    {
        if (_toolbarExtraRow is null || _toolbarExtraRow.Parent is not null) return;
        Microsoft.UI.Xaml.DependencyObject? node = ShowDownloadsButtonCheck;
        while (node is not null and not Border)
        {
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }
        Logger.Info($"Toolbar extras loaded: node={node?.GetType().Name ?? "null"}");
        if (node is Border { Child: Panel cardContent })
        {
            var insertAt = cardContent.Children.Count;
            for (var i = 0; i < cardContent.Children.Count; i++)
            {
                if (cardContent.Children[i] is TextBlock label && label.Text == "按钮顺序")
                {
                    insertAt = i;
                    break;
                }
            }
            cardContent.Children.Insert(insertAt, _toolbarExtraRow);
            Logger.Info($"Toolbar extras: inserted at {insertAt}, count={cardContent.Children.Count}");
            return;
        }
        if (ShowDownloadsButtonCheck.Parent is Panel row)
        {
            row.Children.Add(_toolbarExtraRow);
            Logger.Info($"Toolbar extras: added to row, count={row.Children.Count}");
        }
    }

    private CheckBox CreateToolbarCheck(string label, bool initial, Action<bool> apply)
    {
        var check = new CheckBox { Content = label, IsChecked = initial };
        check.Click += (_, _) =>
        {
            if (_loading) return;
            apply(check.IsChecked == true);
            App.Services.SettingsStore.Save();
            _owner.ApplyToolbarSettings();
        };
        return check;
    }

    private bool _nightProxyBuilt;

    private void BuildNightModeProxy()
    {
        if (_nightProxyBuilt || ForceDarkPagesSwitch is null || AppearanceSection is null) return;
        _nightProxyBuilt = true;

        Microsoft.UI.Xaml.DependencyObject? node = ForceDarkPagesSwitch;
        while (node is not null and not Grid)
        {
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }
        if (node is Grid originalRow)
        {
            originalRow.Visibility = Visibility.Collapsed;
        }

        var toggle = new ToggleSwitch
        {
            IsOn = ForceDarkPagesSwitch.IsOn,
            OnContent = "增强",
            OffContent = "跟随网站"
        };
        toggle.Toggled += (_, _) => ForceDarkPagesSwitch.IsOn = toggle.IsOn;

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "增强网页夜间模式",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "对未适配深色模式的网页强制使用深色样式，部分图片可能被反色。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        panel.Children.Add(toggle);

        AppearanceSection.Children.Insert(Math.Min(1, AppearanceSection.Children.Count), new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = panel
        });
    }

    private void BuildSectionGroupHeader()
    {
        if (GeneralSection is null) return;
        GeneralSection.Children.Insert(Math.Min(2, GeneralSection.Children.Count), new TextBlock
        {
            Text = "启动、网络与浏览器标识",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(2, 8, 0, 0)
        });
    }

    private void BuildTranslationCard()
    {
        if (GeneralSection is null) return;
        var settings = App.Services.Settings;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "网页翻译",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "工具栏翻译按钮使用这里的引擎；目标语言在工具栏 A 菜单选择。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"]
        });

        var provider = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        provider.Items.Add(new ComboBoxItem { Content = "DeepSeek API", Tag = "DeepSeek" });
        provider.Items.Add(new ComboBoxItem { Content = "Bing 翻译", Tag = "Bing" });
        provider.SelectedIndex = settings.TranslationProvider == "Bing" ? 1 : 0;

        var key = new PasswordBox
        {
            PlaceholderText = "DeepSeek API Key（仅 DeepSeek 需要）",
            Password = settings.DeepSeekApiKey ?? string.Empty
        };

        provider.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            settings.TranslationProvider = (provider.SelectedItem as ComboBoxItem)?.Tag as string ?? "DeepSeek";
            App.Services.SettingsStore.Save();
        };
        key.LostFocus += (_, _) =>
        {
            settings.DeepSeekApiKey = key.Password.Trim();
            App.Services.SettingsStore.Save();
        };

        panel.Children.Add(provider);
        panel.Children.Add(key);
        GeneralSection.Children.Insert(Math.Min(6, GeneralSection.Children.Count), new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = panel
        });
    }

    private void BuildBackdropCustomizationPanel()
    {
        if (AcrylicColorPanel is null) return;
        AcrylicColorPanel.Children.Clear();
        BuildMaterialCards();
        AcrylicColorPanel.Visibility = Visibility.Visible;
        var settings = App.Services.Settings;

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var toggle = new ToggleSwitch
        {
            Header = "自定义色调",
            IsOn = settings.BackdropUseCustomTint,
            OnContent = "开",
            OffContent = "关"
        };
        var hint = new TextBlock
        {
            Text = "关闭时使用系统默认材质；开启后使用下面的色调。",
            FontSize = 11,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = 380
        };
        header.Children.Add(toggle);
        header.Children.Add(hint);
        AcrylicColorPanel.Children.Add(header);

        var details = new StackPanel { Spacing = 8 };
        details.Visibility = settings.BackdropUseCustomTint ? Visibility.Visible : Visibility.Collapsed;
        AcrylicColorPanel.Children.Add(details);

        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var hex in new[] { "#202020", "#000000", "#FFFFFF", "#0078D4", "#7B2FF7", "#107C10" })
        {
            var color = ThemeAccent.Parse(hex);
            var swatch = new Button
            {
                Width = 26, Height = 26, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                Background = new SolidColorBrush(color)
            };
            swatch.Click += (_, _) =>
            {
                settings.BackdropTintColor = hex;
                settings.BackdropUseCustomTint = true;
                toggle.IsOn = true;
                App.Services.SettingsStore.Save();
                ApplyAppearance();
                _owner.ApplyAppearance();
            };
            colors.Children.Add(swatch);
        }
        details.Children.Add(colors);

        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            Color = ThemeAccent.Parse(settings.BackdropTintColor),
            IsColorChannelTextInputVisible = true
        };
        var pickerButton = new Button { Content = "调色盘…", Padding = new Thickness(12, 5, 12, 5) };
        pickerButton.Flyout = new Flyout { Content = picker };
        picker.ColorChanged += (_, args) =>
        {
            settings.BackdropTintColor = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
            settings.BackdropUseCustomTint = true;
            toggle.IsOn = true;
            App.Services.SettingsStore.Save();
            ApplyAppearance();
            _owner.ApplyAppearance();
        };
        details.Children.Add(pickerButton);

        var tintLabel = new TextBlock { Text = "色调不透明度", FontSize = 12 };
        var tintSlider = new Slider { Minimum = 0, Maximum = 100, Value = settings.BackdropTintOpacity * 100, StepFrequency = 1 };
        details.Children.Add(tintLabel);
        details.Children.Add(tintSlider);
        var tintValue = new TextBlock { Text = $"{Math.Round(settings.BackdropTintOpacity * 100)}%", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        details.Children.Add(tintValue);
        var lumLabel = new TextBlock { Text = "明度不透明度", FontSize = 12 };
        var lumSlider = new Slider { Minimum = 0, Maximum = 100, Value = settings.BackdropLuminosityOpacity * 100, StepFrequency = 1 };
        details.Children.Add(lumLabel);
        details.Children.Add(lumSlider);
        var lumValue = new TextBlock { Text = $"{Math.Round(settings.BackdropLuminosityOpacity * 100)}%", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        details.Children.Add(lumValue);

        toggle.Toggled += (_, _) =>
        {
            if (_loading) return;
            settings.BackdropUseCustomTint = toggle.IsOn;
            details.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            App.Services.SettingsStore.Save();
            ApplyAppearance();
            _owner.ApplyAppearance();
        };
        tintSlider.ValueChanged += (_, e) =>
        {
            if (_loading) return;
            settings.BackdropTintOpacity = e.NewValue / 100.0;
            tintValue.Text = $"{Math.Round(e.NewValue)}%";
            App.Services.SettingsStore.Save();
            ApplyAppearance();
            _owner.ApplyAppearance();
        };
        lumSlider.ValueChanged += (_, e) =>
        {
            if (_loading) return;
            settings.BackdropLuminosityOpacity = e.NewValue / 100.0;
            lumValue.Text = $"{Math.Round(e.NewValue)}%";
            App.Services.SettingsStore.Save();
            ApplyAppearance();
            _owner.ApplyAppearance();
        };
    }

    private void UpdateMaterialOptions(string material)
    {
        AcrylicColorPanel.Visibility = Visibility.Visible;
        TransparencyRow.Visibility = Visibility.Collapsed;
        ThemeModeRow.Visibility = Visibility.Visible;
        ThemeModeCombo.IsEnabled = true;
        if (TransparencyTitle is not null && TransparencyHint is not null)
        {
            var rainbow = material == "Rainbow";
            TransparencyTitle.Text = rainbow ? "\u70ab\u5f69\u5ea6" : "\u80cc\u666f\u900f\u660e\u5ea6";
            TransparencyHint.Text = rainbow
                ? "\u6570\u503c\u8d8a\u4f4e\uff0c\u5f69\u8272\u6e10\u53d8\u8d8a\u6d53\u70c8\u3002"
                : "\u6570\u503c\u8d8a\u5927\uff0c\u80cc\u666f\u8d8a\u900f\u660e\u3002";
        }
    }

    private void AcrylicColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _syncingAcrylicColor)
        {
            return;
        }

        ApplyAcrylicColor(AcrylicColorBox.Text, updateText: false);
    }

    private void AcrylicSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string color)
        {
            ApplyAcrylicColor(color, updateText: true);
        }
    }

    private void AcrylicColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading || _syncingAcrylicColor)
        {
            return;
        }

        ApplyAcrylicColor(ThemeAccent.ToHex(args.NewColor), updateText: true);
    }

    private void ApplyAcrylicColor(string text, bool updateText)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length is not (7 or 9) || !value.StartsWith('#'))
        {
            return;
        }

        if (!uint.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            return;
        }

        var color = ThemeAccent.Parse(value);
        App.Services.Settings.AcrylicTintColor = value;
        _syncingAcrylicColor = true;
        try
        {
            if (updateText)
            {
                AcrylicColorBox.Text = value;
            }

            AcrylicColorPreview.Background = new SolidColorBrush(color);
            AcrylicColorPicker.Color = color;
        }
        finally
        {
            _syncingAcrylicColor = false;
        }

        App.Services.SettingsStore.Save();
        if (App.Services.Settings.MicaMaterial == "Acrylic")
        {
            ApplyAppearance();
            _owner.ApplyAppearance();
        }
    }

    private void MicaPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            ApplyMicaPreview(tag);
        }
    }

    private void MicaPreview_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string tag)
        {
            ApplyMicaPreview(tag);
        }
    }

    private void ApplyMicaPreview(string tag)
    {
        var parts = tag.Split('|');
        if (parts.Length != 2)
        {
            return;
        }

        _loading = true;
        App.Services.Settings.ThemeMode = parts[0];
        App.Services.Settings.MicaMaterial = parts[1];
        ThemeModeCombo.SelectedIndex = parts[0] == "Light" ? 1 : 2;
        MicaMaterialCombo.SelectedIndex = parts[1] switch
        {
            "Alt" => 1,
            "Acrylic" or "Blur" or "Smoke" or "Rainbow" => 2,
            "AcrylicThin" => 3,
            _ => 0
        };
        _loading = false;
        App.Services.SettingsStore.Save();
        ApplyAppearance();
        _owner.ApplyAppearance();
    }

    private void AdGuardSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Services.AdBlock.Enabled = AdGuardSwitch.IsOn;
        App.Services.AdBlock.NotifySettingsChanged();
        AdGuardStatusText.Text = AdGuardSwitch.IsOn ? "已启用 · 请求级拦截" : "已暂停";
        _owner.RefreshStatusBar();
    }

    private async void UpdateFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateFiltersButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(message => AdGuardRuleSummaryText.Text = message);
            await App.Services.AdBlock.UpdateFiltersAsync(progress);
            RefreshAdGuardStats();
            _owner.RefreshStatusBar();
        }
        finally
        {
            UpdateFiltersButton.IsEnabled = true;
        }
    }

    private void RefreshAdGuardStats()
    {
        var adblock = App.Services.AdBlock;
        AdGuardTotalText.Text = adblock.TotalBlocked.ToString("N0");
        AdGuardDomainText.Text = adblock.DomainRules.ToString("N0");
        AdGuardPathText.Text = adblock.PathRules.ToString("N0");
        AdGuardRulesText.Text = $"{adblock.RuleCount:N0} 条过滤规则";
        AdGuardRuleSummaryText.Text = adblock.LastUpdate == "内置规则"
            ? "正在使用内置规则"
            : $"更新于 {adblock.LastUpdate}";
        AdGuardStatusText.Text = adblock.Enabled ? "已启用 · 请求级拦截" : "已暂停";
        var allowlist = App.Services.Settings.AdBlockAllowlist;
        AllowlistText.Text = allowlist.Count == 0
            ? "当前没有暂停过滤的网站。"
            : string.Join("、", allowlist);
    }

    private async Task RefreshExtensionsAsync()
    {
        _extensionsReady = false;
        await App.Services.Extensions.RefreshAsync();
        ExtensionsList.ItemsSource = App.Services.Extensions.Items;
        await Task.Delay(120);
        _extensionsReady = true;
        UpdateExtensionStatus();
    }

    private void UpdateExtensionStatus()
    {
        var count = App.Services.Extensions.Items.Count;
        ExtensionsStatusText.Text = count == 0
            ? "当前没有安装扩展。可以加载本地解压扩展或安装内置示例。"
            : $"已安装 {count} 个扩展。启用或停用后请刷新网页。";
    }

    private void Extensions_Changed(object? sender, EventArgs e)
    {
        // Enumerating extensions happens whenever the Extensions tab is
        // opened. It must not reload the browser tabs; only actual mutations
        // (install/remove/toggle) should do that.
        DispatcherQueue.TryEnqueue(UpdateExtensionStatus);
    }

    private void AdBlock_StatsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RefreshAdGuardStats);
    }

    private void AdBlock_Changed(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RefreshAdGuardStats);
    }

    private async void RefreshExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshExtensionsAsync();
    }

    private async void InstallSampleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ExtensionsStatusText.Text = "正在安装内置示例扩展…";
            await App.Services.Extensions.InstallSampleAsync();
            await RefreshExtensionsAsync();
            ExtensionsStatusText.Text = "已安装示例扩展。请刷新当前网页后生效。";
        }
        catch (Exception ex)
        {
            ExtensionsStatusText.Text = "安装失败：" + ex.Message;
        }
    }

    private async void LoadExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            ExtensionsStatusText.Text = "正在加载扩展…";
            await App.Services.Extensions.AddFromFolderAsync(folder.Path);
            await RefreshExtensionsAsync();
            ExtensionsStatusText.Text = "扩展已加载。请刷新当前网页后生效。";
        }
        catch (Exception ex)
        {
            ExtensionsStatusText.Text = "加载扩展失败：" + ex.Message;
        }
    }

    private void OpenExtensionsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(App.Services.Paths.Extensions) { UseShellExecute = true });
        }
        catch
        {
            // Non-critical.
        }
    }

    private async void ExtensionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_extensionsReady || sender is not ToggleSwitch toggle || toggle.Tag is not string id)
        {
            return;
        }

        var info = App.Services.Extensions.Items.FirstOrDefault(
            item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (info is null)
        {
            return;
        }

        try
        {
            await App.Services.Extensions.SetEnabledAsync(info, toggle.IsOn);
            _owner.NotifyExtensionsChanged();
        }
        catch (Exception ex)
        {
            ExtensionsStatusText.Text = "切换扩展失败：" + ex.Message;
            toggle.IsOn = info.IsEnabled;
        }
    }

    private async void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
        {
            return;
        }

        var info = App.Services.Extensions.Items.FirstOrDefault(
            item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (info is null)
        {
            return;
        }

        try
        {
            await App.Services.Extensions.RemoveAsync(info);
            await RefreshExtensionsAsync();
            ExtensionsStatusText.Text = "扩展已移除。";
        }
        catch (Exception ex)
        {
            ExtensionsStatusText.Text = "移除失败：" + ex.Message;
        }
    }

    private void PrivacyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var settings = App.Services.Settings;
        settings.DoNotTrack = DoNotTrackSwitch.IsOn;
        settings.BlockTrackers = BlockTrackersSwitch.IsOn;
        settings.ForceDarkPages = ForceDarkPagesSwitch.IsOn;
        settings.DisableJavaScript = DisableJavaScriptSwitch.IsOn;
        settings.BlockAutofill = BlockAutofillSwitch.IsOn;
        settings.ClearCookiesOnExit = ClearCookiesOnExitSwitch.IsOn;
        settings.DenySensitivePermissions = DenySensitivePermissionsSwitch.IsOn;
        App.Services.SettingsStore.Save();
        _owner.ApplyNightModeToWebViews();
        _owner.ApplyPrivacySettings();
    }

    private void ClearPermissionRules_Click(object sender, RoutedEventArgs e)
    {
        App.Services.Settings.PermissionRules.Clear();
        App.Services.SettingsStore.Save();
        _owner.SetTransientStatus("网站权限决定已清除");
    }

    private void PasswordAutofillSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        App.Services.Settings.PasswordAutofill = PasswordAutofillSwitch.IsOn;
        App.Services.SettingsStore.Save();
    }

    private void UpdatePasswordStatus()
    {
        if (PasswordsStatusText is null)
        {
            return;
        }

        PasswordsStatusText.Text = Passwords.Count == 0
            ? "还没有保存密码。点击“添加密码”或“生成密码”创建第一条登录信息。"
            : $"已保存 {Passwords.Count} 条登录信息。复制密码会写入系统剪贴板。";
    }

    private async void AddPassword_Click(object sender, RoutedEventArgs e)
    {
        await ShowPasswordEditorAsync(null, false);
    }

    private async void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        await ShowPasswordEditorAsync(null, true);
    }

    private void RefreshPasswords_Click(object sender, RoutedEventArgs e)
    {
        UpdatePasswordStatus();
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
        {
            return;
        }

        var entry = App.Services.Passwords.FindById(id);
        if (entry is null)
        {
            return;
        }

        var password = App.Services.Passwords.GetPassword(entry);
        if (string.IsNullOrEmpty(password))
        {
            PasswordsStatusText.Text = "无法解密这条密码，可能来自其他 Windows 用户。";
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(password);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            PasswordsStatusText.Text = $"已复制 {entry.Site} 的密码。使用后请清除剪贴板。";
        }
        catch (Exception ex)
        {
            PasswordsStatusText.Text = "复制失败：" + ex.Message;
        }
    }

    private async void EditPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
        {
            return;
        }

        var entry = App.Services.Passwords.FindById(id);
        if (entry is not null)
        {
            await ShowPasswordEditorAsync(entry, false);
        }
    }

    private async void DeletePassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
        {
            return;
        }

        var entry = App.Services.Passwords.FindById(id);
        if (entry is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "删除密码",
            Content = $"确定删除 {entry.Site}（{entry.Username}）的登录信息吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Services.Passwords.Remove(entry);
            UpdatePasswordStatus();
        }
    }

    private async Task ShowPasswordEditorAsync(SavedLogin? entry, bool generate)
    {
        var urlBox = new TextBox
        {
            Header = "网站地址",
            Text = entry?.Url ?? _owner.GetActiveUrl() ?? string.Empty,
            PlaceholderText = "https://example.com",
            FontSize = 14
        };
        var userBox = new TextBox
        {
            Header = "用户名",
            Text = entry?.Username ?? string.Empty,
            FontSize = 14
        };
        var passwordBox = new PasswordBox
        {
            Header = "密码",
            PasswordRevealMode = PasswordRevealMode.Peek,
            FontSize = 14
        };

        if (entry is not null)
        {
            passwordBox.Password = App.Services.Passwords.GetPassword(entry) ?? string.Empty;
        }
        else if (generate)
        {
            passwordBox.Password = PasswordStore.GeneratePassword();
        }
        else
        {
            passwordBox.Password = string.Empty;
        }

        var generateButton = new Button
        {
            Content = "生成强密码",
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12
        };
        generateButton.Click += (_, _) => passwordBox.Password = PasswordStore.GeneratePassword();

        var errorText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.OrangeRed),
            Visibility = Visibility.Collapsed
        };

        var panel = new StackPanel
        {
            Width = 460,
            Spacing = 10
        };
        panel.Children.Add(urlBox);
        panel.Children.Add(userBox);
        panel.Children.Add(passwordBox);
        panel.Children.Add(generateButton);
        panel.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            Title = entry is null ? "添加密码" : "编辑密码",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(urlBox.Text))
                {
                    throw new UriFormatException("请输入网站地址。");
                }

                if (string.IsNullOrWhiteSpace(userBox.Text))
                {
                    throw new InvalidOperationException("请输入用户名。");
                }

                if (string.IsNullOrEmpty(passwordBox.Password))
                {
                    throw new InvalidOperationException("请输入密码。");
                }

                App.Services.Passwords.SaveOrUpdate(urlBox.Text, userBox.Text.Trim(), passwordBox.Password, entry);
                PasswordsStatusText.Text = entry is null ? "密码已保存。" : "密码已更新。";
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                errorText.Text = ex.Message;
                errorText.Visibility = Visibility.Visible;
            }
        };

        await dialog.ShowAsync();
        UpdatePasswordStatus();
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        App.Services.AdBlock.StatsChanged -= AdBlock_StatsChanged;
        App.Services.AdBlock.Changed -= AdBlock_Changed;
        App.Services.Extensions.Changed -= Extensions_Changed;
    }

    private async void SetDefaultBrowser_Click(object sender, RoutedEventArgs e)
    {
        await DefaultBrowserService.OpenDefaultAppsAsync();
        _owner.SetTransientStatus("已打开 Windows 默认应用设置，请选择 EXET 作为默认浏览器。", 8);
    }
}



