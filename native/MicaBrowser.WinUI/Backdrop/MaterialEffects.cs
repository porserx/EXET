using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MicaBrowser.Backdrop;

/// <summary>
/// Optional color layers for the acrylic-backed materials. Mica, Mica Alt,
/// Acrylic and Blur themselves are rendered by a system backdrop controller;
/// only Smoke and Rainbow add a visible color layer.
/// </summary>
public static class MaterialEffects
{
    public static bool SupportsTransparency(string effect) => effect is "Mica" or "Alt" or "Acrylic" or "Rainbow";

    public static bool SupportsThemeSwitch(string effect) =>
        effect is "Mica" or "Alt" or "Acrylic" or "Blur";

    public static Brush? CreateOverlay(string effect, bool dark)
    {
        return effect switch
        {
            "Smoke" => CreateSmoke(dark),
            "Rainbow" => CreateRainbow(dark),
            _ => null
        };
    }

    public static (float Tint, float Luminosity, double Overlay) GetOpacity(
        string effect,
        int transparency)
    {
        var t = Math.Clamp(transparency, 0, 100) / 100.0;
        return effect switch
        {
            "Acrylic" => ((float)(0.82 - 0.68 * t), (float)(0.52 + 0.40 * t), 1.0),
            "Blur" => (0.32f, 0.84f, 1.0),
            // Smoke is deliberately neutral. Rainbow gets a much stronger
            // colored overlay and a wider opacity range so the two are
            // visually distinct at every transparency value.
            // Smoke deliberately has a fixed opacity; its slider is hidden.
            "Smoke" => (0.48f, 0.76f, 0.64),
            "Rainbow" => ((float)(0.40 - 0.24 * t), 0.72f, 0.98 - 0.83 * t),
            _ => (1.0f, 1.0f, 1.0)
        };
    }

    public static double GetMicaScrimAlpha(string effect, int transparency)
    {
        var t = Math.Clamp(transparency, 0, 100) / 100.0;
        var alpha = 0.05 + 0.78 * (1.0 - t);
        if (effect == "Alt")
        {
            alpha += 0.06;
        }

        return Math.Clamp(alpha, 0.05, 0.90);
    }

    public static void Apply(
        Border border,
        string effect,
        bool dark,
        bool backdropActive,
        double overlayOpacity = 1.0)
    {
        border.Opacity = 1.0;
        var overlay = CreateOverlay(effect, dark);
        if (overlay is null)
        {
            if (backdropActive)
            {
                border.ClearValue(Border.BackgroundProperty);
                border.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                return;
            }

            overlay = Solid(dark);
        }

        border.Background = overlay;
        border.Opacity = effect is "Smoke" or "Rainbow"
            ? Math.Clamp(overlayOpacity, 0.15, 1.0)
            : 1.0;
        border.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    public static Brush Solid(bool dark) => new SolidColorBrush(
        dark ? Color.FromArgb(255, 28, 30, 34) : Color.FromArgb(255, 246, 247, 249));

    private static Brush CreateSmoke(bool dark)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        if (dark)
        {
            brush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(90, 40, 44, 52) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.38, Color = Color.FromArgb(50, 28, 31, 38) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.72, Color = Color.FromArgb(38, 55, 58, 66) });
            brush.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(18, 15, 17, 21) });
        }
        else
        {
            brush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(90, 255, 255, 255) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.38, Color = Color.FromArgb(50, 238, 240, 244) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.72, Color = Color.FromArgb(38, 210, 214, 220) });
            brush.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(20, 250, 251, 253) });
        }

        return brush;
    }

    private static Brush CreateRainbow(bool dark)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        if (dark)
        {
            brush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(145, 255, 90, 170) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.38, Color = Color.FromArgb(120, 90, 140, 255) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.72, Color = Color.FromArgb(95, 60, 220, 220) });
            brush.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(55, 255, 170, 120) });
        }
        else
        {
            brush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(125, 255, 120, 180) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.38, Color = Color.FromArgb(110, 150, 140, 255) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.72, Color = Color.FromArgb(85, 100, 210, 255) });
            brush.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(45, 255, 210, 150) });
        }

        return brush;
    }
}
