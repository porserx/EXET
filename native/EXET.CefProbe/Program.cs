using CefSharp;
using CefSharp.OffScreen;

var cache = Path.Combine(Path.GetTempPath(), "exet-cef-probe");
Directory.CreateDirectory(cache);
var settings = new CefSettings
{
    CachePath = cache,
    LogSeverity = LogSeverity.Warning,
    BrowserSubprocessPath = Path.Combine(AppContext.BaseDirectory, "CefSharp.BrowserSubprocess.exe"),
    MultiThreadedMessageLoop = true,
    WindowlessRenderingEnabled = true,
    LogFile = Path.Combine(cache, "cef.log")
};
settings.CefCommandLineArgs.Add("no-sandbox", "1");
settings.CefCommandLineArgs.Add("disable-gpu", "1");
settings.CefCommandLineArgs.Add("disable-software-rasterizer", "1");

if (!Cef.Initialize(settings))
{
    Console.WriteLine("CEF_INIT_FAILED");
    return 2;
}

Console.WriteLine("CEF_INIT_OK");
var browser = new ChromiumWebBrowser("https://example.com");
var loaded = new ManualResetEventSlim(false);
browser.LoadingStateChanged += (_, e) =>
{
    if (!e.IsLoading)
    {
        loaded.Set();
    }
};

var ok = loaded.Wait(TimeSpan.FromSeconds(30));
var title = browser.GetMainFrame().EvaluateScriptAsync("document.title").Result;
Console.WriteLine($"CEF_OK={ok}");
Console.WriteLine($"CEF_URL={browser.Address}");
Console.WriteLine($"CEF_TITLE={title.Result}");

browser.Dispose();
Cef.Shutdown();
return ok ? 0 : 3;
