using System.Runtime.InteropServices;
using Windows.UI;

namespace MicaBrowser.Backdrop;

/// <summary>
/// Windows 10 fallback for Mica/Acrylic. The Windows App SDK controllers
/// require Windows 11, but Windows 10 exposes the classic accent-policy
/// acrylic blur through SetWindowCompositionAttribute.
/// </summary>
public static class LegacyAcrylicHelper
{
    private const int WcaAccentPolicy = 19;

    public static bool Apply(IntPtr hwnd, string effect, bool dark, int transparency)
    {
        if (hwnd == IntPtr.Zero) return false;

        var alt = effect == "Alt";
        Color tint;
        if (dark)
        {
            tint = alt ? Color.FromArgb(255, 18, 20, 24) : Color.FromArgb(255, 22, 24, 28);
        }
        else
        {
            tint = alt ? Color.FromArgb(255, 235, 239, 245) : Color.FromArgb(255, 243, 245, 250);
        }

        var opacity = Math.Clamp(255 - (int)(transparency * 1.5), 90, 220);
        var policy = new AccentPolicy
        {
            AccentState = AccentState.EnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = (opacity << 24) | (tint.B << 16) | (tint.G << 8) | tint.R
        };
        return SetAccent(hwnd, policy);
    }

    public static void Disable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetAccent(hwnd, new AccentPolicy { AccentState = AccentState.Disabled });
    }

    private static bool SetAccent(IntPtr hwnd, AccentPolicy policy)
    {
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = pointer,
                SizeOfData = size
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private enum AccentState
    {
        Disabled = 0,
        EnableAcrylicBlurBehind = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WindowCompositionAttributeData data);
}
