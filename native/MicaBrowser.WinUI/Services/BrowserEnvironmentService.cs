using Microsoft.Web.WebView2.Core;

namespace MicaBrowser.Services;

public sealed class BrowserEnvironmentService
{
    private const string BrowserArguments =
        "--disable-features=msEdgeVisualSearch,msEdgeSidebarV2 " +
        "--enable-features=msWebView2EnableDraggableRegions " +
        "--enable-gpu-rasterization --enable-zero-copy --ignore-gpu-blocklist " +
        "--enable-accelerated-video-decode";

    private readonly AppPaths _paths;
    private static string? BundledRuntimeFolder
    {
        get
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
            return File.Exists(Path.Combine(folder, "msedgewebview2.exe")) ? folder : null;
        }
    }
    private readonly SettingsService _settings;
    private CoreWebView2Environment? _privateEnvironment;
    private string? _privateDataFolder;
    private Task? _initializationTask;

    public CoreWebView2Environment? Environment { get; private set; }
    public string RuntimeVersion { get; private set; } = "unknown";

    public Task EnsureInitializedAsync() => _initializationTask ??= InitializeAsync();

    public BrowserEnvironmentService(AppPaths paths, SettingsService settings)
    {
        _paths = paths;
        _settings = settings;
    }

    private string BuildBrowserArguments()
    {
        var mode = _settings.Settings.ProxyMode;
        var args = BrowserArguments;
        if (string.Equals(mode, "Direct", StringComparison.OrdinalIgnoreCase))
        {
            args += " --no-proxy-server";
        }
        else if (string.Equals(mode, "Clash", StringComparison.OrdinalIgnoreCase))
        {
            args += $" --proxy-server=http://127.0.0.1:{_settings.Settings.ClashMixedPort}";
        }
        else if (string.Equals(mode, "Manual", StringComparison.OrdinalIgnoreCase))
        {
            var server = _settings.Settings.ProxyServer?.Trim() ?? string.Empty;
            if (server.Length > 0)
            {
                if (!server.Contains("://", StringComparison.Ordinal))
                {
                    server = "http://" + server;
                }
                args += $" --proxy-server={server}";
            }
            var bypass = _settings.Settings.ProxyBypass?.Trim() ?? string.Empty;
            if (bypass.Length > 0)
            {
                args += $" --proxy-bypass-list={bypass}";
            }
        }
        return args;
    }

    public async Task InitializeAsync()
    {
        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            AdditionalBrowserArguments = BuildBrowserArguments()
        };

        Environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            BundledRuntimeFolder,
            _paths.WebViewData,
            options);

        try
        {
            RuntimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString() ?? "unknown";
        }
        catch
        {
            RuntimeVersion = "unknown";
        }

        Logger.Info($"WebView2 runtime: {RuntimeVersion}; profile: {_paths.WebViewData}; bundled={BundledRuntimeFolder is not null}");
    }

    /// <summary>
    /// A separate, temporary profile for private tabs. It is never reused after
    /// the process exits and extensions are intentionally disabled.
    /// </summary>
    public async Task<CoreWebView2Environment> GetPrivateEnvironmentAsync()
    {
        if (_privateEnvironment is not null)
        {
            return _privateEnvironment;
        }

        _privateDataFolder = Path.Combine(
            Path.GetTempPath(),
            "EXET",
            "InPrivate",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_privateDataFolder);

        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = false,
            AdditionalBrowserArguments = BuildBrowserArguments()
        };

        _privateEnvironment = await CoreWebView2Environment.CreateWithOptionsAsync(
            BundledRuntimeFolder,
            _privateDataFolder,
            options);

        Logger.Info("Private browsing profile created.");
        return _privateEnvironment;
    }

    public void ClearPrivateProfile()
    {
        _privateEnvironment = null;
        var folder = _privateDataFolder;
        _privateDataFolder = null;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch
        {
            // WebView2 may still hold a handle during shutdown; the temp folder
            // is safe to leave behind and is cleaned by the OS later.
        }
    }
}





