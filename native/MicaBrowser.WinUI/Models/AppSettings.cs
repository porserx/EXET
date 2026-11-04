using MicaBrowser.Helpers;

namespace MicaBrowser.Models;

public sealed class AppSettings : ObservableObject
{
    private string _searchEngine = "Bing";
    private string _customSearchName = "自定义";
    private string _customSearchUrl = "https://www.google.com/search?q={query}";
    private bool _adBlockEnabled = true;
    private string _downloadFolder = string.Empty;
    private bool _askWhereToSave;
    private bool _passwordAutofill = true;
    private bool _doNotTrack = true;
    private bool _blockTrackers = true;
    private bool _forceDarkPages = true;
    private bool _disableJavaScript;
    private bool _blockAutofill;
    private bool _clearCookiesOnExit;
    private bool _denySensitivePermissions;
    private bool _showStatusBar = true;
    private bool _restoreTabs = false;
    private string _startupBehavior = "NewTab";
    private bool _verticalTabs = false;
    private string _themeMode = "System";
    private string _language = "zh-CN";
    private string _toolbarOrder = "Favorite,Shield,Passwords,Favorites,Extensions,Downloads";
    private string _favoritesDisplayMode = "Sidebar";
    private string _downloadsDisplayMode = "Sidebar";
    private string _micaMaterial = "Acrylic";
    private string _acrylicTintColor = "#1F70B8";
    private string _defaultFavoriteFolder = "收藏夹";
    private string _homePage = "https://xingyuefox.github.io/Litestart/";
    private int _micaTransparency = 80;
    private int _animationDuration = 300;
    private bool _startupMaskEnabled = true;
    private bool _prelaunchEnabled = true;
    private bool _keepRunningInBackground = true;
    private bool _showTranslateButton = true;
    private bool _showProxyButton = true;
    private bool _showGhostButton = true;
    private bool _clashEnabled;
    private string _clashCorePath = "";
    private string _clashConfigPath = "";
    private int _clashMixedPort = 7890;
    private int _clashApiPort = 9090;
    private string _clashSecret = "";
    private string _pinnedExtensions = "";
    private string _proxyMode = "System";
    private string _proxyServer = "http://127.0.0.1:7897";
    private string _proxyBypass = "";
    private string _userAgentMode = "Default";
    private string _userAgentCustom = "";
    private string _translationProvider = "DeepSeek";
    private string _deepSeekApiKey = "";
    private string _libreTranslateUrl = "http://127.0.0.1:5000";
    private string _libreTranslateApiKey = "";
    private string _translationTarget = "zh-CN";
    private string _translationSourceLanguage = "auto";
    private bool _smartTranslateEnabled = true;
    private bool _ghostDownloaderEnabled;
    private string _ghostDownloaderPath = "ghd";
    private int _browserZoom = 100;
    private string _accentColor = "#1F70B8";
    private string _uiFontFamily = "Microsoft YaHei UI";
    private int _uiFontWeight = 400;
    private bool _nightMode;
    private bool _verticalTabsIconsOnly;
    private int _verticalTabWidth = 260;
    private bool _suspendBackgroundTabs = true;
    private bool _showHomeButton = true;
    private bool _showFavoriteButton = true;
    private bool _showShieldButton = true;
    private bool _showPasswordsButton = true;
    private bool _showFavoritesButton = true;
    private bool _showExtensionsButton = true;
    private bool _showDownloadsButton = true;
    private bool _showHistoryButton = true;
    public bool ShowHistoryButton
    {
        get => _showHistoryButton;
        set => SetProperty(ref _showHistoryButton, value);
    }

    private long _adBlockTotalBlocked;

    public string SearchEngine
    {
        get => _searchEngine;
        set => SetProperty(ref _searchEngine, value);
    }

    public string CustomSearchName
    {
        get => _customSearchName;
        set => SetProperty(ref _customSearchName, value);
    }

    /// <summary>Custom search URL. Use {query} for the escaped search term.</summary>
    public string CustomSearchUrl
    {
        get => _customSearchUrl;
        set => SetProperty(ref _customSearchUrl, value);
    }

    public bool AdBlockEnabled
    {
        get => _adBlockEnabled;
        set => SetProperty(ref _adBlockEnabled, value);
    }

    /// <summary>Empty means the Windows Downloads folder.</summary>
    public string DownloadFolder
    {
        get => _downloadFolder;
        set => SetProperty(ref _downloadFolder, value);
    }

    public bool AskWhereToSave
    {
        get => _askWhereToSave;
        set => SetProperty(ref _askWhereToSave, value);
    }

    public bool PasswordAutofill
    {
        get => _passwordAutofill;
        set => SetProperty(ref _passwordAutofill, value);
    }

    public bool DoNotTrack
    {
        get => _doNotTrack;
        set => SetProperty(ref _doNotTrack, value);
    }

    public bool BlockTrackers
    {
        get => _blockTrackers;
        set => SetProperty(ref _blockTrackers, value);
    }

    public bool ForceDarkPages
    {
        get => _forceDarkPages;
        set => SetProperty(ref _forceDarkPages, value);
    }

    public bool DisableJavaScript
    {
        get => _disableJavaScript;
        set => SetProperty(ref _disableJavaScript, value);
    }

    public bool BlockAutofill
    {
        get => _blockAutofill;
        set => SetProperty(ref _blockAutofill, value);
    }

    public bool ClearCookiesOnExit
    {
        get => _clearCookiesOnExit;
        set => SetProperty(ref _clearCookiesOnExit, value);
    }

    public bool DenySensitivePermissions
    {
        get => _denySensitivePermissions;
        set => SetProperty(ref _denySensitivePermissions, value);
    }

    /// <summary>Permission decisions keyed by kind|host, value allow or deny.</summary>
    public Dictionary<string, string> PermissionRules { get; set; } = new();

    public bool ShowStatusBar
    {
        get => _showStatusBar;
        set => SetProperty(ref _showStatusBar, value);
    }

    public bool RestoreTabs
    {
        get => _restoreTabs;
        set => SetProperty(ref _restoreTabs, value);
    }

    /// <summary>NewTab, Continue or HomePage.</summary>
    public bool GhostDownloaderEnabled
    {
        get => _ghostDownloaderEnabled;
        set => SetProperty(ref _ghostDownloaderEnabled, value);
    }

    public string GhostDownloaderPath
    {
        get => _ghostDownloaderPath;
        set => SetProperty(ref _ghostDownloaderPath, value);
    }

    public string TranslationProvider
    {
        get => _translationProvider;
        set => SetProperty(ref _translationProvider, value);
    }

    public string LibreTranslateUrl
    {
        get => _libreTranslateUrl;
        set => SetProperty(ref _libreTranslateUrl, value);
    }

    public string LibreTranslateApiKey
    {
        get => _libreTranslateApiKey;
        set => SetProperty(ref _libreTranslateApiKey, value);
    }

    public string DeepSeekApiKey
    {
        get => _deepSeekApiKey;
        set => SetProperty(ref _deepSeekApiKey, value);
    }

    public bool SmartTranslateEnabled
    {
        get => _smartTranslateEnabled;
        set => SetProperty(ref _smartTranslateEnabled, value);
    }

    public string TranslationSourceLanguage
    {
        get => _translationSourceLanguage;
        set => SetProperty(ref _translationSourceLanguage, value);
    }

    public string TranslationTarget
    {
        get => _translationTarget;
        set => SetProperty(ref _translationTarget, value);
    }

    public string UserAgentMode
    {
        get => _userAgentMode;
        set => SetProperty(ref _userAgentMode, value);
    }

    public string UserAgentCustom
    {
        get => _userAgentCustom;
        set => SetProperty(ref _userAgentCustom, value);
    }

    public bool ClashEnabled
    {
        get => _clashEnabled;
        set => SetProperty(ref _clashEnabled, value);
    }

    public string ClashCorePath
    {
        get => _clashCorePath;
        set => SetProperty(ref _clashCorePath, value);
    }

    public string ClashConfigPath
    {
        get => _clashConfigPath;
        set => SetProperty(ref _clashConfigPath, value);
    }

    public int ClashMixedPort
    {
        get => _clashMixedPort;
        set => SetProperty(ref _clashMixedPort, value);
    }

    public int ClashApiPort
    {
        get => _clashApiPort;
        set => SetProperty(ref _clashApiPort, value);
    }

    public string PinnedExtensions
    {
        get => _pinnedExtensions;
        set => SetProperty(ref _pinnedExtensions, value);
    }

    public string ClashSecret
    {
        get => _clashSecret;
        set => SetProperty(ref _clashSecret, value);
    }

    public string ProxyMode
    {
        get => _proxyMode;
        set => SetProperty(ref _proxyMode, value);
    }

    public string ProxyServer
    {
        get => _proxyServer;
        set => SetProperty(ref _proxyServer, value);
    }

    public string ProxyBypass
    {
        get => _proxyBypass;
        set => SetProperty(ref _proxyBypass, value);
    }

    public bool ShowTranslateButton
    {
        get => _showTranslateButton;
        set => SetProperty(ref _showTranslateButton, value);
    }

    public bool ShowProxyButton
    {
        get => _showProxyButton;
        set => SetProperty(ref _showProxyButton, value);
    }

    public bool ShowGhostButton
    {
        get => _showGhostButton;
        set => SetProperty(ref _showGhostButton, value);
    }

    public bool KeepRunningInBackground
    {
        get => _keepRunningInBackground;
        set => SetProperty(ref _keepRunningInBackground, value);
    }

    public bool PrelaunchEnabled
    {
        get => _prelaunchEnabled;
        set => SetProperty(ref _prelaunchEnabled, value);
    }

    public string StartupBehavior
    {
        get => _startupBehavior;
        set => SetProperty(ref _startupBehavior, value);
    }

    public List<SessionEntry> LastSession { get; set; } = new();
    public int LastSessionActiveIndex { get; set; }

    /// <summary>Sidebar or Floating.</summary>
    public string FavoritesDisplayMode
    {
        get => _favoritesDisplayMode;
        set => SetProperty(ref _favoritesDisplayMode, value);
    }

    /// <summary>Sidebar or Floating.</summary>
    public string DownloadsDisplayMode
    {
        get => _downloadsDisplayMode;
        set => SetProperty(ref _downloadsDisplayMode, value);
    }

    /// <summary>Comma-separated optional toolbar button order.</summary>
    public string ToolbarOrder
    {
        get => _toolbarOrder;
        set => SetProperty(ref _toolbarOrder, value);
    }

    /// <summary>zh-CN or en-US.</summary>
    public string Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    /// <summary>System, Light or Dark.</summary>
    public string ThemeMode
    {
        get => _themeMode;
        set => SetProperty(ref _themeMode, value);
    }

    /// <summary>Mica, Alt, Acrylic, Blur, Smoke or Rainbow.</summary>
    public string MicaMaterial
    {
        get => _micaMaterial;
        set => SetProperty(ref _micaMaterial, value);
    }

    /// <summary>Custom tint used by the Acrylic material.</summary>
    public string AcrylicTintColor
    {
        get => _acrylicTintColor;
        set => SetProperty(ref _acrylicTintColor, value);
    }

    public bool BackdropUseCustomTint
    {
        get => _backdropUseCustomTint;
        set => SetProperty(ref _backdropUseCustomTint, value);
    }

    public string BackdropTintColor
    {
        get => _backdropTintColor;
        set => SetProperty(ref _backdropTintColor, value);
    }

    public double BackdropTintOpacity
    {
        get => _backdropTintOpacity;
        set => SetProperty(ref _backdropTintOpacity, Math.Clamp(value, 0.0, 1.0));
    }

    public double BackdropLuminosityOpacity
    {
        get => _backdropLuminosityOpacity;
        set => SetProperty(ref _backdropLuminosityOpacity, Math.Clamp(value, 0.0, 1.0));
    }

    private bool _backdropUseCustomTint;
    private string _backdropTintColor = "#202020";
    private double _backdropTintOpacity = 0.6;
    private double _backdropLuminosityOpacity = 0.3;
    private string _favoriteFolders = "";
    private string _historyDisplayMode = "Floating";
    public string HistoryDisplayMode
    {
        get => _historyDisplayMode;
        set => SetProperty(ref _historyDisplayMode, string.IsNullOrWhiteSpace(value) ? "Floating" : value);
    }

    public string FavoriteFolders
    {
        get => _favoriteFolders;
        set => SetProperty(ref _favoriteFolders, value ?? "");
    }

    public string DefaultFavoriteFolder
    {
        get => _defaultFavoriteFolder;
        set => SetProperty(ref _defaultFavoriteFolder, string.IsNullOrWhiteSpace(value) ? "收藏夹" : value.Trim());
    }

    /// <summary>Third-party home page or extension page URL.</summary>
    public string HomePage
    {
        get => _homePage;
        set => SetProperty(ref _homePage, value);
    }

    /// <summary>Web content zoom percentage, clamped to 25–300.</summary>
    public int BrowserZoom
    {
        get => _browserZoom;
        set => SetProperty(ref _browserZoom, value);
    }

    /// <summary>0 = solid, 100 = most transparent.</summary>
    public int MicaTransparency
    {
        get => _micaTransparency;
        set => SetProperty(ref _micaTransparency, Math.Clamp(value, 0, 100));
    }

    /// <summary>UI animation duration in milliseconds. 0 disables animations.</summary>
    public int AnimationDuration
    {
        get => _animationDuration;
        set => SetProperty(ref _animationDuration, Math.Clamp(value, 0, 600));
    }

    public bool StartupMaskEnabled
    {
        get => _startupMaskEnabled;
        set => SetProperty(ref _startupMaskEnabled, value);
    }

    /// <summary>Accent color in #RRGGBB form.</summary>
    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value);
    }

    public string UiFontFamily
    {
        get => _uiFontFamily;
        set => SetProperty(ref _uiFontFamily, value ?? string.Empty);
    }

    public int UiFontWeight
    {
        get => _uiFontWeight;
        set => SetProperty(ref _uiFontWeight, Math.Clamp(value, 100, 900));
    }

    public bool NightMode
    {
        get => _nightMode;
        set => SetProperty(ref _nightMode, value);
    }

    public bool VerticalTabs
    {
        get => _verticalTabs;
        set => SetProperty(ref _verticalTabs, value);
    }

    public bool VerticalTabsIconsOnly
    {
        get => _verticalTabsIconsOnly;
        set => SetProperty(ref _verticalTabsIconsOnly, value);
    }

    public int VerticalTabWidth
    {
        get => _verticalTabWidth;
        set => SetProperty(ref _verticalTabWidth, Math.Clamp(value, 180, 420));
    }

    public bool SuspendBackgroundTabs
    {
        get => _suspendBackgroundTabs;
        set => SetProperty(ref _suspendBackgroundTabs, value);
    }

    public bool ShowHomeButton
    {
        get => _showHomeButton;
        set => SetProperty(ref _showHomeButton, value);
    }

    public bool ShowFavoriteButton
    {
        get => _showFavoriteButton;
        set => SetProperty(ref _showFavoriteButton, value);
    }

    public bool ShowShieldButton
    {
        get => _showShieldButton;
        set => SetProperty(ref _showShieldButton, value);
    }

    public bool ShowPasswordsButton
    {
        get => _showPasswordsButton;
        set => SetProperty(ref _showPasswordsButton, value);
    }

    public bool ShowFavoritesButton
    {
        get => _showFavoritesButton;
        set => SetProperty(ref _showFavoritesButton, value);
    }

    public bool ShowExtensionsButton
    {
        get => _showExtensionsButton;
        set => SetProperty(ref _showExtensionsButton, value);
    }

    public bool ShowDownloadsButton
    {
        get => _showDownloadsButton;
        set => SetProperty(ref _showDownloadsButton, value);
    }

    public long AdBlockTotalBlocked
    {
        get => _adBlockTotalBlocked;
        set => SetProperty(ref _adBlockTotalBlocked, value);
    }

    public List<string> AdBlockAllowlist { get; set; } = new();

    public string LastFilterUpdate { get; set; } = string.Empty;
}
