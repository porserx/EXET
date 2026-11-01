namespace MicaBrowser.Services;

/// <summary>
/// The user-facing release version. It intentionally lives in version.txt so
/// the +0.05 release policy can be applied without changing assembly metadata.
/// </summary>
public static class AppVersion
{
    private const string ResourceName = "EXET.version.txt";
    private static string? _display;

    public static string Display => _display ??= ReadDisplay();

    private static string ReadDisplay()
    {
        try
        {
            using var stream = typeof(AppVersion).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return "1.0";
            }

            using var reader = new StreamReader(stream);
            var value = reader.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(value) ? "1.0" : value;
        }
        catch
        {
            return "1.0";
        }
    }
}
