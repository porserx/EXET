using System.Diagnostics;
using System.Globalization;
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
        // Keep Edge from handling Ctrl+J / Ctrl+H / Ctrl+D / Ctrl+Shift+S
        // as its own download, history, favorite and settings panels.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
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
        core.WebMessageReceived += (_, args) => OnWebShortcutMessage(args);
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync("""
            (function () {
              if (window.__exetShortcutInstalled) return;
              window.__exetShortcutInstalled = true;
              document.addEventListener('keydown', function (event) {
                if (!event.ctrlKey || event.altKey) return;
                const key = (event.key || '').toLowerCase();
                let action = null;
                if (key === 'f' && !event.shiftKey) action = 'find';
                else if (key === 'p' && !event.shiftKey) action = 'print';
                else if (key === 'u' && !event.shiftKey) action = 'viewsource';
                else if (key === 't' && event.shiftKey) action = 'reopen';
                else if (key === 's' && event.shiftKey) action = 'settings';
                else if (key === 'j' && !event.shiftKey) action = 'downloads';
                else if (key === 'h' && !event.shiftKey) action = 'history';
                else if (key === 'd' && !event.shiftKey) action = 'favorite';
                else if (key === 't' && !event.shiftKey) action = 'newtab';
                else if (key === 'w' && !event.shiftKey) action = 'closetab';
                else if (key === 'l' && !event.shiftKey) action = 'address';
                else if (key === 'r' && !event.shiftKey) action = 'reload';
                else if (key === 'n' && event.shiftKey) action = 'private';
                else if (event.key === 'Tab' && event.shiftKey) action = 'previoustab';
                else if (event.key === 'Tab') action = 'nexttab';
                else if (key === '0' && !event.shiftKey) action = 'zoomreset';
                else if (key === '+' || key === '=') action = 'zoomin';
                else if (key === '-') action = 'zoomout';
                else if (/^[1-9]$/.test(key) && !event.shiftKey) action = 'tab:' + key;
                else if (event.key === 'F5') action = 'reload';
                if (!action) return;
                event.preventDefault();
                event.stopPropagation();
                if (window.chrome && window.chrome.webview) {
                  window.chrome.webview.postMessage('exet-shortcut:' + action);
                }
              }, true);
            })();
            """);
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

    private void OnWebShortcutMessage(CoreWebView2WebMessageReceivedEventArgs args)
    {
        string? message;
        try
        {
            message = args.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        const string prefix = "exet-shortcut:";
        if (message is null || !message.StartsWith(prefix, StringComparison.Ordinal)) return;
        var action = message[prefix.Length..];
        DispatcherQueue.TryEnqueue(() => HandleWebShortcut(action));
    }

    private void HandleWebShortcut(string action)
    {
        switch (action)
        {
            case "find":
                OpenFindBar();
                break;
            case "print":
                try
                {
                    _activeTab?.Core?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Print failed.", ex);
                    SetTransientStatus("打印失败：" + ex.Message, 6);
                }
                break;
            case "viewsource":
                if (_activeTab?.Url is { Length: > 0 } sourceUrl)
                {
                    NavigateActive("view-source:" + sourceUrl);
                }
                break;
            case "nexttab":
                SwitchTab(1);
                break;
            case "previoustab":
                SwitchTab(-1);
                break;
            case "reopen":
                if (_recentlyClosed.FirstOrDefault() is { } closed)
                {
                    _recentlyClosed.Remove(closed);
                    CreateTab(closed.Url, true);
                }
                break;
            case "zoomreset":
                SetBrowserZoom(100);
                break;
            case "zoomin":
                SetBrowserZoom(App.Services.Settings.BrowserZoom + 10);
                break;
            case "zoomout":
                SetBrowserZoom(App.Services.Settings.BrowserZoom - 10);
                break;
            case "tab:1":
            case "tab:2":
            case "tab:3":
            case "tab:4":
            case "tab:5":
            case "tab:6":
            case "tab:7":
            case "tab:8":
            case "tab:9":
                SelectTabByNumber(action[4] - '0');
                break;
            case "settings":
                OpenSettings("general");
                break;
            case "downloads":
                OpenSidebar("downloads");
                break;
            case "history":
                OpenSidebar("history");
                break;
            case "favorite":
                FavoriteButton_Click(FavoriteButton, new RoutedEventArgs());
                break;
            case "newtab":
                CreateTab(null, true);
                break;
            case "private":
                CreatePrivateTab();
                break;
            case "closetab":
                CloseActiveTab();
                break;
            case "address":
                AddressBox.Focus(FocusState.Programmatic);
                AddressBox.SelectAll();
                break;
            case "reload":
                _activeTab?.Core?.Reload();
                break;
        }
    }

    private void SwitchTab(int delta)
    {
        if (Tabs.Count < 2 || _activeTab is null) return;
        var index = Tabs.IndexOf(_activeTab);
        if (index < 0) return;
        SelectTab(Tabs[(index + delta + Tabs.Count) % Tabs.Count]);
    }

    private void SelectTabByNumber(int number)
    {
        if (Tabs.Count == 0) return;
        var index = number == 9 ? Tabs.Count - 1 : Math.Min(number - 1, Tabs.Count - 1);
        SelectTab(Tabs[index]);
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
            _ = TryAutoTranslateAsync(tab);
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
                var wasComplete = item.IsComplete;
                item.TotalBytes = Math.Max(0, progress.FileSize);
                item.ReceivedBytes = Math.Max(0, (long)(progress.FileSize * Math.Clamp(progress.Progress, 0, 100) / 100));
                item.State = status;
                item.IsComplete = done;
                item.IsFailed = failed;
                UpdateStatusBar();
                SaveDownloadHistory();
                if (done && !wasComplete)
                {
                    NotifyDownloadCompleted(item);
                }
            });
            if (done || failed) return;
        }
    }

    private void UpdateDownloadItem(DownloadItem item, CoreWebView2DownloadOperation operation)
    {
        void Update()
        {
            var wasComplete = item.IsComplete;
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
            if (item.IsComplete && !wasComplete)
            {
                NotifyDownloadCompleted(item);
            }
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

            var translate = tab.Core.Environment.CreateContextMenuItem(
                "翻译选中文字“" + label + "”",
                null,
                CoreWebView2ContextMenuItemKind.Command);
            translate.CustomItemSelected += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(async () => await TranslateSelectedTextAsync(tab, selected));
            };

            var search = tab.Core.Environment.CreateContextMenuItem(
                "在网页中搜索“" + label + "”",
                null,
                CoreWebView2ContextMenuItemKind.Command);
            search.CustomItemSelected += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() => NavigateActive(SearchUri(selected)));
            };

            args.MenuItems.Insert(0, translate);
            args.MenuItems.Insert(1, search);
            args.MenuItems.Insert(2, tab.Core.Environment.CreateContextMenuItem(
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

    private async Task TranslateSelectedTextAsync(BrowserTab tab, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || tab.Core is null) return;

        try
        {
            var settings = App.Services.Settings;
            SetTransientStatus("正在翻译选中文字…", 4);
            var translated = await TranslationService.TranslateBatchAsync(
                settings,
                new[] { trimmed },
                settings.TranslationTarget,
                string.IsNullOrWhiteSpace(settings.TranslationSourceLanguage)
                    ? "auto"
                    : settings.TranslationSourceLanguage);
            var value = translated.FirstOrDefault() ?? trimmed;
            var json = JsonSerializer.Serialize(value);

            var script = """
                (function(text){
                  const old = document.getElementById('exet-selection-translation');
                  if (old) old.remove();
                  let left = 24, top = 80;
                  const sel = window.getSelection();
                  if (sel && sel.rangeCount) {
                    const r = sel.getRangeAt(0).getBoundingClientRect();
                    left = r.left;
                    top = r.bottom + 8;
                  }
                  const width = 420;
                  left = Math.max(12, Math.min(left, innerWidth - width - 16));
                  top = Math.max(12, Math.min(top, innerHeight - 160));
                  const box = document.createElement('div');
                  box.id = 'exet-selection-translation';
                  box.textContent = text;
                  Object.assign(box.style, {
                    position: 'fixed', left: left + 'px', top: top + 'px',
                    width: 'auto', maxWidth: width + 'px', maxHeight: '220px',
                    overflow: 'auto', zIndex: '2147483647',
                    padding: '12px 34px 12px 14px', borderRadius: '12px',
                    background: 'rgba(28,30,34,.96)', color: '#fff',
                    font: '13px/1.6 "Microsoft YaHei UI", sans-serif',
                    boxShadow: '0 10px 30px rgba(0,0,0,.35)',
                    border: '1px solid rgba(255,255,255,.14)',
                    whiteSpace: 'pre-wrap', cursor: 'copy'
                  });
                  const close = document.createElement('button');
                  close.textContent = '×';
                  Object.assign(close.style, {
                    position: 'absolute', right: '8px', top: '4px',
                    border: '0', background: 'transparent', color: '#fff',
                    fontSize: '18px', lineHeight: '20px', cursor: 'pointer'
                  });
                  close.onclick = () => box.remove();
                  box.appendChild(close);
                  box.addEventListener('click', () => {
                    if (navigator.clipboard) navigator.clipboard.writeText(text);
                  });
                  document.body.appendChild(box);
                  return true;
                })(__VALUE__)
                """.Replace("__VALUE__", json);
            await tab.Core.ExecuteScriptAsync(script);
            SetTransientStatus("译文已显示在网页中（点击浮层可复制）", 4);
        }
        catch (Exception ex)
        {
            Logger.Warn("Selected text translation failed.", ex);
            SetTransientStatus("翻译失败：" + ex.Message, 8);
        }
    }

    private readonly Dictionary<string, bool> _autoTranslateAttempts = new(StringComparer.Ordinal);

    private async Task TryAutoTranslateAsync(BrowserTab tab)
    {
        var autoSettings = App.Services.Settings;
        if (!autoSettings.SmartTranslateEnabled ||
            tab.Core is null ||
            tab.IsPrivate ||
            tab.IsHomePage ||
            IsAutoTranslateExcludedUrl(tab.Url, autoSettings.HomePage) ||
            !Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = tab.Id + "|" + tab.Url;
        if (_autoTranslateAttempts.ContainsKey(key)) return;
        _autoTranslateAttempts[key] = true;

        try
        {
            // Let client-rendered pages finish their first paint before sampling.
            await Task.Delay(800);
            var raw = await tab.Core.ExecuteScriptAsync("""
                (function(){
                  if (!document.body) return JSON.stringify({lang:'',text:''});
                  const text=(document.body.innerText||'').replace(/\s+/g,' ').trim();
                  return JSON.stringify({
                    lang:(document.documentElement.lang||'').toLowerCase(),
                    text:text.slice(0,6000)
                  });
                })()
                """);
            var json = JsonSerializer.Deserialize<string>(raw) ?? "{}";
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;
            var lang = doc.RootElement.TryGetProperty("lang", out var langElement)
                ? langElement.GetString() ?? string.Empty
                : string.Empty;
            if (text.Length < 120) return;

            var (detected, confidence) = DetectTextLanguage(text, lang);
            var target = SystemTranslationTarget();
            if (detected is null || detected == target || confidence < 0.85) return;
            if (TargetLanguageShare(text, target) > 0.05) return;

            SetTransientStatus(
                $"检测到网页语言：{TranslationLanguageLabel(detected)}，正在自动翻译…", 5);
            await TranslatePageAsync(target, TranslationLanguageLabel(target));
        }
        catch (Exception ex)
        {
            Logger.Warn("Smart translation check failed.", ex);
        }
    }

    private static bool IsAutoTranslateExcludedUrl(string url, string? homePage)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var page)) return true;
        var path = page.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        if (path.EndsWith("/newtab") || path.EndsWith("/new-tab") || path.EndsWith("/startpage"))
        {
            return true;
        }

        if (Uri.TryCreate(homePage, UriKind.Absolute, out var home) &&
            string.Equals(page.Host, home.Host, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(path, home.AbsolutePath.TrimEnd('/').ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    private static double TargetLanguageShare(string text, string target)
    {
        var total = 0;
        var matching = 0;
        foreach (var ch in text)
        {
            var isLetter = char.IsLetter(ch);
            var isCjk = ch >= '\u4e00' && ch <= '\u9fff';
            var isKana = ch >= '\u3040' && ch <= '\u30ff';
            var isHangul = ch >= '\uac00' && ch <= '\ud7af';
            var isCyrillic = ch >= '\u0400' && ch <= '\u04ff';
            var isArabic = ch >= '\u0600' && ch <= '\u06ff';
            var isLatin = isLetter && !isCjk && !isKana && !isHangul && !isCyrillic && !isArabic;
            if (!isLetter && !isCjk && !isKana && !isHangul) continue;
            total++;
            matching += target switch
            {
                "zh-CN" or "zh-TW" => isCjk ? 1 : 0,
                "ja" => isCjk || isKana ? 1 : 0,
                "ko" => isHangul ? 1 : 0,
                "ru" => isCyrillic ? 1 : 0,
                "ar" => isArabic ? 1 : 0,
                _ => isLatin ? 1 : 0
            };
        }
        return total == 0 ? 0 : (double)matching / total;
    }

    private static string SystemTranslationTarget()
    {
        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name.ToLowerInvariant();
        if (name.StartsWith("zh")) return "zh-CN";
        if (name.StartsWith("ja")) return "ja";
        if (name.StartsWith("ko")) return "ko";
        if (name.StartsWith("fr")) return "fr";
        if (name.StartsWith("de")) return "de";
        if (name.StartsWith("es")) return "es";
        if (name.StartsWith("ru")) return "ru";
        if (name.StartsWith("ar")) return "ar";
        if (name.StartsWith("pt")) return "pt";
        if (name.StartsWith("it")) return "it";
        return "en";
    }

    private static (string? Language, double Confidence) DetectTextLanguage(string text, string lang)
    {
        int cjk = 0, latin = 0, cyrillic = 0, arabic = 0, hangul = 0, kana = 0;
        foreach (var ch in text)
        {
            if (ch >= '\u3040' && ch <= '\u30ff') kana++;
            else if (ch >= '\uac00' && ch <= '\ud7af') hangul++;
            else if (ch >= '\u4e00' && ch <= '\u9fff') cjk++;
            else if (ch >= '\u0400' && ch <= '\u04ff') cyrillic++;
            else if (ch >= '\u0600' && ch <= '\u06ff') arabic++;
            else if (char.IsLetter(ch)) latin++;
        }

        var total = cjk + latin + cyrillic + arabic + hangul + kana;
        if (total < 40) return (null, 0);

        var max = Math.Max(Math.Max(Math.Max(cjk, latin), Math.Max(cyrillic, arabic)), Math.Max(hangul, kana));
        var confidence = (double)max / total;

        if (kana > 0 && kana >= cjk / 3) return ("ja", confidence);
        if (hangul > 0 && hangul >= max / 2) return ("ko", confidence);
        if (cjk > 0 && cjk >= max / 2) return ("zh-CN", confidence);
        if (cyrillic > 0 && cyrillic >= max / 2) return ("ru", confidence);
        if (arabic > 0 && arabic >= max / 2) return ("ar", confidence);

        var normalized = lang.Split('-')[0];
        return normalized switch
        {
            "fr" => ("fr", confidence),
            "de" => ("de", confidence),
            "es" => ("es", confidence),
            "pt" => ("pt", confidence),
            "it" => ("it", confidence),
            _ => ("en", confidence)
        };
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
