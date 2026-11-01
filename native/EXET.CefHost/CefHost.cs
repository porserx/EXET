using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO.MemoryMappedFiles;
using EXET.Cef.Contracts;

namespace EXET.CefHost;

public sealed class CefHost : ICefHost
{
    private static readonly string ServerPath = Path.Combine(AppContext.BaseDirectory, "EXET.CefServer.exe");
    private Process? _server;
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private Timer? _timer;
    private int _lastFrame;

    public event EventHandler<CefPaintFrame>? Paint;

    public IntPtr CreateBrowser(string url, int width, int height)
    {
        DisposeBrowser(IntPtr.Zero);
        var mapName = "EXET.CefFrame." + Guid.NewGuid().ToString("N");
        var commandLine = $"\"{ServerPath}\" \"{mapName}\" \"{url}\" {width} {height}";
        _server = StartBreakaway(commandLine) ?? StartFallback(commandLine)
            ?? throw new InvalidOperationException("Could not start CEF server.");
        _lastFrame = 0;

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                _map = MemoryMappedFile.OpenExisting(mapName);
                _view = _map.CreateViewAccessor(0, 16 + 1920 * 1080 * 4);
                break;
            }
            catch (FileNotFoundException)
            {
                Thread.Sleep(50);
            }
        }
        if (_view is null)
        {
            throw new TimeoutException("CEF shared memory did not become available.");
        }

        _timer = new Timer(_ => Pump(), null, 0, 33);
        return (IntPtr)_server.Id;
    }

    private void Pump()
    {
        if (_view is null) return;
        try
        {
            var frameId = _view.ReadInt32(8);
            if (frameId == 0 || frameId == _lastFrame) return;
            var width = _view.ReadInt32(0);
            var height = _view.ReadInt32(4);
            var length = _view.ReadInt32(12);
            if (width <= 0 || height <= 0 || length <= 0 || length != width * height * 4) return;
            var bytes = new byte[length];
            _view.ReadArray(16, bytes, 0, length);
            _lastFrame = frameId;
            Paint?.Invoke(this, new CefPaintFrame { Width = width, Height = height, Pixels = bytes });
        }
        catch
        {
        }
    }

    public void Resize(IntPtr browserId, int width, int height)
    {
        // Server-side resize IPC is added after the frame transport is verified.
    }

    public void DisposeBrowser(IntPtr browserId)
    {
        _timer?.Dispose();
        _timer = null;
        _view?.Dispose();
        _view = null;
        _map?.Dispose();
        _map = null;
        if (_server is { HasExited: false })
        {
            try { _server.Kill(true); } catch { }
        }
        _server?.Dispose();
        _server = null;
    }

    public void Shutdown()
    {
        DisposeBrowser(IntPtr.Zero);
    }

    private Process? StartFallback(string commandLine)
    {
        var info = new ProcessStartInfo(ServerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
            Arguments = commandLine.Substring(ServerPath.Length + 2)
        };
        return Process.Start(info);
    }

    private Process? StartBreakaway(string commandLine)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
        const uint CREATE_NO_WINDOW = 0x08000000;
        if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                CREATE_BREAKAWAY_FROM_JOB | CREATE_NO_WINDOW, IntPtr.Zero,
                AppContext.BaseDirectory, ref si, out var pi))
        {
            return null;
        }
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return Process.GetProcessById(pi.dwProcessId);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string? app, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
        uint creationFlags, IntPtr environment, string currentDirectory,
        ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}

