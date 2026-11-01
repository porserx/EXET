using System.Collections.ObjectModel;
using System.Text.Json;
using MicaBrowser.Models;
using Microsoft.Web.WebView2.Core;

namespace MicaBrowser.Services;

public sealed class ExtensionService
{
    private readonly AppPaths _paths;
    private readonly string _metaFile;
    private readonly Dictionary<string, ExtensionMeta> _meta = new(StringComparer.OrdinalIgnoreCase);

    public ExtensionService(AppPaths paths)
    {
        _paths = paths;
        _metaFile = Path.Combine(paths.Root, "extensions-meta.json");
        LoadMeta();
    }

    public CoreWebView2Profile? Profile { get; private set; }
    public ObservableCollection<ExtensionInfo> Items { get; } = new();
    public string SampleExtensionPath => _paths.SampleExtension;
    public bool IsAvailable => Profile is not null;

    public event EventHandler? Changed;
    public event EventHandler? Mutated;

    public void AttachProfile(CoreWebView2Profile profile)
    {
        if (ReferenceEquals(Profile, profile))
        {
            return;
        }

        Profile = profile;
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (Profile is null)
        {
            return;
        }

        try
        {
            var extensions = await Profile.GetBrowserExtensionsAsync();
            var list = extensions
                .Where(extension => !extension.Name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Items.Clear();
            foreach (var extension in list)
            {
                _meta.TryGetValue(extension.Id, out var meta);
                var (popup, icon) = ReadManifest(meta?.Folder, meta?.Popup, meta?.Icon);
                Items.Add(new ExtensionInfo(extension, meta?.Folder, popup, icon));
            }
            Logger.Info($"Loaded {list.Count} browser extension(s): " +
                string.Join(", ", list.Select(item => item.Name + "=" + item.Id)));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not enumerate browser extensions.", ex);
        }
    }

    public async Task<ExtensionInfo?> AddFromFolderAsync(string folder)
    {
        if (Profile is null)
        {
            throw new InvalidOperationException("浏览器扩展环境尚未就绪。");
        }

        if (!File.Exists(Path.Combine(folder, "manifest.json")))
        {
            throw new FileNotFoundException("所选目录不包含 manifest.json。", Path.Combine(folder, "manifest.json"));
        }

        Logger.Info($"AddBrowserExtensionAsync: {folder}");
        var extension = await Profile.AddBrowserExtensionAsync(folder);
        Remember(extension.Id, folder);
        await RefreshAsync();
        Mutated?.Invoke(this, EventArgs.Empty);
        return Items.FirstOrDefault(
            item => string.Equals(item.Id, extension.Id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SetEnabledAsync(ExtensionInfo info, bool enabled)
    {
        try
        {
            await info.Extension.EnableAsync(enabled);
            info.IsEnabled = enabled;
            Changed?.Invoke(this, EventArgs.Empty);
            Mutated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not change extension state for {info.Id}.", ex);
            throw;
        }
    }

    public async Task RemoveAsync(ExtensionInfo info)
    {
        try
        {
            await info.Extension.RemoveAsync();
            Items.Remove(info);
            _meta.Remove(info.Id);
            SaveMeta();
            Changed?.Invoke(this, EventArgs.Empty);
            Mutated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not remove extension {info.Id}.", ex);
            throw;
        }
    }

    public async Task<ExtensionInfo?> InstallSampleAsync()
    {
        if (!Directory.Exists(SampleExtensionPath))
        {
            throw new DirectoryNotFoundException("内置示例扩展目录不存在。");
        }

        return await AddFromFolderAsync(SampleExtensionPath);
    }

    private sealed class ExtensionMeta
    {
        public string Folder { get; set; } = string.Empty;
        public string Popup { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    private void LoadMeta()
    {
        try
        {
            if (!File.Exists(_metaFile)) return;
            var map = JsonSerializer.Deserialize<Dictionary<string, ExtensionMeta>>(File.ReadAllText(_metaFile));
            if (map is null) return;
            foreach (var (id, meta) in map) _meta[id] = meta;
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not load extension metadata.", ex);
        }
    }

    private void SaveMeta()
    {
        try
        {
            File.WriteAllText(_metaFile, JsonSerializer.Serialize(_meta,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not save extension metadata.", ex);
        }
    }

    private void Remember(string id, string folder)
    {
        var (popup, icon) = ReadManifest(folder, null, null);
        _meta[id] = new ExtensionMeta { Folder = folder, Popup = popup, Icon = icon };
        SaveMeta();
    }

    private static (string Popup, string Icon) ReadManifest(string? folder, string? knownPopup, string? knownIcon)
    {
        if (string.IsNullOrWhiteSpace(folder)) return (knownPopup ?? "", knownIcon ?? "");
        try
        {
            var manifest = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifest)) return (knownPopup ?? "", knownIcon ?? "");
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            var root = doc.RootElement;
            var popup = knownPopup ?? "";
            if (popup.Length == 0)
            {
                foreach (var key in new[] { "action", "browser_action", "page_action" })
                {
                    if (root.TryGetProperty(key, out var action) &&
                        action.TryGetProperty("default_popup", out var value))
                    {
                        popup = value.GetString() ?? "";
                        break;
                    }
                }
            }
            var icon = knownIcon ?? "";
            if (icon.Length == 0 && root.TryGetProperty("icons", out var icons))
            {
                var best = 0;
                foreach (var item in icons.EnumerateObject())
                {
                    if (int.TryParse(item.Name, out var size) && size >= best)
                    {
                        best = size;
                        icon = item.Value.GetString() ?? "";
                    }
                }
            }
            return (popup, icon);
        }
        catch
        {
            return (knownPopup ?? "", knownIcon ?? "");
        }
    }
}
