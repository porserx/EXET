using System.Text.Json;
using MicaBrowser.Models;
using MicaBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace MicaBrowser;

public sealed partial class MainWindow
{
    private const string PasswordCaptureScript = """
        (() => {
            if (window.__micaPasswordCaptureInstalled) return;
            window.__micaPasswordCaptureInstalled = true;
            let lastSent = 0;

            function findCredentials() {
                const password = document.querySelector('input[type="password"]');
                if (!password || !password.value) return null;
                const form = password.form || document;
                let username = '';
                const candidates = form.querySelectorAll(
                    'input[type="email"], input[type="text"], input[type="tel"], input[autocomplete="username"]');
                for (const input of candidates) {
                    if (input !== password && !input.disabled && !input.readOnly && input.value) {
                        username = input.value;
                        break;
                    }
                }
                if (!username) {
                    const inputs = form.querySelectorAll('input');
                    for (const input of inputs) {
                        if (input !== password && !input.disabled && !input.readOnly &&
                            input.type !== 'password' && input.type !== 'hidden' && input.value) {
                            username = input.value;
                            break;
                        }
                    }
                }
                return username ? { username, password: password.value } : null;
            }

            function sendCredentials() {
                try {
                    const value = findCredentials();
                    if (!value) return;
                    const now = Date.now();
                    if (now - lastSent < 1200) return;
                    lastSent = now;
                    if (!window.chrome || !window.chrome.webview) return;
                    window.chrome.webview.postMessage({
                        kind: 'mica-credentials',
                        url: location.href,
                        username: value.username,
                        password: value.password
                    });
                } catch (_) { }
            }

            document.addEventListener('submit', () => sendCredentials(), true);
            document.addEventListener('click', (event) => {
                try {
                    const target = event.target;
                    const button = target && target.closest
                        ? target.closest('button, input[type="submit"], [role="button"]')
                        : null;
                    if (button && document.querySelector('input[type="password"]')) {
                        setTimeout(sendCredentials, 350);
                    }
                } catch (_) { }
            }, true);
            document.addEventListener('keydown', (event) => {
                if (event.key === 'Enter' && document.querySelector('input[type="password"]')) {
                    setTimeout(sendCredentials, 350);
                }
            }, true);
        })();
        """;

    private readonly HashSet<string> _recentPasswordPrompts = new(StringComparer.OrdinalIgnoreCase);
    private bool _passwordPromptOpen;

    private void ConfigurePasswordCapture(BrowserTab tab)
    {
        var core = tab.Core;
        if (core is null)
        {
            return;
        }

        core.WebMessageReceived += (_, args) => OnPasswordWebMessage(tab, args);
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(PasswordCaptureScript)
            .AsTask()
            .ContinueWith(
                task => Logger.Warn("Could not install password capture script.", task.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    private void OnPasswordWebMessage(BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json) || !json.Contains("mica-credentials", StringComparison.Ordinal))
            {
                return;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("kind", out var kind) ||
                kind.GetString() != "mica-credentials" ||
                !root.TryGetProperty("url", out var urlElement) ||
                !root.TryGetProperty("username", out var usernameElement) ||
                !root.TryGetProperty("password", out var passwordElement))
            {
                return;
            }

            var url = urlElement.GetString() ?? string.Empty;
            var username = usernameElement.GetString()?.Trim() ?? string.Empty;
            var password = passwordElement.GetString() ?? string.Empty;
            if (username.Length is 0 or > 512 || password.Length is 0 or > 1024)
            {
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return;
            }

            if (App.Services.Passwords.HasEquivalent(url, username, password))
            {
                return;
            }

            var promptKey = uri.Host + "|" + username;
            if (_passwordPromptOpen || _recentPasswordPrompts.Contains(promptKey))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(async () =>
            {
                _recentPasswordPrompts.Add(promptKey);
                await PromptSavePasswordAsync(url, username, password);
            });
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not process a password capture message.", ex);
        }
    }

    private async Task PromptSavePasswordAsync(string url, string username, string password)
    {
        if (_passwordPromptOpen)
        {
            return;
        }

        _passwordPromptOpen = true;
        try
        {
            var host = PasswordStore.NormalizeHost(url);
            var existing = App.Services.Passwords.ForHost(host)
                .FirstOrDefault(item => string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));

            var details = new StackPanel { Spacing = 5, Width = 360 };
            details.Children.Add(new TextBlock { Text = host, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            details.Children.Add(new TextBlock { Text = "用户名：" + username, FontSize = 12, Opacity = 0.75 });
            details.Children.Add(new TextBlock { Text = "密码：••••••••", FontSize = 12, Opacity = 0.75 });
            details.Children.Add(new TextBlock
            {
                Text = "密码只会加密保存在本机，不会上传。",
                FontSize = 11,
                Opacity = 0.55,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });

            var dialog = new ContentDialog
            {
                Title = existing is null ? "保存密码？" : "更新密码？",
                Content = details,
                PrimaryButtonText = existing is null ? "保存" : "更新",
                CloseButtonText = "忽略",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                App.Services.Passwords.SaveOrUpdate(url, username, password, existing);
                SetTransientStatus(existing is null ? "密码已保存到密码管理器" : "密码管理器中的密码已更新");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not show the password save prompt.", ex);
        }
        finally
        {
            _passwordPromptOpen = false;
        }
    }

    private async Task TryAutofillPasswordAsync(BrowserTab tab)
    {
        if (tab.IsPrivate ||
            !App.Services.Settings.PasswordAutofill ||
            tab.Core is null ||
            !Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri))
        {
            return;
        }

        var login = App.Services.Passwords.ForHost(uri.Host).FirstOrDefault();
        if (login is null)
        {
            return;
        }

        var password = App.Services.Passwords.GetPassword(login);
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var usernameJson = JsonSerializer.Serialize(login.Username);
        var passwordJson = JsonSerializer.Serialize(password);
        var script =
            "(function(){" +
            "const user=" + usernameJson + ";const pass=" + passwordJson + ";" +
            "const pw=document.querySelector('input[type=password]');" +
            "if(!pw||pw.value)return 'skip';" +
            "const form=pw.form||document;" +
            "const inputs=form.querySelectorAll('input[type=email],input[type=text],input[type=tel],input[autocomplete=username]');" +
            "let userInput=null;" +
            "for(const input of inputs){if(input!==pw&&!input.disabled&&!input.readOnly&&!input.value){userInput=input;break;}}" +
            "if(userInput){userInput.focus();userInput.value=user;userInput.dispatchEvent(new Event('input',{bubbles:true}));userInput.dispatchEvent(new Event('change',{bubbles:true}));}" +
            "pw.focus();pw.value=pass;pw.dispatchEvent(new Event('input',{bubbles:true}));pw.dispatchEvent(new Event('change',{bubbles:true}));" +
            "return 'filled';})();";

        try
        {
            var result = await tab.Core.ExecuteScriptAsync(script);
            if (result.Contains("filled", StringComparison.Ordinal) &&
                ReferenceEquals(tab, _activeTab))
            {
                SetTransientStatus($"已自动填充 {login.Site} 的登录信息");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not autofill a saved login.", ex);
        }
    }

    private async Task FillLoginAsync(SavedLogin login)
    {
        if (_activeTab?.Core is null)
        {
            SetTransientStatus("请先打开一个网页再填充密码。");
            return;
        }

        var password = App.Services.Passwords.GetPassword(login);
        if (string.IsNullOrEmpty(password))
        {
            SetTransientStatus("无法解密这条密码。");
            return;
        }

        var usernameJson = JsonSerializer.Serialize(login.Username);
        var passwordJson = JsonSerializer.Serialize(password);
        var script =
            "(function(){" +
            "const user=" + usernameJson + ";const pass=" + passwordJson + ";" +
            "const pw=document.querySelector('input[type=password]');" +
            "if(!pw)return 'no-password';" +
            "const form=pw.form||document;" +
            "const inputs=form.querySelectorAll('input[type=email],input[type=text],input[type=tel],input[autocomplete=username]');" +
            "let userInput=null;" +
            "for(const input of inputs){if(input!==pw&&!input.disabled&&!input.readOnly){userInput=input;break;}}" +
            "if(userInput){userInput.focus();userInput.value=user;userInput.dispatchEvent(new Event('input',{bubbles:true}));userInput.dispatchEvent(new Event('change',{bubbles:true}));}" +
            "pw.focus();pw.value=pass;pw.dispatchEvent(new Event('input',{bubbles:true}));pw.dispatchEvent(new Event('change',{bubbles:true}));" +
            "return 'filled';})();";

        try
        {
            var result = await _activeTab.Core.ExecuteScriptAsync(script);
            if (result.Contains("filled", StringComparison.Ordinal))
            {
                SetTransientStatus($"已填充 {login.Site} 的登录信息");
            }
            else
            {
                SetTransientStatus("当前页面没有找到密码输入框。");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not fill a saved login.", ex);
            SetTransientStatus("填充密码失败：" + ex.Message);
        }
    }
}
