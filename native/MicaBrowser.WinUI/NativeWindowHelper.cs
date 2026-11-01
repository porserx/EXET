using System.Runtime.InteropServices;

namespace MicaBrowser;

internal static class NativeWindowHelper
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    private delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr idSubclass,
        IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SubclassProc callback,
        UIntPtr idSubclass,
        IntPtr refData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private static readonly SubclassProc Callback = WindowProc;

    public static void Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var attached = SetWindowSubclass(hwnd, Callback, (UIntPtr)1, IntPtr.Zero);
            Services.Logger.Info($"Maximize constraint hook attached: {attached}.");
        }
        catch (Exception ex)
        {
            Services.Logger.Warn("Could not attach the maximize constraint hook.", ex);
        }
    }

    private static IntPtr WindowProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr idSubclass,
        IntPtr refData)
    {
        var result = DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == WmGetMinMaxInfo)
        {
            TryConstrainMaximizedSize(hwnd, lParam);
        }

        return result;
    }

    private static void TryConstrainMaximizedSize(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return;
            }

            var work = info.Work;
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            mmi.MaxPosition.X = work.Left - info.Monitor.Left;
            mmi.MaxPosition.Y = work.Top - info.Monitor.Top;
            mmi.MaxSize.X = work.Right - work.Left;
            mmi.MaxSize.Y = work.Bottom - work.Top;
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        catch
        {
            // Let the system perform its default maximize.
        }
    }
}
