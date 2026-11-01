using System.Diagnostics;
using System.Text.Json;
using MicaBrowser.Models;
using MicaBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MicaBrowser;

public sealed partial class MainWindow
{
    private static readonly string[] TrackerHosts =
    {
        "google-analytics.com", "googletagmanager.com", "doubleclick.net",
        "facebook.net", "hotjar.com", "scorecardresearch.com", "criteo.com",
        "taboola.com", "outbrain.com", "adnxs.com", "quantserve.com",
        "zedo.com", "rubiconproject.com", "pubmatic.com", "openx.net"
    };

    private async Task<bool> EnsureWebViewAsync(BrowserTab tab)
    {
        if (tab.WebView is not null && tab.Core is not null)
        {
            return true;
        }

        try
        {
            await App.Services.BrowserEnvironment.EnsureInitializedAsync();

            var environment = tab.IsPrivate
                ? await App.Services.BrowserEnvironment.GetPrivateEnvironmentAsync()
                : App.Services.BrowserEnvironment.Environment;
            var webView = new Microsoft.UI.Xaml.Controls.WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            tab.WebView = webView;
            BrowserHost.Children.Add(webView);
            await webView.EnsureCoreWebView2Async(environment);
            tab.Core = webView.CoreWebView2;
            if (tab.Core is null)
            {
                throw new InvalidOperationException("浏览器内核初始化失败。");
            }

            ConfigureCore(tab);
            if (!tab.IsPrivate && tab.Core.Profile is not null)
            {
                App.Services.Extensions.AttachProfile(tab.Core.Profile);
            var extensionTest = Environment.GetEnvironmentVariable("EXET_EXTENSION_TEST");
            if (!string.IsNullOrWhiteSpace(extensionTest))
            {
                _ = LoadTestExtensionAsync(extensionTest);
            }
            }

            ApplyCoreColorScheme(tab.Core);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not initialize a WebView2 tab.", ex);
            SetTransientStatus("网页引擎初始化失败：" + ex.Message, 8);
            return false;
        }
    }

    private void ConfigureCore(BrowserTab tab)
    {
        var core = tab.Core!;
        // Browser zoom is handled by the app so Ctrl+wheel can update the
        // saved percentage, the More menu and the top-right zoom overlay.
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        tab.DefaultUserAgent = core.Settings.UserAgent;
        var userAgent = UserAgentProvider.Resolve(App.Services.Settings);
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            core.Settings.UserAgent = userAgent;
        }

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) => OnWebResourceRequested(tab, args);
        core.NavigationStarting += (_, args) => OnNavigationStarting(tab, args);
        core.NavigationCompleted += (_, args) => OnNavigationCompleted(tab, args);
        core.SourceChanged += (_, _) => OnSourceChanged(tab);
        core.DocumentTitleChanged += (_, _) => OnDocumentTitleChanged(tab);
        core.FaviconChanged += (_, _) => OnFaviconChanged(tab);
        core.NewWindowRequested += (_, args) => OnNewWindowRequested(tab, args);
        core.PermissionRequested += (_, args) => OnPermissionRequested(tab, args);
        core.DownloadStarting += (_, args) => OnDownloadStarting(tab, args);
        core.ContextMenuRequested += async (_, args) => await AddSearchContextMenuItemAsync(tab, args);
        core.ProcessFailed += (_, args) => Logger.Warn($"WebView2 process failed: {args.ProcessFailedKind}");
        if (!tab.IsPrivate)
        {
            ConfigurePasswordCapture(tab);
        }

        ApplyBrowserZoom();
    }

    public void ApplyUserAgent()
    {
        var userAgent = UserAgentProvider.Resolve(App.Services.Settings);
        foreach (var tab in Tabs)
        {
            if (tab.Core is null) continue;
            tab.Core.Settings.UserAgent = string.IsNullOrWhiteSpace(userAgent)
                ? (tab.DefaultUserAgent ?? tab.Core.Settings.UserAgent)
                : userAgent;
        }
    }

    private void OnWebResourceRequested(BrowserTab tab, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var requestUri) ||
                (requestUri.Scheme != Uri.UriSchemeHttp && requestUri.Scheme != Uri.UriSchemeHttps))
            {
                return;
            }

            Uri? pageUri = null;
            if (Uri.TryCreate(tab.Url, UriKind.Absolute, out var parsedPage) && parsedPage.IsAbsoluteUri)
            {
                pageUri = parsedPage;
            }

            if (App.Services.Settings.DoNotTrack)
            {
                try
                {
                    args.Request.Headers.SetHeader("DNT", "1");
                }
                catch
                {
                    // Some request kinds do not expose mutable headers.
                }
            }

            var trackerBlocked = false;
            if (App.Services.Settings.BlockTrackers &&
                pageUri is not null &&
                !string.Equals(requestUri.Host, pageUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                trackerBlocked = TrackerHosts.Any(host =>
                    requestUri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                    requestUri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
            }

            if (!trackerBlocked &&
                !App.Services.AdBlock.ShouldBlock(requestUri, pageUri, args.ResourceContext, out _))
            {
                return;
            }

            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            args.Response = tab.Core!.Environment.CreateWebResourceResponse(
                stream,
                403,
                "Blocked by EXET",
                "Content-Type: text/plain; charset=utf-8");
            tab.BlockedCount++;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(_activeTab, tab))
                {
                    UpdateShieldState();
                }

                UpdateStatusBar();
            });
        }
        catch (Exception ex)
        {
            Logger.Warn("AdGuard request filter failed.", ex);
        }
    }

    private void OnNavigationStarting(BrowserTab tab, CoreWebView2NavigationStartingEventArgs args)
    {
        if (IsPdfSource(args.Uri))
        {
            args.Cancel = true;
            OpenPdfPreview(args.Uri);
            return;
        }

        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is not ("http" or "https" or "file" or "about" or "chrome-extension"))
            {
                args.Cancel = true;
                return;
            }

            tab.IsHomePage = IsHomeUrl(args.Uri);
            tab.Url = args.Uri;
            tab.IsNewTab = false;
        }

        tab.IsLoading = true;
        if (ReferenceEquals(_activeTab, tab))
        {
            _addressTyping = false;
            HideAddressSuggestions();
            AddressLoading.Visibility = Visibility.Visible;
            AddressLoading.IsActive = true;
            SetAddressBoxText(tab.IsHomePage ? string.Empty : args.Uri);
        }
    }

    private void RecordSessionHistory(BrowserTab tab)
    {
        if (tab.Core is null || string.IsNullOrWhiteSpace(tab.Url))
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title;
        var existing = SessionHistory.FirstOrDefault(item =>
            string.Equals(item.Url, tab.Url, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Title = title;
            existing.VisitedAt = DateTime.Now;
            var oldIndex = SessionHistory.IndexOf(existing);
            if (oldIndex > 0)
            {
                SessionHistory.Move(oldIndex, 0);
            }
            return;
        }

        SessionHistory.Insert(0, new HistoryEntry
        {
            Url = tab.Url,
            Title = title,
            VisitedAt = DateTime.Now
        });
        while (SessionHistory.Count > 30)
        {
            SessionHistory.RemoveAt(SessionHistory.Count - 1);
        }
    }

    private void OnNavigationCompleted(BrowserTab tab, CoreWebView2NavigationCompletedEventArgs args)
    {
        tab.IsLoading = false;
        RecordSessionHistory(tab);
        if (tab.Favicon is null)
        {
            _ = UpdateFaviconFallbackAsync(tab);
        }
        if (tab.Core is not null)
        {
            tab.CanGoBack = tab.Core.CanGoBack;
            tab.CanGoForward = tab.Core.CanGoForward;
            if (!string.IsNullOrWhiteSpace(tab.Core.DocumentTitle))
            {
                tab.Title = tab.Core.DocumentTitle;
            }

            if (!string.IsNullOrWhiteSpace(tab.Core.Source))
            {
                tab.Url = tab.Core.Source;
            }
        }

        if (tab.Core is not null)
        {
            ApplyPageDarkMode(tab.Core);
            ApplyPageZoom(tab.Core);
            ApplyHomeTone(tab);
        }

        if (ReferenceEquals(_activeTab, tab))
        {
            _addressTyping = false;
            HideAddressSuggestions();
        }

        if (ReferenceEquals(_activeTab, tab))
        {
            AddressLoading.IsActive = false;
            AddressLoading.Visibility = Visibility.Collapsed;
            UpdateAddressBar();
            UpdateNavigationState();
            UpdateShieldState();
            UpdateFavoriteState();
            UpdateStatusBar();
            UpdateWindowTitle();
        }

        if (!tab.IsPrivate &&
            args.IsSuccess &&
            Uri.TryCreate(tab.Url, UriKind.Absolute, out var historyUri) &&
            (historyUri.Scheme == Uri.UriSchemeHttp || historyUri.Scheme == Uri.UriSchemeHttps))
        {
            App.Services.History.Record(tab.Url, tab.Title);
            _ = TryAutofillPasswordAsync(tab);
        }
    }

    private void OnSourceChanged(BrowserTab tab)
    {
        if (tab.Core is null)
        {
            return;
        }

        tab.Url = tab.Core.Source;
        if (IsHomeUrl(tab.Url))
        {
            tab.IsHomePage = true;
        }

        if (ReferenceEquals(_activeTab, tab))
        {
            SetAddressBoxText(IsInlineSettingsOpen
                ? "exet://settings"
                : (tab.IsHomePage || IsHomeUrl(tab.Url) ? string.Empty : tab.Url));
            UpdateNavigationState();
        }
    }

    private void OnDocumentTitleChanged(BrowserTab tab)
    {
        if (tab.Core is null || string.IsNullOrWhiteSpace(tab.Core.DocumentTitle))
        {
            return;
        }

        tab.Title = tab.Core.DocumentTitle;
        if (ReferenceEquals(_activeTab, tab))
        {
            UpdateWindowTitle();
        }
    }

    private void OnFaviconChanged(BrowserTab tab)
    {
        try
        {
            var uri = tab.Core?.FaviconUri;
            if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var faviconUri))
            {
                _ = UpdateFaviconFallbackAsync(tab);
                return;
            }

            tab.Favicon = new BitmapImage(faviconUri);
        }
        catch
        {
            // Favicon is optional.
        }
    }

    private async Task UpdateFaviconFallbackAsync(BrowserTab tab)
    {
        if (tab.Core is null || string.IsNullOrWhiteSpace(tab.Url)) return;
        try
        {
            var script = "(function(){var l=document.querySelector('link[rel~=\"icon\"],link[rel=\"shortcut icon\"]');return l&&l.href?l.href:'';})()";
            var raw = await tab.Core.ExecuteScriptAsync(script);
            var href = raw.Trim('"').Replace("\\/", "/");
            if (!string.IsNullOrWhiteSpace(href) &&
                Uri.TryCreate(href, UriKind.Absolute, out var hrefUri) &&
                (hrefUri.Scheme == Uri.UriSchemeHttps || hrefUri.Scheme == Uri.UriSchemeHttp))
            {
                tab.Favicon = new BitmapImage(hrefUri);
                return;
            }

            if (Uri.TryCreate(tab.Url, UriKind.Absolute, out var pageUri))
            {
                var fallback = new Uri(pageUri.GetLeftPart(UriPartial.Authority) + "/favicon.ico");
                tab.Favicon = new BitmapImage(fallback);
            }
        }
        catch
        {
            // A site without a favicon keeps the letter badge.
        }
    }

    private void OnNewWindowRequested(BrowserTab tab, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (string.IsNullOrWhiteSpace(args.Uri) || args.Uri == "about:blank")
        {
            return;
        }

        if (IsPdfSource(args.Uri))
        {
            OpenPdfPreview(args.Uri);
            return;
        }

        var uri = args.Uri;
        DispatcherQueue.TryEnqueue(() => CreateTab(uri, true, tab.IsPrivate));
    }

    private async void OnPermissionRequested(BrowserTab tab, CoreWebView2PermissionRequestedEventArgs args)
    {
        args.Handled = true;
        if (App.Services.Settings.DenySensitivePermissions &&
            args.PermissionKind is Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Camera
                or Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone
                or Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Geolocation
                or Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Notifications)
        {
            args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            return;
        }

        var label = PermissionLabel(args.PermissionKind);
        var host = Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
            ? uri.Host
            : tab.Url;
        var key = $"{args.PermissionKind}|{host}";
        var rules = App.Services.Settings.PermissionRules;
        if (rules.TryGetValue(key, out var saved))
        {
            args.State = saved == "allow"
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
            return;
        }

        if (_permissionPromptOpen || RootGrid.XamlRoot is null)
        {
            args.State = CoreWebView2PermissionState.Deny;
            return;
        }

        var deferral = args.GetDeferral();
        _permissionPromptOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "网站权限请求",
                Content = $"{host}\n\n请求使用{label}。是否允许？",
                PrimaryButtonText = "允许",
                CloseButtonText = "阻止",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            var allow = await dialog.ShowAsync() == ContentDialogResult.Primary;
            args.State = allow
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
            rules[key] = allow ? "allow" : "deny";
            App.Services.SettingsStore.Save();
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not show permission prompt.", ex);
            args.State = CoreWebView2PermissionState.Deny;
        }
        finally
        {
            _permissionPromptOpen = false;
            deferral.Complete();
        }
    }

    private bool _permissionPromptOpen;

    private static string PermissionLabel(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Camera => "摄像头",
        CoreWebView2PermissionKind.Microphone => "麦克风",
        CoreWebView2PermissionKind.Geolocation => "定位",
        CoreWebView2PermissionKind.Notifications => "通知",
        CoreWebView2PermissionKind.ClipboardRead => "剪贴板",
        _ => "网站"
    };

    private async void OnDownloadStarting(BrowserTab tab, CoreWebView2DownloadStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (App.Services.Settings.GhostDownloaderEnabled)
            {
                var ghost = await TrySendToGhostDownloader(args.DownloadOperation.Uri);
                if (ghost is not null)
                {
                    args.Cancel = true;
                    args.Handled = true;
                    var ghostItem = new DownloadItem
                    {
                        FileName = ghost.Name,
                        FilePath = Path.Combine(ghost.Folder, ghost.Name),
                        SourceUrl = args.DownloadOperation.Uri
                    };
                    ghostItem.State = "正在下载";
                    Downloads.Insert(0, ghostItem);
                    OpenDownloadsSidebar();
                    SetTransientStatus("已交给 Ghost Downloader");
                    _ = PollGhostTaskAsync(ghostItem, ghost.TaskId);
                    return;
                }
            }

            var suggestedName = Path.GetFileName(args.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggestedName))
            {
                suggestedName = "download";
            }

            string destination;
            if (App.Services.Settings.AskWhereToSave)
            {
                var picker = new FileSavePicker
                {
                    SuggestedFileName = suggestedName,
                    SuggestedStartLocation = PickerLocationId.Downloads
                };
                var extension = Path.GetExtension(suggestedName);
                if (string.IsNullOrWhiteSpace(extension) || extension == ".")
                {
                    extension = ".download";
                }

                picker.FileTypeChoices.Add("文件", new List<string> { extension });
                InitializeWithWindow.Initialize(picker, _hwnd);
                var file = await picker.PickSaveFileAsync();
                if (file is null)
                {
                    args.Cancel = true;
                    return;
                }

                destination = file.Path;
            }
            else
            {
                destination = GetUniqueDownloadPath(suggestedName);
            }

            args.ResultFilePath = destination;
            args.Handled = true;

            var item = new DownloadItem
            {
                FileName = Path.GetFileName(destination),
                FilePath = destination,
                SourceUrl = args.DownloadOperation.Uri,
                Operation = args.DownloadOperation
            };
            item.State = "正在下载";
            Downloads.Insert(0, item);
            OpenDownloadsSidebar();

            var operation = args.DownloadOperation;
            operation.BytesReceivedChanged += (_, _) => UpdateDownloadItem(item, operation);
            operation.StateChanged += (_, _) =>
            {
                UpdateDownloadItem(item, operation);
                SaveDownloadHistory();
            };
            UpdateDownloadItem(item, operation);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not start download.", ex);
            SetTransientStatus("下载失败：" + ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<GhostTaskInfo?> TrySendToGhostDownloader(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var executable = GhostDownloaderService.ResolveExecutable(App.Services.Settings.GhostDownloaderPath);
            var folder = App.Services.Settings.DownloadFolder?.Trim();
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = App.Services.Paths.DownloadsFolder;
            }
            Directory.CreateDirectory(folder);

            var task = await GhostDownloaderService.AddAsync(executable, url, folder);
            if (task is null)
            {
                SetTransientStatus("Ghost Downloader 没有返回任务，已改回浏览器下载");
                return null;
            }
            Logger.Info($"Ghost Downloader task {task.TaskId}: {url}");
            return task;
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not send download to Ghost Downloader.", ex);
            SetTransientStatus("Ghost Downloader 启动失败：" + ex.Message);
            return null;
        }
    }

    private async Task PollGhostTaskAsync(DownloadItem item, string taskId)
    {
        var executable = GhostDownloaderService.ResolveExecutable(App.Services.Settings.GhostDownloaderPath);
        while (true)
        {
            await Task.Delay(1000);
            var progress = await GhostDownloaderService.GetProgressAsync(executable, taskId);
            if (progress is null) continue;

            var done = progress.Status.Equals("completed", StringComparison.OrdinalIgnoreCase);
            var failed = progress.Status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                         progress.Status.Equals("error", StringComparison.OrdinalIgnoreCase);
            var paused = progress.Status.Contains("pause", StringComparison.OrdinalIgnoreCase);
            var status = done ? "已完成" : failed ? "下载失败" : paused ? "已暂停" : "正在下载";
            DispatcherQueue.TryEnqueue(() =>
            {
                item.TotalBytes = Math.Max(0, progress.FileSize);
                item.ReceivedBytes = Math.Max(0, (long)(progress.FileSize * Math.Clamp(progress.Progress, 0, 100) / 100));
                item.State = status;
                item.IsComplete = done;
                item.IsFailed = failed;
                UpdateStatusBar();
                SaveDownloadHistory();
            });
            if (done || failed) return;
        }
    }

    private void UpdateDownloadItem(DownloadItem item, CoreWebView2DownloadOperation operation)
    {
        void Update()
        {
            item.ReceivedBytes = Math.Max(0, operation.BytesReceived);
            item.TotalBytes = Math.Max(0, operation.TotalBytesToReceive);
            switch (operation.State)
            {
                case CoreWebView2DownloadState.Completed:
                    item.IsComplete = true;
                    item.IsFailed = false;
                    item.State = "已完成";
                    break;
                case CoreWebView2DownloadState.Interrupted:
                    item.IsFailed = true;
                    item.IsComplete = false;
                    item.State = "下载已中断";
                    break;
                default:
                    item.State = "正在下载";
                    break;
            }

            UpdateStatusBar();
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Update();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Update);
        }
    }

    private void OpenDownloadsSidebar()
    {
        OpenSidebar("downloads");
    }

    private string GetUniqueDownloadPath(string fileName)
    {
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "download";
        }

        var configured = App.Services.Settings.DownloadFolder?.Trim();
        var folder = string.IsNullOrWhiteSpace(configured)
            ? App.Services.Paths.DownloadsFolder
            : configured;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            folder = App.Services.Paths.DownloadsFolder;
            Directory.CreateDirectory(folder);
        }

        var path = Path.Combine(folder, safeName);
        if (!File.Exists(path))
        {
            return path;
        }

        var name = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var i = 1; i < 10000; i++)
        {
            path = Path.Combine(folder, $"{name} ({i}){extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(folder, $"{name} ({Guid.NewGuid():N}){extension}");
    }

    private void NavigateActive(string input, bool isHome = false)
    {
        var target = (input ?? string.Empty).Trim();
        if (target.Equals("exet://settings", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("exet://settings/", StringComparison.OrdinalIgnoreCase))
        {
            OpenSettings("general");
            return;
        }
        if (IsInlineSettingsOpen) CloseInlineSettings();
        if (_activeTab is null)
        {
            _activeTab = CreateTab(null, true);
        }

        if (_activeTab is not null)
        {
            _ = NavigateAsync(_activeTab, input, true, isHome);
        }
    }

    private async Task NavigateAsync(BrowserTab tab, string input, bool fromUser, bool isHome = false)
    {
        var target = NormalizeInput(input);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        if (IsPdfSource(target))
        {
            if (ReferenceEquals(_activeTab, tab))
            {
                SetAddressBoxText(target);
            }

            OpenPdfPreview(target);
            return;
        }

        if (!await EnsureWebViewAsync(tab) || tab.Core is null)
        {
            return;
        }

        tab.IsNewTab = false;
        tab.IsHomePage = isHome;
        if (ReferenceEquals(_activeTab, tab))
        {
            SetAddressBoxText(isHome ? string.Empty : target);
        }

        if (tab.WebView is not null)
        {
            tab.WebView.Visibility = ReferenceEquals(_activeTab, tab) ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ReferenceEquals(_activeTab, tab))
        {
            UpdateLoadingState(true);
        }

        try
        {
            tab.Core.Navigate(target);
        }
        catch (Exception ex)
        {
            Logger.Error($"Navigation failed: {target}", ex);
            SetTransientStatus("无法打开页面：" + ex.Message, 6);
        }
    }

    private string NormalizeInput(string input)
    {
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https" or "file" or "about" or "chrome-extension")
        {
            return absolute.AbsoluteUri;
        }

        try
        {
            if (File.Exists(text))
            {
                return new Uri(Path.GetFullPath(text)).AbsoluteUri;
            }
        }
        catch
        {
            // Not a path.
        }

        if (text.Contains(' ') || !text.Contains('.'))
        {
            return SearchUri(text);
        }

        return "https://" + text;
    }

    private static readonly string HomeToneScript = """
        (() => {
            const id = 'exet-home-tone';
            const old = document.getElementById(id);
            if (old) old.remove();
            const style = document.createElement('style');
            style.id = id;
            style.textContent = 'html { filter: brightness(0.94); }';
            (document.head || document.documentElement).appendChild(style);
        })();
        """;

    private static readonly string ClearHomeToneScript = """
        (() => {
            const old = document.getElementById('exet-home-tone');
            if (old) old.remove();
        })();
        """;

    private void ApplyHomeTone(BrowserTab tab)
    {
        if (tab.Core is null)
        {
            return;
        }

        var isHome = tab.IsHomePage || IsHomeUrl(tab.Url);
        _ = tab.Core.ExecuteScriptAsync(isHome ? HomeToneScript : ClearHomeToneScript);
    }

    private async Task AddSearchContextMenuItemAsync(
        BrowserTab tab, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var raw = await tab.Core!.ExecuteScriptAsync(
                "(window.getSelection ? window.getSelection().toString() : '').trim()");
            var selected = JsonSerializer.Deserialize<string>(raw)?.Trim();
            if (string.IsNullOrWhiteSpace(selected)) return;

            var label = selected.Length > 28 ? selected[..28] + "…" : selected;
            var item = tab.Core.Environment.CreateContextMenuItem(
                "在网页中搜索“" + label + "”",
                null,
                CoreWebView2ContextMenuItemKind.Command);
            item.CustomItemSelected += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() => NavigateActive(SearchUri(selected)));
            };
            args.MenuItems.Insert(0, item);
            args.MenuItems.Insert(1, tab.Core.Environment.CreateContextMenuItem(
                "-", null, CoreWebView2ContextMenuItemKind.Separator));
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not add web search context menu item.", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private string SearchUri(string query)
    {
        var term = Uri.EscapeDataString(query);
        var settings = App.Services.Settings;
        return settings.SearchEngine switch
        {
            "Baidu" => "https://www.baidu.com/s?wd=" + term,
            "Google" => "https://www.google.com/search?q=" + term,
            "DuckDuckGo" => "https://duckduckgo.com/?q=" + term,
            "Sogou" => "https://www.sogou.com/web?query=" + term,
            "Yandex" => "https://yandex.com/search/?text=" + term,
            "Custom" => BuildCustomSearchUrl(settings.CustomSearchUrl, term),
            _ => "https://www.bing.com/search?q=" + term
        };
    }

    private static string BuildCustomSearchUrl(string template, string escapedTerm)
    {
        var value = string.IsNullOrWhiteSpace(template)
            ? "https://www.google.com/search?q={query}"
            : template.Trim();
        if (!value.Contains("{query}", StringComparison.OrdinalIgnoreCase))
        {
            value += value.Contains('?') ? "&q={query}" : "?q={query}";
        }

        return value.Replace("{query}", escapedTerm, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadTestExtensionAsync(string folder)
    {
        try
        {
            var info = await App.Services.Extensions.AddFromFolderAsync(folder);
            Logger.Info($"Extension test loaded: {info?.Name} / {info?.Id}");
        }
        catch (Exception ex)
        {
            Logger.Warn("Extension test failed.", ex);
        }
    }
}
