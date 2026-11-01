using MicaBrowser.Models;
using MicaBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace MicaBrowser;

public sealed partial class MainWindow
{
    public void ApplyNightModeToWebViews()
    {
        foreach (var tab in Tabs)
        {
            if (tab.Core is not null)
            {
                ApplyCoreColorScheme(tab.Core);
            }
        }
    }

    public void ApplyCoreColorScheme(CoreWebView2 core)
    {
        try
        {
            if (core.Profile is null)
            {
                return;
            }

            core.Profile.PreferredColorScheme = App.Services.Settings.NightMode
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Auto;
            ApplyPageDarkMode(core);
        }
        catch
        {
            // Older runtimes may not expose the profile color scheme.
        }
    }

    private static readonly string ForceDarkStyleScript = """
        (() => {
            const id = 'exet-force-dark-style';
            const old = document.getElementById(id);
            if (old) old.remove();

            function luminance(value) {
                const match = value && value.match(/rgba?\((\d+)[,\s]+(\d+)[,\s]+(\d+)/i);
                if (!match) return -1;
                const r = Number(match[1]) / 255;
                const g = Number(match[2]) / 255;
                const b = Number(match[3]) / 255;
                return 0.2126 * r + 0.7152 * g + 0.0722 * b;
            }

            function apply() {
                try {
                    const body = getComputedStyle(document.body).backgroundColor;
                    const html = getComputedStyle(document.documentElement).backgroundColor;
                    const bodyLum = luminance(body);
                    const htmlLum = luminance(html);
                    const pageLum = Math.max(bodyLum, htmlLum);
                    // Sites that already render a dark background keep their
                    // own theme. Only invert pages that are still light.
                    if (pageLum >= 0 && pageLum < 0.45) return;
                    const declaredScheme = (
                        getComputedStyle(document.documentElement).colorScheme || '') +
                        (getComputedStyle(document.body).colorScheme || '');
                    if (declaredScheme.toLowerCase().includes('dark')) return;
                    const style = document.createElement('style');
                    style.id = id;
                    style.textContent = `
                        html { background: #fff !important; filter: invert(0.92) hue-rotate(180deg) !important; }
                        html, body { color-scheme: dark !important; }
                        img, video, canvas, svg, picture, iframe,
                        [style*="background-image"] {
                            filter: invert(1) hue-rotate(180deg) !important;
                        }
                    `;
                    (document.head || document.documentElement).appendChild(style);
                } catch (_) { }
            }

            if (document.readyState === 'complete') setTimeout(apply, 120);
            else window.addEventListener('load', () => setTimeout(apply, 120), { once: true });
        })();
        """;

    private static readonly string RemoveForceDarkStyleScript = """
        (() => {
            const style = document.getElementById('exet-force-dark-style');
            if (style) style.remove();
        })();
        """;

    private void ApplyPageDarkMode(CoreWebView2 core)
    {
        try
        {
            var enabled = App.Services.Settings.NightMode && App.Services.Settings.ForceDarkPages;
            var script = enabled ? ForceDarkStyleScript : RemoveForceDarkStyleScript;
            _ = core.ExecuteScriptAsync(script);
        }
        catch
        {
            // Some pages reject script injection; the preferred color scheme
            // still applies where the site supports it.
        }
    }

    private async Task SuspendInactiveTabsAsync()
    {
        if (!App.Services.Settings.SuspendBackgroundTabs)
        {
            return;
        }

        foreach (var tab in Tabs.ToList())
        {
            var core = tab.Core;
            if (core is null ||
                ReferenceEquals(tab, _activeTab) ||
                tab.IsSuspended ||
                core.IsDocumentPlayingAudio)
            {
                continue;
            }

            try
            {
                if (await core.TrySuspendAsync())
                {
                    tab.IsSuspended = true;
                }
            }
            catch
            {
                // A page with an active dialog or devtools may reject suspension.
            }
        }
    }

    private void CreatePrivateTab() => CreateTab(null, true, true);

    private void ToggleNightMode()
    {
        var settings = App.Services.Settings;
        settings.NightMode = !settings.NightMode;
        App.Services.SettingsStore.Save();
        ApplyAppearance();
    }

    private static string BuildWebFontScript()
    {
        var settings = App.Services.Settings;
        var family = string.IsNullOrWhiteSpace(settings.UiFontFamily)
            ? DefaultUiFontFamily
            : settings.UiFontFamily.Trim();
        var weight = Math.Clamp(settings.UiFontWeight, 100, 900);
        var useBundledMiSans = string.Equals(family, "MiSans", StringComparison.OrdinalIgnoreCase);
        var familyJson = System.Text.Json.JsonSerializer.Serialize(family);

        var fontFaces = useBundledMiSans
            ? """
              @font-face{font-family:'EXET MiSans';src:url('https://exet-fonts.local/MiSans-Light.ttf') format('truetype');font-weight:300;font-style:normal;font-display:swap;}
              @font-face{font-family:'EXET MiSans';src:url('https://exet-fonts.local/MiSans-Regular.ttf') format('truetype');font-weight:400;font-style:normal;font-display:swap;}
              @font-face{font-family:'EXET MiSans';src:url('https://exet-fonts.local/MiSans-Medium.ttf') format('truetype');font-weight:500;font-style:normal;font-display:swap;}
              @font-face{font-family:'EXET MiSans';src:url('https://exet-fonts.local/MiSans-Semibold.ttf') format('truetype');font-weight:600;font-style:normal;font-display:swap;}
              @font-face{font-family:'EXET MiSans';src:url('https://exet-fonts.local/MiSans-Bold.ttf') format('truetype');font-weight:700;font-style:normal;font-display:swap;}
              """
            : string.Empty;

        var primary = useBundledMiSans ? "'EXET MiSans'" : familyJson;
        return $$"""
            (() => {
                const id = 'exet-web-font';
                let style = document.getElementById(id);
                if (!style) {
                    style = document.createElement('style');
                    style.id = id;
                    (document.head || document.documentElement).appendChild(style);
                }
                style.textContent = {{System.Text.Json.JsonSerializer.Serialize(fontFaces)}} + `
                    html, body { font-family: {{primary}}, "Microsoft YaHei UI", "Segoe UI", sans-serif !important; font-weight: {{weight}} !important; }
                    body :not(i):not(svg):not(code):not(pre):not(kbd):not(samp):not([class*="icon"]):not([class*="fa"]):not([class*="material-icons"]) {
                        font-family: inherit !important;
                    }
                `;
            })();
            """;
    }

    public void ApplyWebFontToWebViews()
    {
        var script = BuildWebFontScript();
        foreach (var tab in Tabs)
        {
            if (tab.Core is null)
            {
                continue;
            }

            ApplyWebFontToCore(tab.Core, script);
            _ = tab.Core.ExecuteScriptAsync(script);
        }
    }

    public static void ApplyWebFontToCore(CoreWebView2 core, string? script = null)
    {
        try
        {
            var fontDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            if (Directory.Exists(fontDirectory))
            {
                core.SetVirtualHostNameToFolderMapping(
                    "exet-fonts.local",
                    fontDirectory,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            _ = core.AddScriptToExecuteOnDocumentCreatedAsync(script ?? BuildWebFontScript());
        }
        catch
        {
            // Font injection is best effort; pages still render with the
            // browser's normal fallback fonts.
        }
    }
}
