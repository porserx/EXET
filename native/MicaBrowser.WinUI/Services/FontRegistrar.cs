using System.Runtime.InteropServices;

namespace MicaBrowser.Services;

public static class FontRegistrar
{
    private const uint FrPrivate = 0x10;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);

    public static void RegisterBundledFonts()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.ttf")
                     .Where(path => path.EndsWith("MiSans-Regular.ttf", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith("MiSans-Semibold.ttf", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                AddFontResourceEx(file, FrPrivate, IntPtr.Zero);
            }
            catch
            {
                // A missing or malformed font must not prevent the browser
                // from starting; Windows falls back to its default UI font.
            }
        }
    }
}
