using System.Collections.ObjectModel;
using System.Diagnostics;
using MicaBrowser.Backdrop;
using MicaBrowser.Models;
using MicaBrowser.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace MicaBrowser;

public sealed partial class TaskManagerWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Time)> _previous = new();
    private readonly ObservableCollection<BrowserTaskItem> _items = new();
    private readonly AppWindow _appWindow;

    public TaskManagerWindow()
    {
        InitializeComponent();
        var settings = App.Services.Settings;
        var theme = settings.NightMode
            ? ElementTheme.Dark
            : settings.ThemeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        RootGrid.RequestedTheme = theme;
        RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

        var effect = settings.MicaMaterial;
        if (effect is "Mica" or "Alt" && ThemeMicaBackdrop.IsSupported)
        {
            SystemBackdrop = new ThemeMicaBackdrop
            {
                Theme = theme,
                Kind = effect == "Alt" ? MicaKind.BaseAlt : MicaKind.Base,
                Transparency = settings.MicaTransparency,
                AccentColor = ThemeAccent.Parse(settings.AccentColor),
                UseDarkTint = AppSurface.IsDark(settings.NightMode, settings.ThemeMode, theme)
            };
        }
        else if (ThemeAcrylicBackdrop.IsSupported)
        {
            var dark = AppSurface.IsDark(settings.NightMode, settings.ThemeMode, theme);
            var tint = effect switch
            {
                "Acrylic" => ThemeAccent.Parse(settings.AcrylicTintColor),
                "Smoke" => dark
                    ? Windows.UI.Color.FromArgb(255, 28, 31, 38)
                    : Windows.UI.Color.FromArgb(255, 238, 242, 248),
                "Rainbow" => dark
                    ? Windows.UI.Color.FromArgb(255, 58, 50, 92)
                    : Windows.UI.Color.FromArgb(255, 236, 232, 250),
                _ => dark
                    ? Windows.UI.Color.FromArgb(255, 32, 34, 40)
                    : Windows.UI.Color.FromArgb(255, 246, 247, 250)
            };
            var opacity = MaterialEffects.GetOpacity(effect, settings.MicaTransparency);
            SystemBackdrop = new ThemeAcrylicBackdrop
            {
                Theme = theme,
                TintColor = tint,
                TintOpacity = (float)opacity.Tint,
                LuminosityOpacity = (float)opacity.Luminosity
            };
        }
        var hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        const int width = 900;
        const int height = 600;
        _appWindow.MoveAndResize(new RectInt32(
            work.X + Math.Max(0, (work.Width - width) / 2),
            work.Y + Math.Max(0, (work.Height - height) / 2),
            Math.Min(width, work.Width),
            Math.Min(height, work.Height)));

        TasksList.ItemsSource = _items;
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var processInfos = new Dictionary<int, string>();
        try
        {
            var environment = App.Services.BrowserEnvironment.Environment;
            if (environment is not null)
            {
                foreach (var info in environment.GetProcessInfos())
                {
                    processInfos[info.ProcessId] = info.Kind.ToString();
                }
            }
        }
        catch
        {
            // Process APIs are best effort and vary across WebView2 versions.
        }

        var current = Process.GetCurrentProcess();
        var ids = new HashSet<int>(processInfos.Keys) { current.Id };
        var now = DateTime.UtcNow;
        var nextPrevious = new Dictionary<int, (TimeSpan Cpu, DateTime Time)>();
        var rows = new List<BrowserTaskItem>();
        long totalMemory = 0;
        double totalCpu = 0;

        foreach (var id in ids)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                var cpu = process.TotalProcessorTime;
                var cpuPercent = 0.0;
                if (_previous.TryGetValue(id, out var old))
                {
                    var elapsed = Math.Max(0.1, (now - old.Time).TotalSeconds);
                    cpuPercent = Math.Clamp(
                        (cpu - old.Cpu).TotalMilliseconds / 1000.0 / elapsed /
                        Math.Max(1, Environment.ProcessorCount) * 100.0,
                        0,
                        100);
                }

                nextPrevious[id] = (cpu, now);
                totalMemory += process.WorkingSet64;
                totalCpu += cpuPercent;
                var isBrowser = id == current.Id;
                rows.Add(new BrowserTaskItem
                {
                    Name = isBrowser ? "EXET" : "WebView2",
                    Kind = isBrowser ? "浏览器" : KindLabel(processInfos[id]),
                    Detail = isBrowser ? "主进程" : $"进程 {id}",
                    ProcessId = id,
                    MemoryBytes = process.WorkingSet64,
                    Cpu = $"{cpuPercent:F1}%",
                    Memory = FormatBytes(process.WorkingSet64)
                });
            }
            catch
            {
                // The process may have exited between enumeration and sampling.
            }
        }

        _previous.Clear();
        foreach (var pair in nextPrevious)
        {
            _previous[pair.Key] = pair.Value;
        }

        _items.Clear();
        foreach (var row in rows.OrderByDescending(row => row.MemoryBytes))
        {
            _items.Add(row);
        }

        SummaryText.Text =
            $"共 {_items.Count} 个进程 · CPU {totalCpu:F1}% · 内存 {FormatBytes(totalMemory)}";
    }

    private static string KindLabel(string kind) => kind switch
    {
        "Browser" => "浏览器",
        "Renderer" => "渲染进程",
        "Gpu" => "GPU",
        "Utility" => "实用程序",
        "Crashpad" => "崩溃处理",
        _ => "WebView2"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F0} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024:F1} MB";
        }

        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }
}
