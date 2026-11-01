using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MicaBrowser.Backdrop;

/// <summary>
/// A system backdrop that can be switched between the two system materials,
/// light/dark themes, tint opacity and an accent tint at runtime.
/// </summary>
public sealed class ThemeMicaBackdrop : SystemBackdrop
{
    private MicaController? _controller;
    private SystemBackdropConfiguration? _configuration;

    public ElementTheme Theme { get; set; } = ElementTheme.Default;
    public MicaKind Kind { get; set; } = MicaKind.BaseAlt;
    public int Transparency { get; set; } = 35;
    public Color AccentColor { get; set; } = Color.FromArgb(255, 31, 112, 184);
    public bool UseDarkTint { get; set; }
    public double DarkTintScale { get; set; } = 1.0;
    public bool UseCustomTint { get; set; }
    public Color CustomTintColor { get; set; } = Color.FromArgb(255, 32, 32, 32);
    public float CustomTintOpacity { get; set; } = 0.60f;
    public float CustomLuminosityOpacity { get; set; } = 0.30f;

    public static bool IsSupported => MicaController.IsSupported();

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

        _controller = new MicaController
        {
            Kind = Kind
        };
        ApplyMaterialSettings();

        var attached = _controller.AddSystemBackdropTarget(target);
        if (attached)
        {
            _controller.SetSystemBackdropConfiguration(_configuration);
        }

        MicaBrowser.Services.Logger.Info(
            $"Mica target connected: {attached}, kind={Kind}, theme={Theme}, darkTint={UseDarkTint}, transparency={Transparency}");
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
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
        }

        ResetAndApply(true);
    }

    public void UpdateMaterial(MicaKind kind)
    {
        Kind = kind;
        ResetAndApply(true);
    }

    public void UpdateTransparency(int transparency)
    {
        Transparency = Math.Clamp(transparency, 0, 100);
        ResetAndApply();
    }

    public void UpdateDarkTint(bool dark)
    {
        UseDarkTint = dark;
        ResetAndApply(true);
    }

    public void UpdateAccent(Color color)
    {
        AccentColor = color;
    }

    private void ResetAndApply(bool reapplyConfiguration = false)
    {
        if (_controller is null)
        {
            return;
        }

        // Reset tint/opacity before applying the new theme or material. This
        // avoids a stale dark tint when returning to light mode, and lets a
        // Base/BaseAlt switch take effect without rebuilding the whole
        // backdrop (which caused the window to flicker).
        _controller.ResetProperties();
        _controller.Kind = Kind;
        ApplyMaterialSettings();

        if (reapplyConfiguration && _configuration is not null)
        {
            // Theme changes must be pushed into the controller explicitly;
            // otherwise the first dark->light switch can leave the old
            // backdrop colors until a second switch.
            _controller.SetSystemBackdropConfiguration(_configuration);
        }
    }

    private void ApplyMaterialSettings()
    {
        if (_controller is null)
        {
            return;
        }

        _controller.Kind = Kind;

        // MicaController exposes tint and luminosity opacity even though the
        // built-in MicaBackdrop does not. Use them to implement the requested
        // transparency slider while keeping the sampled wallpaper visible.
        if (UseCustomTint)
        {
            _controller.TintColor = CustomTintColor;
            _controller.TintOpacity = CustomTintOpacity;
            _controller.LuminosityOpacity = CustomLuminosityOpacity;
            _controller.FallbackColor = Color.FromArgb(255, CustomTintColor.R, CustomTintColor.G, CustomTintColor.B);
            return;
        }
        // Custom properties were cleared by ResetProperties; do not set them,
        // so the Mica controller follows the system theme defaults.
        var dark = UseDarkTint || Theme == ElementTheme.Dark;
        var alt = Kind == MicaKind.BaseAlt;
        var t = Math.Clamp(Transparency, 0, 100) / 100.0;
        // WinUI Mica is a neutral material. Keep the tint black/white and
        // leave the sampled wallpaper visible; the transparency slider only
        // changes the neutral tint/luminosity layers.
        var neutral = alt ? (byte)24 : (byte)32;
        _controller.TintColor = dark
            ? Color.FromArgb(255, neutral, neutral, neutral)
            : (alt ? Color.FromArgb(255, 232, 236, 242) : Color.FromArgb(255, 243, 243, 243));
        _controller.TintOpacity = dark
            ? (float)Math.Clamp(0.04 + (1 - t) * 0.34, 0.02, 0.55)
            : (float)Math.Clamp((alt ? 0.16 : 0.04) + (1 - t) * 0.30, 0.03, 0.50);
        _controller.LuminosityOpacity = (float)Math.Clamp(0.92 - (1 - t) * 0.10, 0.70, 0.96);
        _controller.FallbackColor = dark
            ? Color.FromArgb(255, 32, 32, 32)
            : Color.FromArgb(255, 243, 243, 243);
    }

    public void UpdateCustomization(bool useCustom, Color color, float tintOpacity, float luminosityOpacity, int transparency)
    {
        UseCustomTint = useCustom;
        CustomTintColor = color;
        CustomTintOpacity = tintOpacity;
        CustomLuminosityOpacity = luminosityOpacity;
        Transparency = transparency;
        ResetAndApply(true);
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
