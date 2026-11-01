using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MicaBrowser.Backdrop;

/// <summary>One acrylic surface shared by every secondary window and menu.</summary>
public static class AppSurface
{
    public static bool IsDark(bool nightMode, string themeMode, ElementTheme requestedTheme)
    {
        if (nightMode || themeMode == "Dark")
        {
            return true;
        }

        if (themeMode == "Light")
        {
            return false;
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark;
    }

    public static Brush CreatePopup(bool dark, string material, Color acrylicTint) => material switch
    {
        "Acrylic" or "Blur" => new AcrylicBrush
        {
            TintColor = acrylicTint,
            TintOpacity = 0.5,
            TintLuminosityOpacity = dark ? 0.35 : 0.6,
            FallbackColor = dark
                ? Color.FromArgb(255, 30, 32, 38)
                : Color.FromArgb(255, 246, 247, 250)
        },
        "Smoke" => new AcrylicBrush
        {
            TintColor = dark ? Color.FromArgb(255, 28, 31, 38) : Color.FromArgb(255, 238, 242, 248),
            TintOpacity = 0.46,
            TintLuminosityOpacity = dark ? 0.4 : 0.55,
            FallbackColor = dark ? Color.FromArgb(255, 28, 31, 38) : Color.FromArgb(255, 238, 242, 248)
        },
        "Rainbow" => new AcrylicBrush
        {
            TintColor = dark ? Color.FromArgb(255, 58, 50, 92) : Color.FromArgb(255, 236, 232, 250),
            TintOpacity = 0.46,
            TintLuminosityOpacity = dark ? 0.4 : 0.55,
            FallbackColor = dark ? Color.FromArgb(255, 58, 50, 92) : Color.FromArgb(255, 236, 232, 250)
        },
        _ => new AcrylicBrush
        {
            TintColor = dark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243),
            TintOpacity = 0.18,
            TintLuminosityOpacity = 0.90,
            FallbackColor = dark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243)
        }
    };

    public static AcrylicBrush Create(bool dark) => new()
    {
        TintColor = dark
            ? Color.FromArgb(255, 28, 30, 36)
            : Color.FromArgb(255, 246, 247, 250),
        TintOpacity = 0.42,
        TintLuminosityOpacity = dark ? 0.42 : 0.56,
        FallbackColor = dark
            ? Color.FromArgb(255, 28, 30, 36)
            : Color.FromArgb(255, 246, 247, 250)
    };
}
