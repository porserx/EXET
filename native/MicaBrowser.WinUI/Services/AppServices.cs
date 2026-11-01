using MicaBrowser.Models;

namespace MicaBrowser.Services;

public sealed class AppServices : IDisposable
{
    public AppPaths Paths { get; }
    public SettingsService SettingsStore { get; }
    public AppSettings Settings => SettingsStore.Settings;
    public HistoryService History { get; }
    public FavoritesService Favorites { get; }
    public BrowserEnvironmentService BrowserEnvironment { get; }
    public AdBlockService AdBlock { get; }
    public ExtensionService Extensions { get; }
    public PasswordStore Passwords { get; }
    public ClashService Clash { get; }

    public AppServices()
    {
        Paths = new AppPaths();
        Logger.Initialize(Paths.Logs);
        SettingsStore = new SettingsService(Paths.SettingsFile);
        History = new HistoryService(Paths.HistoryFile);
        Favorites = new FavoritesService(Paths.FavoritesFile);
        BrowserEnvironment = new BrowserEnvironmentService(Paths, SettingsStore);
        AdBlock = new AdBlockService(Paths, SettingsStore);
        Extensions = new ExtensionService(Paths);
        Passwords = new PasswordStore(Paths.PasswordsFile);
        Clash = new ClashService();
    }

    public void LoadState()
    {
        Logger.Info("EXET starting.");
        SettingsStore.Load();
        DefaultBrowserService.EnsureRegistered();
        History.Load();
        Favorites.Load();
        Passwords.Load();
    }

    public async Task InitializeEnvironmentAsync()
    {
        try
        {
            if (Settings.ClashEnabled)
            {
                try { await Clash.StartAsync(Settings); }
                catch (Exception ex) { Logger.Warn("Clash auto-start failed.", ex); }
            }
            await BrowserEnvironment.EnsureInitializedAsync();
            await AdBlock.InitializeAsync();
            Logger.Info("EXET environment initialized.");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not initialize the browser environment.", ex);
        }
    }

    public async Task InitializeAsync()
    {
        LoadState();
        await InitializeEnvironmentAsync();
    }

    public void Dispose()
    {
        try
        {
            SettingsStore.Save();
            History.Save();
            Favorites.Save();
            Passwords.Save();
            Clash.Dispose();
            BrowserEnvironment.ClearPrivateProfile();
            Logger.Info("EXET shutting down.");
        }
        catch
        {
            // Best effort during shutdown.
        }
    }
}
