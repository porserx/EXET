using System.Diagnostics;
using MicaBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MicaBrowser;

public sealed partial class MainWindow
{
    private async void GhostButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.Settings;
        var flyout = new MenuFlyout { MenuFlyoutPresenterStyle = CreateMaterialMenuStyle() };
        var toggle = new MenuFlyoutItem
        {
            Text = (settings.GhostDownloaderEnabled ? "● " : "") + "使用 Ghost Downloader"
        };
        toggle.Click += (_, _) =>
        {
            settings.GhostDownloaderEnabled = !settings.GhostDownloaderEnabled;
            App.Services.SettingsStore.Save();
            SetTransientStatus(settings.GhostDownloaderEnabled
                ? "下载将交给 Ghost Downloader"
                : "下载由 EXET 处理");
        };
        flyout.Items.Add(toggle);

        var choose = new MenuFlyoutItem { Text = "选择 ghd.exe…" };
        choose.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            settings.GhostDownloaderPath = file.Path;
            App.Services.SettingsStore.Save();
            SetTransientStatus("已选择 Ghost Downloader：" + file.Path);
        };
        flyout.Items.Add(choose);

        var open = new MenuFlyoutItem { Text = "打开 Ghost Downloader" };
        open.Click += (_, _) =>
        {
            try
            {
                var path = settings.GhostDownloaderPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    SetTransientStatus("请先选择 Ghost Downloader 程序");
                    return;
                }
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetTransientStatus("无法打开 Ghost Downloader：" + ex.Message);
            }
        };
        flyout.Items.Add(open);
        flyout.Items.Add(new MenuFlyoutSeparator());

        var page = new MenuFlyoutItem { Text = "项目主页 / 下载" };
        page.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/XiaoYouChR/Ghost-Downloader-3")
        {
            UseShellExecute = true
        });
        flyout.Items.Add(page);

        flyout.ShowAt((FrameworkElement)sender);
    }
}
