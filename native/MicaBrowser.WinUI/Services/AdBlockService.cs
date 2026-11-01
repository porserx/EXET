using System.Net.Http;
using System.Text;
using MicaBrowser.Helpers;
using MicaBrowser.Models;
using Microsoft.Web.WebView2.Core;

namespace MicaBrowser.Services;

public sealed class AdBlockService : ObservableObject
{
    private const string BaseFilterUrl = "https://filters.adtidy.org/extension/chromium/filters/2.txt";
    private const string ChineseFilterUrl = "https://filters.adtidy.org/extension/chromium/filters/224.txt";
    private const string BaseFilterName = "adguard-base.txt";
    private const string ChineseFilterName = "adguard-chinese.txt";

    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private AdBlockEngine? _engine;
    private int _sessionBlocked;
    private DateTime _lastStatsChange = DateTime.MinValue;
    private bool _isReady;
    private string _status = "AdGuard 规则准备中…";

    public AdBlockService(AppPaths paths, SettingsService settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public AppSettings Settings => _settings.Settings;

    public bool Enabled
    {
        get => Settings.AdBlockEnabled;
        set
        {
            if (Settings.AdBlockEnabled == value)
            {
                return;
            }
            Settings.AdBlockEnabled = value;
            _settings.Save();
            OnPropertyChanged();
            RaiseChanged();
        }
    }

    public bool IsReady
    {
        get => _isReady;
        private set => SetProperty(ref _isReady, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public int DomainRules => _engine?.Stats.DomainRules ?? 0;
    public int PathRules => _engine?.Stats.PathRules ?? 0;
    public int ExceptionRules => _engine?.Stats.ExceptionRules ?? 0;
    public int RuleCount => DomainRules + PathRules;
    public long SessionBlocked => _sessionBlocked;
    public long TotalBlocked => Settings.AdBlockTotalBlocked + _sessionBlocked;
    public string LastUpdate => string.IsNullOrWhiteSpace(Settings.LastFilterUpdate)
        ? "内置规则"
        : Settings.LastFilterUpdate;

    public event EventHandler? StatsChanged;
    public event EventHandler? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            EnsureFilterFiles();
            var engine = await Task.Run(() => AdBlockEngine.Load(GetFilterPaths()));
            _engine = engine;
            IsReady = true;
            Status = Enabled
                ? $"AdGuard 已启用 · {engine.Stats.DomainRules + engine.Stats.PathRules:N0} 条规则"
                : "AdGuard 已暂停";
            OnPropertyChanged(nameof(DomainRules));
            OnPropertyChanged(nameof(PathRules));
            OnPropertyChanged(nameof(ExceptionRules));
            OnPropertyChanged(nameof(RuleCount));
            RaiseChanged();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load AdGuard filters.", ex);
            Status = "AdGuard 规则载入失败";
        }
    }

    public bool IsSiteAllowed(Uri? pageUri)
    {
        if (pageUri is null || !pageUri.IsAbsoluteUri)
        {
            return false;
        }

        var host = pageUri.Host;
        return Settings.AdBlockAllowlist.Any(
            domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                      host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    public void SetSiteAllowed(Uri pageUri, bool allowed)
    {
        if (!pageUri.IsAbsoluteUri)
        {
            return;
        }

        var host = pageUri.Host;
        var existing = Settings.AdBlockAllowlist.FirstOrDefault(
            domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                      host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

        if (allowed)
        {
            if (existing is null)
            {
                Settings.AdBlockAllowlist.Add(host);
            }
        }
        else if (existing is not null)
        {
            Settings.AdBlockAllowlist.Remove(existing);
            if (!host.Equals(existing, StringComparison.OrdinalIgnoreCase))
            {
                Settings.AdBlockAllowlist.Remove(host);
            }
        }

        _settings.Save();
        OnPropertyChanged(nameof(Enabled));
        RaiseChanged();
    }

    public bool ShouldBlock(
        Uri requestUri,
        Uri? pageUri,
        CoreWebView2WebResourceContext resourceContext,
        out string reason)
    {
        reason = string.Empty;
        if (!Enabled || _engine is null || IsSiteAllowed(pageUri))
        {
            return false;
        }

        // Never block a top-level navigation; the user explicitly asked to go there.
        if (resourceContext == CoreWebView2WebResourceContext.Document)
        {
            return false;
        }

        if (!_engine.IsBlocked(requestUri, pageUri, out reason))
        {
            return false;
        }

        _sessionBlocked++;
        Settings.AdBlockTotalBlocked++;
        RaiseStatsThrottled();
        return true;
    }

    public async Task UpdateFiltersAsync(IProgress<string>? progress = null)
    {
        if (!IsReady)
        {
            progress?.Report("规则引擎尚未就绪");
            return;
        }

        var baseTemp = Path.Combine(_paths.Filters, BaseFilterName + ".new");
        var chineseTemp = Path.Combine(_paths.Filters, ChineseFilterName + ".new");

        try
        {
            progress?.Report("正在下载 AdGuard Base filter…");
            await DownloadFileAsync(BaseFilterUrl, baseTemp, progress);
            progress?.Report("正在下载 AdGuard Chinese filter…");
            await DownloadFileAsync(ChineseFilterUrl, chineseTemp, progress);

            var baseSize = new FileInfo(baseTemp).Length;
            var chineseSize = new FileInfo(chineseTemp).Length;
            if (baseSize < 100_000 || chineseSize < 50_000)
            {
                throw new InvalidDataException("下载的过滤规则文件不完整。");
            }

            progress?.Report("正在编译 AdGuard 规则…");
            var next = await Task.Run(() => AdBlockEngine.Load(new[] { baseTemp, chineseTemp }));
            if (next.Stats.DomainRules < 1000)
            {
                throw new InvalidDataException("过滤规则数量异常。");
            }

            File.Move(baseTemp, Path.Combine(_paths.Filters, BaseFilterName), true);
            File.Move(chineseTemp, Path.Combine(_paths.Filters, ChineseFilterName), true);
            _engine = next;

            Settings.LastFilterUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _settings.Save();
            Status = $"AdGuard 已更新 · {next.Stats.DomainRules + next.Stats.PathRules:N0} 条规则";
            OnPropertyChanged(nameof(DomainRules));
            OnPropertyChanged(nameof(PathRules));
            OnPropertyChanged(nameof(ExceptionRules));
            OnPropertyChanged(nameof(RuleCount));
            OnPropertyChanged(nameof(LastUpdate));
            progress?.Report("AdGuard 规则已更新");
            RaiseChanged();
        }
        catch (Exception ex)
        {
            Logger.Error("Filter update failed.", ex);
            progress?.Report("规则更新失败：" + ex.Message);
            SafeDelete(baseTemp);
            SafeDelete(chineseTemp);
        }
    }

    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(LastUpdate));
        OnPropertyChanged(nameof(TotalBlocked));
        RaiseChanged();
    }

    private void RaiseStatsThrottled()
    {
        if (DateTime.Now - _lastStatsChange < TimeSpan.FromMilliseconds(180))
        {
            return;
        }
        _lastStatsChange = DateTime.Now;
        StatsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        StatsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureFilterFiles()
    {
        CopyFilterIfNeeded(BaseFilterName);
        CopyFilterIfNeeded(ChineseFilterName);
    }

    private void CopyFilterIfNeeded(string fileName)
    {
        var target = Path.Combine(_paths.Filters, fileName);
        var source = Path.Combine(_paths.BundledFilters, fileName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Bundled AdGuard filter is missing.", source);
        }

        if (!File.Exists(target) || new FileInfo(target).Length < 50_000)
        {
            File.Copy(source, target, true);
        }
    }

    private string[] GetFilterPaths() => new[]
    {
        Path.Combine(_paths.Filters, BaseFilterName),
        Path.Combine(_paths.Filters, ChineseFilterName)
    };

    private static async Task DownloadFileAsync(string url, string destination, IProgress<string>? progress)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(90);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MicaBrowser/2.0 (+AdGuard filter updater)");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(destination);
        var buffer = new byte[128 * 1024];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            received += read;
            if (total is > 0)
            {
                progress?.Report($"下载过滤规则… {received * 100 / total.Value}%");
            }
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failure.
        }
    }
}
