using CefSharp;
using CefSharp.WinForms;

var cache = Path.Combine(Path.GetTempPath(), "exet-cef-winforms");
Directory.CreateDirectory(cache);
var settings = new CefSettings
{
    CachePath = cache,
    BrowserSubprocessPath = Path.Combine(AppContext.BaseDirectory, "CefSharp.BrowserSubprocess.exe"),
    MultiThreadedMessageLoop = true,
    LogFile = Path.Combine(cache, "cef.log")
};
settings.CefCommandLineArgs.Add("no-sandbox", "1");
settings.CefCommandLineArgs.Add("disable-gpu", "1");

if (!Cef.Initialize(settings))
{
    return 2;
}

ApplicationConfiguration.Initialize();
var form = new Form
{
    Text = "EXET CEF Host Probe",
    Width = 1100,
    Height = 760,
    StartPosition = FormStartPosition.CenterScreen
};
var browser = new ChromiumWebBrowser("https://example.com")
{
    Dock = DockStyle.Fill
};
form.Controls.Add(browser);
Application.Run(form);
Cef.Shutdown();
return 0;
