using System.Collections.Concurrent;
using System.Text.Json;
using MicaBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MicaBrowser;

public sealed partial class MainWindow
{
    private void ProxyButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout { MenuFlyoutPresenterStyle = CreateMaterialMenuStyle() };
        var settings = App.Services.Settings;
        void AddMode(string label, string mode)
        {
            var item = new MenuFlyoutItem
            {
                Text = (settings.ProxyMode == mode ? "● " : "") + label
            };
            item.Click += (_, _) =>
            {
                settings.ProxyMode = mode;
                App.Services.SettingsStore.Save();
                SetTransientStatus("代理已切换，重启 EXET 后生效");
            };
            flyout.Items.Add(item);
        }
        AddMode("跟随系统代理", "System");
        AddMode("直连（不使用代理）", "Direct");
        AddMode("手动代理", "Manual");
        AddMode("Clash Meta（Hysteria2）", "Clash");
        flyout.Items.Add(new MenuFlyoutSeparator());
        var more = new MenuFlyoutItem { Text = "代理设置…" };
        more.Click += (_, _) => OpenSettings("general");
        flyout.Items.Add(more);
        flyout.ShowAt((FrameworkElement)sender);
    }

    private void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.Core is null || _activeTab.IsSettings)
        {
            SetTransientStatus("请先打开一个网页");
            return;
        }

        ShowTranslateMenu((FrameworkElement)sender);
    }

    private static string TranslationLanguageLabel(string code) => code switch
    {
        "zh-CN" => "中文",
        "en" => "English",
        "ja" => "日本語",
        "ko" => "한국어",
        "fr" => "Français",
        "de" => "Deutsch",
        "es" => "Español",
        "ru" => "Русский",
        "ar" => "العربية",
        "pt" => "Português",
        "it" => "Italiano",
        _ => code
    };

    private void UpdateTranslateButtonTooltip()
    {
        if (_translateButton is null) return;
        var label = TranslationLanguageLabel(App.Services.Settings.TranslationTarget);
        ToolTipService.SetToolTip(_translateButton, "翻译网页（目标：" + label + "）");
    }

    private void ShowTranslateMenu(FrameworkElement anchor)
    {
        var settings = App.Services.Settings;
        var flyout = new MenuFlyout { MenuFlyoutPresenterStyle = CreateMaterialMenuStyle() };

        var languages = new (string Code, string Label)[]
        {
            ("zh-CN", "中文（简体）"),
            ("en", "English"),
            ("ja", "日本語"),
            ("ko", "한국어"),
            ("fr", "Français"),
            ("de", "Deutsch"),
            ("es", "Español"),
            ("ru", "Русский"),
            ("ar", "العربية"),
            ("pt", "Português"),
            ("it", "Italiano")
        };

        var smart = new MenuFlyoutItem
        {
            Text = (settings.SmartTranslateEnabled ? "● " : "") + "智能翻译（自动翻译外语网页）"
        };
        smart.Click += (_, _) =>
        {
            settings.SmartTranslateEnabled = !settings.SmartTranslateEnabled;
            App.Services.SettingsStore.Save();
            SetTransientStatus(settings.SmartTranslateEnabled
                ? "智能翻译：已开启"
                : "智能翻译：已关闭", 4);
        };
        flyout.Items.Add(smart);
        flyout.Items.Add(new MenuFlyoutSeparator());

        var targetHeader = new MenuFlyoutItem { Text = "翻译为", IsEnabled = false };
        flyout.Items.Add(targetHeader);
        foreach (var (code, label) in languages)
        {
            var mark = string.Equals(settings.TranslationTarget, code, StringComparison.OrdinalIgnoreCase) ? "● " : "";
            var item = new MenuFlyoutItem { Text = mark + label };
            item.Click += async (_, _) =>
            {
                await TranslatePageAsync(code, label);
            };
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var showOriginal = new MenuFlyoutItem { Text = "显示原文", Icon = new FontIcon { Glyph = "\uE7A7" } };
        showOriginal.Click += async (_, _) => await ShowOriginalAsync(true);
        flyout.Items.Add(showOriginal);

        var showTranslation = new MenuFlyoutItem { Text = "显示译文", Icon = new FontIcon { Glyph = "\uE8C1" } };
        showTranslation.Click += async (_, _) => await ShowOriginalAsync(false);
        flyout.Items.Add(showTranslation);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var source = new MenuFlyoutSubItem { Text = "源语言" };
        var auto = new MenuFlyoutItem
        {
            Text = (settings.TranslationSourceLanguage == "auto" ? "● " : "") + "自动识别语言"
        };
        auto.Click += (_, _) =>
        {
            settings.TranslationSourceLanguage = "auto";
            App.Services.SettingsStore.Save();
            SetTransientStatus("源语言：自动识别");
        };
        source.Items.Add(auto);
        foreach (var (code, label) in languages)
        {
            var mark = string.Equals(settings.TranslationSourceLanguage, code, StringComparison.OrdinalIgnoreCase) ? "● " : "";
            var item = new MenuFlyoutItem { Text = mark + label };
            item.Click += (_, _) =>
            {
                settings.TranslationSourceLanguage = code;
                App.Services.SettingsStore.Save();
                SetTransientStatus("源语言：" + label);
            };
            source.Items.Add(item);
        }
        flyout.Items.Add(source);

        var engine = new MenuFlyoutSubItem { Text = "翻译引擎" };
        var deepSeek = new MenuFlyoutItem
        {
            Text = (settings.TranslationProvider == "DeepSeek" ? "● " : "") + "DeepSeek API"
        };
        deepSeek.Click += (_, _) =>
        {
            settings.TranslationProvider = "DeepSeek";
            App.Services.SettingsStore.Save();
            SetTransientStatus("翻译引擎：DeepSeek");
        };
        var bing = new MenuFlyoutItem
        {
            Text = (settings.TranslationProvider == "Bing" ? "● " : "") + "Bing 翻译"
        };
        bing.Click += (_, _) =>
        {
            settings.TranslationProvider = "Bing";
            App.Services.SettingsStore.Save();
            SetTransientStatus("翻译引擎：Bing");
        };
        var libre = new MenuFlyoutItem
        {
            Text = (settings.TranslationProvider == "LibreTranslate" ? "● " : "") + "LibreTranslate（自建）"
        };
        libre.Click += (_, _) =>
        {
            settings.TranslationProvider = "LibreTranslate";
            App.Services.SettingsStore.Save();
            SetTransientStatus("翻译引擎：LibreTranslate");
        };
        engine.Items.Add(deepSeek);
        engine.Items.Add(bing);
        engine.Items.Add(libre);
        flyout.Items.Add(engine);

        var apiKey = new MenuFlyoutItem { Text = "DeepSeek API Key…" };
        apiKey.Click += async (_, _) => await ShowDeepSeekKeyDialogAsync();
        flyout.Items.Add(apiKey);

        var libreConfig = new MenuFlyoutItem { Text = "LibreTranslate 地址…" };
        libreConfig.Click += async (_, _) => await ShowLibreTranslateDialogAsync();
        flyout.Items.Add(libreConfig);

        flyout.ShowAt(anchor);
    }

    private async Task ShowLibreTranslateDialogAsync()
    {
        var settings = App.Services.Settings;
        var url = new TextBox
        {
            Header = "服务地址",
            Text = string.IsNullOrWhiteSpace(settings.LibreTranslateUrl)
                ? "http://127.0.0.1:5000"
                : settings.LibreTranslateUrl,
            PlaceholderText = "http://127.0.0.1:5000"
        };
        var key = new PasswordBox
        {
            Header = "API Key（可选）",
            Password = settings.LibreTranslateApiKey ?? string.Empty
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "填入自建 LibreTranslate 地址，程序会请求 /translate。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        });
        panel.Children.Add(url);
        panel.Children.Add(key);
        var dialog = new ContentDialog
        {
            Title = "LibreTranslate",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            settings.LibreTranslateUrl = url.Text.Trim();
            settings.LibreTranslateApiKey = key.Password.Trim();
            App.Services.SettingsStore.Save();
            SetTransientStatus("LibreTranslate 配置已保存", 4);
        }
    }

    private async Task ShowDeepSeekKeyDialogAsync()
    {
        var settings = App.Services.Settings;
        var box = new PasswordBox
        {
            Password = settings.DeepSeekApiKey ?? string.Empty,
            PlaceholderText = "sk-…"
        };
        var dialog = new ContentDialog
        {
            Title = "DeepSeek API Key",
            Content = box,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            settings.DeepSeekApiKey = box.Password.Trim();
            App.Services.SettingsStore.Save();
            SetTransientStatus(settings.DeepSeekApiKey.Length > 0
                ? "DeepSeek API Key 已保存"
                : "DeepSeek API Key 已清除", 4);
        }
    }

    private async Task TranslatePageAsync(string target, string targetLabel, bool retry = true)
    {
        var tab = _activeTab;
        if (tab?.Core is null)
        {
            SetTransientStatus("请先打开一个网页");
            return;
        }

        var settings = App.Services.Settings;
        settings.TranslationTarget = target;
        App.Services.SettingsStore.Save();
        Logger.Info($"Translation target saved: {target}");
        UpdateTranslateButtonTooltip();
        var source = string.IsNullOrWhiteSpace(settings.TranslationSourceLanguage)
            ? "auto"
            : settings.TranslationSourceLanguage;
        Logger.Info($"Translation start: {target}, source={source}");
        SetTransientStatus($"正在提取网页文字，准备翻译为{targetLabel}…");

        const string collectScript = """
            (function () {
              if (!document.body) return "[]";
              if (window.__exetNodes && window.__exetNodes.length && window.__exetOriginal &&
                  window.__exetNodes.every(function (node) { return node.isConnected; }))
                return JSON.stringify(window.__exetOriginal);
              const skip = /^(SCRIPT|STYLE|NOSCRIPT|TEXTAREA|CODE|PRE|SVG|CANVAS)$/i;
              const nodes = [];
              const originals = [];
              const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
                acceptNode: function (node) {
                  const text = node.nodeValue;
                  if (!text || !text.trim()) return NodeFilter.FILTER_REJECT;
                  const parent = node.parentElement;
                  if (!parent || skip.test(parent.tagName)) return NodeFilter.FILTER_REJECT;
                  return NodeFilter.FILTER_ACCEPT;
                }
              });
              while (walker.nextNode() && nodes.length < 500) {
                const node = walker.currentNode;
                nodes.push(node);
                originals.push(node.nodeValue);
              }
              window.__exetNodes = nodes;
              window.__exetOriginal = originals;
              window.__exetTranslated = [];
              window.__exetShowingOriginal = false;
              return JSON.stringify(originals);
            })();
            """;

        try
        {
            var raw = await tab.Core.ExecuteScriptAsync(collectScript);
            Logger.Info($"Translation collect raw: {(raw?.Length ?? 0)} chars");
            var json = JsonSerializer.Deserialize<string>(raw) ?? "[]";
            var texts = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            if (texts.Count == 0)
            {
                SetTransientStatus("没有找到可翻译文字（可能是 iframe、图片或脚本生成内容）", 6);
                return;
            }

            const int batchSize = 40;
            var cache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            var batches = new List<(int Start, List<string> Texts)>();
            for (var start = 0; start < texts.Count; start += batchSize)
            {
                batches.Add((start, texts.Skip(start).Take(batchSize).ToList()));
            }

            var done = 0;
            var appliedTotal = 0;
            var changedTotal = 0;
            var concurrency = settings.TranslationProvider == "DeepSeek"
                ? 3
                : settings.TranslationProvider == "LibreTranslate" ? 4 : 2;
            using var gate = new SemaphoreSlim(concurrency);
            var tasks = batches.Select(async batch =>
            {
                await gate.WaitAsync();
                try
                {
                    var unique = batch.Texts.Where(text => !cache.ContainsKey(text)).Distinct().ToList();
                    if (unique.Count > 0)
                    {
                        var translated = await TranslationService.TranslateBatchAsync(settings, unique, target, source);
                        for (var i = 0; i < unique.Count && i < translated.Count; i++)
                        {
                            cache[unique[i]] = translated[i];
                        }
                    }

                    var values = JsonSerializer.Serialize(
                        batch.Texts.Select(text => cache.TryGetValue(text, out var translated) ? translated : text).ToList());
                    var applyScript =
                        "(function(){const v=" + values +
                        ";const n=window.__exetNodes.slice(" + batch.Start + "," + (batch.Start + batch.Texts.Count) +
                        ");let applied=0,changed=0;for(let i=0;i<n.length&&i<v.length;i++){if(!n[i].isConnected)continue;applied++;if(n[i].nodeValue!==v[i])changed++;n[i].__exetTranslated=v[i];n[i].nodeValue=v[i];}" +
                        "return JSON.stringify({applied:applied,changed:changed});})()";
                    var applyRaw = await tab.Core.ExecuteScriptAsync(applyScript);
                    var applyJson = JsonSerializer.Deserialize<string>(applyRaw) ?? "{}";
                    using (var applyDoc = JsonDocument.Parse(applyJson))
                    {
                        var applied = applyDoc.RootElement.TryGetProperty("applied", out var a) ? a.GetInt32() : 0;
                        var changed = applyDoc.RootElement.TryGetProperty("changed", out var c) ? c.GetInt32() : 0;
                        Interlocked.Add(ref appliedTotal, applied);
                        Interlocked.Add(ref changedTotal, changed);
                    }
                    await tab.Core.ExecuteScriptAsync("window.__exetShowingOriginal=false");
                    Interlocked.Add(ref done, batch.Texts.Count);
                    SetTransientStatus($"正在翻译 {done}/{texts.Count}…");
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
            Logger.Info($"Translation done: requested={done}, applied={appliedTotal}, changed={changedTotal}, target={target}");
            if (changedTotal == 0)
            {
                if (retry)
                {
                    SetTransientStatus("页面内容已变化，正在重新提取并翻译…", 4);
                    await Task.Delay(500);
                    await TranslatePageAsync(target, targetLabel, retry: false);
                    return;
                }
                SetTransientStatus("翻译服务没有返回可显示的结果，请稍后重试或切换翻译引擎", 10);
            }
            else
            {
                var suffix = changedTotal < texts.Count ? $"（部分完成 {changedTotal}/{texts.Count}）" : "";
                SetTransientStatus($"翻译完成：{changedTotal} 段 → {targetLabel}{suffix}", 8);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Translation failed.", ex);
            SetTransientStatus("翻译失败：" + ex.Message, 10);
        }
    }

    private async Task ShowOriginalAsync(bool showOriginal)
    {
        var tab = _activeTab;
        if (tab?.Core is null)
        {
            SetTransientStatus("请先打开一个网页");
            return;
        }

        try
        {
            var script =
                "(function(show){const n=window.__exetNodes||[];const o=window.__exetOriginal||[];" +
                "for(let i=0;i<n.length;i++){const value=show?o[i]:(n[i].__exetTranslated||o[i]);" +
                "if(value!==undefined)n[i].nodeValue=value;}window.__exetShowingOriginal=show;return n.length;})( " +
                (showOriginal ? "true" : "false") + " )";
            var raw = await tab.Core.ExecuteScriptAsync(script);
            var count = int.TryParse(raw, out var value) ? value : 0;
            SetTransientStatus(showOriginal
                ? $"已显示原文（{count} 段）"
                : $"已显示译文（{count} 段）", 5);
        }
        catch (Exception ex)
        {
            Logger.Warn("Show original failed.", ex);
            SetTransientStatus("切换原文失败：" + ex.Message, 8);
        }
    }
}
