using System.Text.Json;
using MicaBrowser.Models;

namespace MicaBrowser.Services;

public sealed class SettingsService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Settings { get; private set; } = new();

    public SettingsService(string path)
    {
        _path = path;
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, _json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read settings.json; using defaults.", ex);
            Settings = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(Settings.UiFontFamily) ||
            string.Equals(Settings.UiFontFamily, "MiSans", StringComparison.OrdinalIgnoreCase))
        {
            Settings.UiFontFamily = "Microsoft YaHei UI";
        }
        Settings.AdBlockAllowlist ??= new List<string>();
        Settings.PermissionRules ??= new Dictionary<string, string>();
        Settings.LastSession ??= new List<MicaBrowser.Models.SessionEntry>();
        if (Settings.MicaMaterial is "LiquidGlass" or "Highlight")
        {
            Settings.MicaMaterial = "Rainbow";
        }
        if (Settings.StartupBehavior == "Continue")
        {
            // Migrate the old combined startup option to the explicit restore
            // switch introduced in 1.10.
            Settings.StartupBehavior = "NewTab";
            Settings.RestoreTabs = true;
        }
    }

    public void Save()
    {
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Settings, _json));
            File.Move(temp, _path, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save settings.json.", ex);
        }
    }
}
