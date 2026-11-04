using System.Threading;
using MicaBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppNotifications;

namespace MicaBrowser;

public partial class App : Application
{
    private Window? _errorWindow;
    private const string InstanceMutexName = @"Local\EXET.Browser.Instance";
    private const string ShowEventName = @"Local\EXET.Browser.Show";
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showEvent;

    public static App Instance { get; private set; } = null!;
    public static AppServices Services { get; private set; } = null!;
    public static List<MainWindow> MainWindows { get; } = new();
    public static MainWindow? MainWindow { get; set; }

    public App()
    {
        Instance = this;
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            Logger.Error("Unhandled WinUI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Logger.Error("Unhandled AppDomain exception", args.ExceptionObject as Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var preload = Environment.GetCommandLineArgs()
                .Any(arg => string.Equals(arg, "--preload", StringComparison.OrdinalIgnoreCase));
            if (TrySignalExistingInstance())
            {
                Environment.Exit(0);
                return;
            }
            CreateInstanceObjects();

            Services = new AppServices();
            Services.LoadState();
            try
            {
                AppNotificationManager.Default.Register();
            }
            catch (Exception notificationEx)
            {
                Logger.Warn("Windows notifications are unavailable.", notificationEx);
            }
            Localization.English = string.Equals(
                Services.Settings.Language,
                "en-US",
                StringComparison.OrdinalIgnoreCase);
            FontRegistrar.RegisterBundledFonts();
            MainWindow = new MainWindow();

            if (preload)
            {
                // Start hidden and warm up the WebView2 environment. A normal
                // launch signals ShowEvent and only has to reveal this window.
                try
                {
                    MainWindow.AppWindow.Hide();
                }
                catch
                {
                    MainWindow.Activate();
                    MainWindow.AppWindow.Hide();
                }
                PrelaunchService.Sync(Services.Settings.PrelaunchEnabled);
                Logger.Info("EXET preload window ready.");
                _ = Services.InitializeEnvironmentAsync();
            }
            else
            {
                MainWindow.Activate();
                MainWindow.InitializeBrowserSession();
                PrelaunchService.Sync(Services.Settings.PrelaunchEnabled);
                // The window is created before WebView2 and the filter database
                // are ready, so the shell appears immediately.
                _ = Services.InitializeEnvironmentAsync();
            }

            StartActivationListener();
        }
        catch (Exception ex)
        {
            Logger.Error("EXET could not start.", ex);
            ShowFatalError(ex);
        }
    }

    private static bool TrySignalExistingInstance()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(InstanceMutexName);
            for (var i = 0; i < 20; i++)
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show))
                {
                    show.Set();
                    show.Dispose();
                    return true;
                }
                Thread.Sleep(50);
            }
        }
        catch
        {
            // No live instance (or the previous one is still starting).
        }
        return false;
    }

    private static void CreateInstanceObjects()
    {
        _instanceMutex = new Mutex(true, InstanceMutexName, out _);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    private void StartActivationListener()
    {
        var show = _showEvent;
        if (show is null) return;
        _ = Task.Run(() =>
        {
            while (true)
            {
                show.WaitOne();
                var window = MainWindow;
                if (window is null) continue;
                window.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        window.AppWindow.Show();
                    }
                    catch
                    {
                        // Already visible.
                    }
                    window.Activate();
                    window.InitializeBrowserSession();
                });
            }
        });
    }

    private void ShowFatalError(Exception ex)
    {
        _errorWindow = new Window
        {
            Title = "EXET — 启动错误",
            Content = new Grid
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Children =
                {
                    new StackPanel
                    {
                        Margin = new Thickness(40),
                        VerticalAlignment = VerticalAlignment.Center,
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "EXET 无法启动",
                                FontSize = 26,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                            },
                            new TextBlock
                            {
                                Text = ex.Message,
                                TextWrapping = TextWrapping.Wrap,
                                Opacity = 0.75
                            },
                            new TextBlock
                            {
                                Text = "详细日志位于 %LOCALAPPDATA%\\EXET\\Logs\\exet.log",
                                TextWrapping = TextWrapping.Wrap,
                                Opacity = 0.55
                            }
                        }
                    }
                }
            }
        };
        _errorWindow.Activate();
    }
}




