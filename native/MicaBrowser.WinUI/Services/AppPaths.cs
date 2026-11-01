namespace MicaBrowser.Services;

public sealed class AppPaths
{
    public string Root { get; }
    public string WebViewData { get; }
    public string Filters { get; }
    public string Logs { get; }
    public string Extensions { get; }
    public string BundledAssets { get; }
    public string BundledFilters { get; }
    public string SampleExtension { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string HistoryFile => Path.Combine(Root, "history.json");
    public string FavoritesFile => Path.Combine(Root, "favorites.json");
    public string PasswordsFile => Path.Combine(Root, "passwords.json");
    public string PdfCache { get; }
    public string DownloadsFolder { get; }

    public AppPaths()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("EXET_DATA_DIR")
            ?? Environment.GetEnvironmentVariable("MICA_DATA_DIR");
        Root = !string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.GetFullPath(overrideRoot)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EXET");
        WebViewData = Path.Combine(Root, "WebView2");
        Filters = Path.Combine(Root, "Filters");
        Logs = Path.Combine(Root, "Logs");
        Extensions = Path.Combine(Root, "Extensions");
        PdfCache = Path.Combine(Root, "PdfCache");
        BundledAssets = Path.Combine(AppContext.BaseDirectory, "Assets");
        BundledFilters = Path.Combine(BundledAssets, "Filters");
        SampleExtension = Path.Combine(AppContext.BaseDirectory, "Extensions", "EXETSample");
        DownloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(WebViewData);
        Directory.CreateDirectory(Filters);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Extensions);
        Directory.CreateDirectory(PdfCache);
        Directory.CreateDirectory(DownloadsFolder);
    }
}
