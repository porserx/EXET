using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using CefSharp;
using CefSharp.OffScreen;

if (args.Length < 4)
{
    return 2;
}

var mapName = args[0];
var url = args[1];
var width = Math.Min(1280, Math.Max(320, int.Parse(args[2])));
var height = Math.Min(720, Math.Max(240, int.Parse(args[3])));
var dataDir = Path.Combine(AppContext.BaseDirectory, "cef-data");
var log = Path.Combine(dataDir, "cef-server.log");
try { Directory.CreateDirectory(dataDir); } catch { }
void Log(string message)
{
    try { File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n"); } catch { }
}
Log("start " + string.Join(' ', args));
Log($"render {width}x{height}");

var cache = Path.Combine(dataDir, "cache-" + Environment.ProcessId);
Directory.CreateDirectory(cache);
var settings = new CefSettings
{
    CachePath = cache,
    BrowserSubprocessPath = Path.Combine(AppContext.BaseDirectory, "CefSharp.BrowserSubprocess.exe"),
    MultiThreadedMessageLoop = true,
    WindowlessRenderingEnabled = true,
    LogFile = Path.Combine(cache, "cef.log")
};
settings.CefCommandLineArgs.Add("no-sandbox", "1");
var extra = Environment.GetEnvironmentVariable("CEF_EXTRA_FLAGS");
if (string.IsNullOrWhiteSpace(extra))
{
    settings.CefCommandLineArgs.Add("disable-gpu", "1");
    settings.CefCommandLineArgs.Add("disable-gpu-sandbox", "1");
    settings.CefCommandLineArgs.Add("disable-gpu-compositing", "1");
    settings.CefCommandLineArgs.Add("use-angle", "swiftshader");
}
else
{
    foreach (var item in extra.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var eq = item.IndexOf('=');
        if (eq > 0) settings.CefCommandLineArgs.Add(item[..eq], item[(eq + 1)..]);
        else settings.CefCommandLineArgs.Add(item, "1");
    }
}
if (!Cef.Initialize(settings))
{
    Log("init failed");
    return 3;
}
Log("init ok");

var capacity = 16 + 1920 * 1080 * 4;
using var map = MemoryMappedFile.CreateNew(mapName, capacity);
using var view = map.CreateViewAccessor(0, capacity);
var browser = new ChromiumWebBrowser(url, new BrowserSettings { WindowlessFrameRate = 30 })
{
    Size = new System.Drawing.Size(width, height)
};
var frameId = 0;
browser.Paint += (_, e) =>
{
    if (e.Width <= 0 || e.Height <= 0 || e.BufferHandle == IntPtr.Zero) return;
    var length = e.Width * e.Height * 4;
    if (length > capacity - 16) return;
    var bytes = new byte[length];
    Marshal.Copy(e.BufferHandle, bytes, 0, length);
    view.Write(8, ++frameId);
    view.Write(0, e.Width);
    view.Write(4, e.Height);
    view.Write(12, length);
    view.WriteArray(16, bytes, 0, length);
};
Log("browser created");

Thread.Sleep(Timeout.Infinite);
return 0;
