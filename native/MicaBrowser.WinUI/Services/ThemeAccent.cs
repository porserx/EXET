using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MicaBrowser.Services;

public static class ThemeAccent
{
    public static readonly (string Name, string Hex)[] Presets =
    {
        ("标志蓝", "#1F70B8"),
        ("紫", "#6B4DE6"),
        ("蓝", "#2F6BFF"),
        ("青", "#008C95"),
        ("绿", "#2E8B57"),
        ("橙", "#D97706"),
        ("红", "#C2413B"),
        ("粉", "#C0397A")
    };

    public static Color Parse(string? value)
    {
        var text = (value ?? string.Empty).Trim().TrimStart('#');
        if (text.Length == 8 &&
            uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var argb))
        {
            return Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
        }

        if (text.Length == 6 &&
            uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return Color.FromArgb(
                255,
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF));
        }

        return Color.FromArgb(255, 31, 112, 184);
    }

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static void Apply(FrameworkElement root, string? hex)
    {
        var color = Parse(hex);
        var light1 = Shade(color, 0.18);
        var light2 = Shade(color, 0.34);
        var light3 = Shade(color, 0.50);
        var dark1 = Shade(color, -0.16);
        var dark2 = Shade(color, -0.30);
        var dark3 = Shade(color, -0.44);

        var app = Application.Current.Resources;
        app["SystemAccentColor"] = color;
        app["SystemAccentColorLight1"] = light1;
        app["SystemAccentColorLight2"] = light2;
        app["SystemAccentColorLight3"] = light3;
        app["SystemAccentColorDark1"] = dark1;
        app["SystemAccentColorDark2"] = dark2;
        app["SystemAccentColorDark3"] = dark3;

        SetBrushColor(app, "AccentFillColorDefaultBrush", color);
        SetBrushColor(app, "AccentFillColorSecondaryBrush", Color.FromArgb(230, color.R, color.G, color.B));
        SetBrushColor(app, "AccentFillColorTertiaryBrush", Color.FromArgb(180, color.R, color.G, color.B));
        SetBrushColor(app, "AccentFillColorDisabledBrush", Color.FromArgb(51, color.R, color.G, color.B));
        SetBrushColor(app, "AccentTextFillColorPrimaryBrush", color);
        SetBrushColor(app, "AccentTextFillColorSecondaryBrush", Color.FromArgb(200, color.R, color.G, color.B));
        SetBrushColor(app, "AccentTextFillColorTertiaryBrush", Color.FromArgb(150, color.R, color.G, color.B));
        SetBrushColor(app, "SystemControlHighlightAccentBrush", color);
        SetBrushColor(app, "SystemControlBackgroundAccentBrush", color);
        SetBrushColor(app, "SystemControlForegroundAccentBrush", color);
        SetBrushColor(app, "SliderTrackValueFill", color);
        SetBrushColor(app, "SliderTrackValueFillPointerOver", Color.FromArgb(230, color.R, color.G, color.B));
        SetBrushColor(app, "SliderTrackValueFillPressed", Color.FromArgb(180, color.R, color.G, color.B));
        SetBrushColor(app, "ToggleSwitchFillOn", color);
        SetBrushColor(app, "ToggleSwitchFillOnPointerOver", Color.FromArgb(230, color.R, color.G, color.B));
        SetBrushColor(app, "ToggleSwitchFillOnPressed", Color.FromArgb(180, color.R, color.G, color.B));
        SetBrushColor(app, "ListViewItemSelectionIndicatorBrush", color);
        SetBrushColor(app, "ListViewItemSelectionIndicatorPointerOverBrush", color);
        SetBrushColor(app, "ListViewItemSelectionIndicatorPressedBrush", color);

        // The root may contain local accent brushes created by an earlier build;
        // update those instances too so already-created controls change color.
        foreach (var key in new[]
                 {
                     "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush",
                     "AccentFillColorTertiaryBrush", "AccentTextFillColorPrimaryBrush",
                     "AccentTextFillColorSecondaryBrush"
                 })
        {
            if (root.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
            {
                brush.Color = key.Contains("Secondary", StringComparison.Ordinal)
                    ? Color.FromArgb(200, color.R, color.G, color.B)
                    : key.Contains("Tertiary", StringComparison.Ordinal)
                        ? Color.FromArgb(180, color.R, color.G, color.B)
                        : color;
            }
        }
    }

    private static void SetBrushColor(ResourceDictionary dictionary, string key, Color color)
    {
        if (dictionary.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        dictionary[key] = new SolidColorBrush(color);
    }

    private static Color Shade(Color color, double amount)
    {
        byte Channel(byte value)
        {
            var target = amount >= 0 ? 255.0 : 0.0;
            return (byte)Math.Clamp(value + (target - value) * Math.Abs(amount), 0, 255);
        }

        return Color.FromArgb(color.A, Channel(color.R), Channel(color.G), Channel(color.B));
    }
}
