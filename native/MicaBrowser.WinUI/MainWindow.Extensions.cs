using MicaBrowser.Models;
using MicaBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MicaBrowser;

public sealed partial class MainWindow
{
    private StackPanel? _extensionButtonsPanel;

    private void InstallExtensionButtons()
    {
        if (_extensionButtonsPanel is not null) return;

        // Pinned extension buttons occupy their own toolbar column. Unpinned
        // popup extensions stay in the overflow menu.
        ToolbarRow.ColumnDefinitions.Insert(15, new ColumnDefinition { Width = GridLength.Auto });
        _extensionButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(_extensionButtonsPanel, 15);
        ToolbarRow.Children.Add(_extensionButtonsPanel);
        Grid.SetColumn(ToolbarExpandButton, 16);
        Grid.SetColumn(MenuButton, 17);
        Grid.SetColumnSpan(ZoomOverlay, 18);

        App.Services.Extensions.Changed += (_, _) => DispatcherQueue.TryEnqueue(RebuildExtensionButtons);
        App.Services.Extensions.Mutated += (_, _) => DispatcherQueue.TryEnqueue(RebuildExtensionButtons);
        RebuildExtensionButtons();
    }

    public bool HasPopupExtensions =>
        App.Services.Extensions.Items.Any(item => item.HasPopup);

    public bool IsExtensionPinned(string id) =>
        (App.Services.Settings.PinnedExtensions ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(id, StringComparer.OrdinalIgnoreCase);

    public void TogglePinnedExtension(string id)
    {
        var pinned = (App.Services.Settings.PinnedExtensions ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (pinned.RemoveAll(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            pinned.Add(id);
        }
        App.Services.Settings.PinnedExtensions = string.Join(',', pinned);
        App.Services.SettingsStore.Save();
        RebuildExtensionButtons();
        var info = App.Services.Extensions.Items.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        SetTransientStatus(info is null
            ? "扩展固定状态已更新"
            : (IsExtensionPinned(id) ? "已固定：" : "已取消固定：") + info.Name, 4);
    }

    private void RebuildExtensionButtons()
    {
        if (_extensionButtonsPanel is null) return;
        _extensionButtonsPanel.Children.Clear();

        foreach (var info in App.Services.Extensions.Items.Where(item => item.HasPopup && IsExtensionPinned(item.Id)))
        {
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6)
            };
            ToolTipService.SetToolTip(button, info.Name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, info.Name);
            button.Content = CreateExtensionIcon(info);
            button.Click += async (_, _) => await OpenExtensionPopupAsync(info, button);
            _extensionButtonsPanel.Children.Add(button);
        }

        _extensionButtonsPanel.Visibility = _extensionButtonsPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyToolbarSettings();
    }

    private static FrameworkElement CreateExtensionIcon(ExtensionInfo info)
    {
        var iconPath = string.IsNullOrWhiteSpace(info.IconPath)
            ? string.Empty
            : Path.Combine(info.Folder, info.IconPath);
        if (iconPath.Length > 0 && File.Exists(iconPath))
        {
            return new Image
            {
                Width = 16,
                Height = 16,
                Source = new BitmapImage(new Uri(iconPath))
            };
        }
        return new FontIcon
        {
            Glyph = "\uE8F1",
            FontSize = 14,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons")
        };
    }

    private bool AddExtensionMenuItems(MenuFlyout flyout)
    {
        var popupExtensions = App.Services.Extensions.Items.Where(item => item.HasPopup).ToList();
        if (popupExtensions.Count == 0) return false;

        var sub = new MenuFlyoutSubItem { Text = "扩展" };
        foreach (var info in popupExtensions)
        {
            var open = new MenuFlyoutItem { Text = "打开 " + info.Name };
            open.Click += async (_, _) =>
            {
                flyout.Hide();
                await OpenExtensionPopupAsync(info, ToolbarExpandButton);
            };
            sub.Items.Add(open);

            var pinned = IsExtensionPinned(info.Id);
            var pin = new MenuFlyoutItem
            {
                Text = (pinned ? "取消固定 " : "固定到工具栏 ") + info.Name
            };
            pin.Click += (_, _) =>
            {
                flyout.Hide();
                TogglePinnedExtension(info.Id);
            };
            sub.Items.Add(pin);
        }
        flyout.Items.Add(sub);
        return true;
    }

    private async Task OpenExtensionPopupAsync(ExtensionInfo info, FrameworkElement anchor)
    {
        if (!info.HasPopup)
        {
            SetTransientStatus("该扩展没有 popup 页面", 4);
            return;
        }

        var webView = new WebView2
        {
            Width = 380,
            Height = 520
        };
        var status = new TextBlock
        {
            Text = "正在加载扩展弹窗…",
            Margin = new Thickness(12),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
        };
        var host = new Grid();
        host.Children.Add(webView);
        host.Children.Add(status);

        var flyout = new Flyout
        {
            Content = host,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
        };
        flyout.ShowAt(anchor);

        try
        {
            await webView.EnsureCoreWebView2Async(App.Services.BrowserEnvironment.Environment);
            webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    status.Visibility = Visibility.Collapsed;
                    Logger.Info($"Extension popup loaded: {info.Name} {webView.Source}");
                }
                else
                {
                    status.Text = "扩展弹窗加载失败：" + args.WebErrorStatus;
                }
            };
            webView.CoreWebView2.Navigate(info.PopupUri);
            Logger.Info($"Extension popup navigate: {info.PopupUri}");
        }
        catch (Exception ex)
        {
            Logger.Warn("Extension popup failed.", ex);
            status.Text = "扩展弹窗打开失败：" + ex.Message;
        }
    }
}
