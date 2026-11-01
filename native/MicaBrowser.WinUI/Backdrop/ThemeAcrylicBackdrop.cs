using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MicaBrowser.Backdrop;

/// <summary>
/// Real Windows desktop acrylic backdrop with a configurable tint. Using the
/// controller (instead of an in-app AcrylicBrush) is what makes the material
/// blur the desktop instead of falling back to a flat color.
/// </summary>
public sealed class ThemeAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    public ElementTheme Theme { get; set; } = ElementTheme.Default;
    public DesktopAcrylicKind Kind { get; set; } = DesktopAcrylicKind.Base;
    public bool UseCustomTint { get; set; }
    public Color TintColor { get; set; } = Color.FromArgb(255, 32, 32, 32);
    public float TintOpacity { get; set; } = 0.60f;
    public float LuminosityOpacity { get; set; } = 0.30f;

    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        if (!IsSupported)
        {
            return;
        }

        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = ToBackdropTheme(Theme)
        };
        _controller = new DesktopAcrylicController { Kind = Kind };
        if (UseCustomTint)
        {
            _controller.TintColor = TintColor;
            _controller.TintOpacity = TintOpacity;
            _controller.LuminosityOpacity = LuminosityOpacity;
            _controller.FallbackColor = Color.FromArgb(255, TintColor.R, TintColor.G, TintColor.B);
        }

        if (_controller.AddSystemBackdropTarget(target))
        {
            _controller.SetSystemBackdropConfiguration(_configuration);
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }

    public void UpdateTheme(ElementTheme theme)
    {
        Theme = theme;
        if (_configuration is not null)
        {
            _configuration.Theme = ToBackdropTheme(theme);
            // Acrylic does not always pick up a theme change from the
            // configuration alone. Push it back into the controller so a
            // dark window does not leave dark acrylic patches in light mode.
            _controller?.SetSystemBackdropConfiguration(_configuration);
        }

        if (_controller is not null && UseCustomTint)
        {
            _controller.FallbackColor = Color.FromArgb(255, TintColor.R, TintColor.G, TintColor.B);
        }
    }

    public void UpdateCustomization(bool useCustom, Color color, float tintOpacity, float luminosityOpacity, DesktopAcrylicKind kind)
    {
        UseCustomTint = useCustom;
        TintColor = color;
        TintOpacity = tintOpacity;
        LuminosityOpacity = luminosityOpacity;
        Kind = kind;
        if (_controller is null) return;
        _controller.Kind = kind;
        if (useCustom)
        {
            _controller.TintColor = color;
            _controller.TintOpacity = tintOpacity;
            _controller.LuminosityOpacity = luminosityOpacity;
            _controller.FallbackColor = Color.FromArgb(255, color.R, color.G, color.B);
        }
    }

    public void UpdateTint(Color color)
    {
        TintColor = color;
        if (_controller is not null)
        {
            _controller.TintColor = color;
        }
    }

    public void UpdateOpacity(float tintOpacity, float luminosityOpacity)
    {
        TintOpacity = tintOpacity;
        LuminosityOpacity = luminosityOpacity;
        if (_controller is not null)
        {
            _controller.TintOpacity = tintOpacity;
            _controller.LuminosityOpacity = luminosityOpacity;
        }
    }

    public void SetInputActive(bool active)
    {
        if (_configuration is not null)
        {
            _configuration.IsInputActive = active;
        }
    }

    private static SystemBackdropTheme ToBackdropTheme(ElementTheme theme) =>
        theme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };
}
