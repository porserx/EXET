using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.Graphics;
using MicaBrowser.Backdrop;
using MicaBrowser.Models;
using MicaBrowser.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.UI.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace MicaBrowser;

public sealed partial class MainWindow
{
    private void NewTab_Click(object sender, RoutedEventArgs e) => CreateTab(null, true);

    private FrameworkElement? _tabLayoutAnchor;
    private DateTime _lastTabLayoutMenu = DateTime.MinValue;
    private DateTime _lastVerticalTabsToggle = DateTime.MinValue;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoSendChanging = 0x0400;
    private const uint SwpAsyncWindowPos = 0x4000;

    private enum SnapTarget { None, Maximize, LeftHalf, RightHalf }
    private SnapTarget _snapTarget;
    private const uint SwpNoRedraw = 0x0008;

    private CancellationTokenSource? _windowDragCts;

    private readonly DispatcherTimer _windowDragTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };
    private bool _windowDragTimerReady;
    private bool _isWindowDragging;
    private NativePoint _windowDragStartCursor;
    private PointInt32 _windowDragStartPosition;
    private UIElement? _dragSourceElement;
    private bool _historyRecentOnly;
    private bool _sidebarDocked;
    private bool _sidebarTargetOpen;
    private int _sidebarAnimationVersion;
    private readonly ObservableCollection<HistoryEntry> _recentlyClosed = new();
    private byte[]? _pendingScreenshotBytes;
    private PointInt32 _lastWindowDragPosition;
    private DateTime _lastTitleBarClickTime = DateTime.MinValue;
    private NativePoint _lastTitleBarClickPoint;

    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        IntPtr uIdSubclass, IntPtr dwRefData);

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SubclassProc pfn, IntPtr uIdSubclass, IntPtr dwRefData);

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint point);

    private const uint WmNcHitTest = 0x0084;
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private SubclassProc? _titleBarSubclass;

    private void InstallTitleBarHitTest()
    {
        if (_hwnd == IntPtr.Zero || _titleBarSubclass is not null)
        {
            return;
        }

        _titleBarSubclass = TitleBarSubclassProc;
        SetWindowSubclass(_hwnd, _titleBarSubclass, (IntPtr)1, IntPtr.Zero);
    }

    private IntPtr TitleBarSubclassProc(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WmEnterSizeMove)
        {
            BrowserHost.Opacity = 0;
        }
        else if (msg == WmExitSizeMove)
        {
            BrowserHost.Opacity = 1;
        }
        else if (msg == WmNcHitTest)
        {
            var point = new NativePoint
            {
                X = (short)(lParam.ToInt64() & 0xFFFF),
                Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF)
            };
            ScreenToClient(hWnd, ref point);
            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            var y = point.Y / scale;
            if (y >= 0 && y < 40)
            {
                return (IntPtr)HtCaption;
            }
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private bool IsTitleBarInteractive(double x, double y)
    {
        if (IsPointInside(CustomCaptionButtons, x, y) ||
            IsPointInside(HorizontalNewTabButton, x, y))
        {
            return true;
        }

        if (!App.Services.Settings.VerticalTabs)
        {
            foreach (var tab in Tabs)
            {
                if (HorizontalTabList.ContainerFromItem(tab) is ListViewItem item &&
                    IsPointInside(item, x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsPointInside(FrameworkElement element, double x, double y)
    {
        if (element.Visibility != Visibility.Visible ||
            element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var point = element.TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            return x >= point.X && x <= point.X + element.ActualWidth &&
                   y >= point.Y && y <= point.Y + element.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private void AttachWindowDrag(UIElement element)
    {
        if (!_windowDragTimerReady)
        {
            _windowDragTimer.Tick += (_, _) => UpdateWindowDrag();
            _windowDragTimerReady = true;
        }
        element.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(WindowTitleBar_PointerPressed), true);
        element.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler(WindowTitleBar_PointerMoved), true);
        element.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(WindowTitleBar_PointerReleased), true);
        element.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(WindowTitleBar_PointerReleased), true);
    }

    private void UpdateWindowDrag()
    {
        if (!_isWindowDragging)
        {
            _windowDragTimer.Stop();
            return;
        }

        // Stop even if the pointer was released outside the title bar.
        if ((GetAsyncKeyState(0x01) & 0x8000) == 0)
        {
            EndWindowDrag();
            return;
        }

        if (_appWindow is null || !GetCursorPos(out var cursor))
        {
            return;
        }

        _snapTarget = GetSnapTarget(cursor);

        var next = new PointInt32(
            _windowDragStartPosition.X + cursor.X - _windowDragStartCursor.X,
            _windowDragStartPosition.Y + cursor.Y - _windowDragStartCursor.Y);
        if (next.X == _lastWindowDragPosition.X && next.Y == _lastWindowDragPosition.Y)
        {
            return;
        }

        _lastWindowDragPosition = next;
        var hwnd = Win32Interop.GetWindowFromWindowId(_appWindow.Id);
        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            next.X,
            next.Y,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    private void RunWindowDragLoop(
        IntPtr hwnd,
        NativePoint startCursor,
        PointInt32 startPosition,
        CancellationToken token)
    {
        var lastX = startPosition.X;
        var lastY = startPosition.Y;
        try
        {
            while (!token.IsCancellationRequested &&
                   (GetAsyncKeyState(0x01) & 0x8000) != 0)
            {
                if (GetCursorPos(out var cursor))
                {
                    var x = startPosition.X + cursor.X - startCursor.X;
                    var y = startPosition.Y + cursor.Y - startCursor.Y;
                    if (x != lastX || y != lastY)
                    {
                        lastX = x;
                        lastY = y;
                        SetWindowPos(
                            hwnd,
                            IntPtr.Zero,
                            x,
                            y,
                            0,
                            0,
                            SwpNoSize | SwpNoZOrder | SwpNoActivate |
                            SwpNoOwnerZOrder | SwpNoSendChanging |
                            SwpAsyncWindowPos | SwpNoRedraw);
                    }
                }

                Thread.Sleep(16);
            }
        }
        catch
        {
            // The window may close while dragging.
        }
        finally
        {
            try
            {
                SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    lastX,
                    lastY,
                    0,
                    0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate |
                    SwpNoOwnerZOrder | SwpNoSendChanging | SwpAsyncWindowPos);
            }
            catch
            {
            }

            _isWindowDragging = false;
            DispatcherQueue.TryEnqueue(() => BrowserHost.Visibility = Visibility.Visible);
        }
    }

    private void WindowTitleBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_appWindow is null || _isWindowDragging)
        {
            return;
        }

        var node = e.OriginalSource as DependencyObject;
        while (node is not null)
        {
            if (node is ButtonBase or ListViewItem or ScrollBar or TextBox)
            {
                return;
            }
            node = VisualTreeHelper.GetParent(node);
        }

        if (!e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed ||
            !GetCursorPos(out var cursor))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastTitleBarClickTime).TotalMilliseconds <= 450 &&
            Math.Abs(cursor.X - _lastTitleBarClickPoint.X) <= 4 &&
            Math.Abs(cursor.Y - _lastTitleBarClickPoint.Y) <= 4)
        {
            _lastTitleBarClickTime = DateTime.MinValue;
            _windowDragCts?.Cancel();
            _isWindowDragging = false;
            _windowDragTimer.Stop();
            ToggleWindowMaximize();
            e.Handled = true;
            return;
        }

        _lastTitleBarClickTime = now;
        _lastTitleBarClickPoint = cursor;

        _isWindowDragging = true;
        _windowDragStartCursor = cursor;
        _windowDragStartPosition = _appWindow.Position;
        _lastWindowDragPosition = _appWindow.Position;
        _snapTarget = SnapTarget.None;
        _windowDragTimer.Interval = TimeSpan.FromMilliseconds(6);
        _windowDragTimer.Start();
        e.Handled = true;
    }

    private void WindowTitleBar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isWindowDragging)
        {
            e.Handled = true;
        }
    }

    private void WindowTitleBar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndWindowDrag();
    }

    private SnapTarget GetSnapTarget(NativePoint cursor)
    {
        try
        {
            var display = DisplayArea.GetFromPoint(
                new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
            var work = display.WorkArea;
            const int edge = 10;
            if (cursor.Y <= work.Y + edge)
            {
                return SnapTarget.Maximize;
            }
            if (cursor.X <= work.X + edge)
            {
                return SnapTarget.LeftHalf;
            }
            if (cursor.X >= work.X + work.Width - edge)
            {
                return SnapTarget.RightHalf;
            }
        }
        catch
        {
        }

        return SnapTarget.None;
    }

    private void ApplySnapTarget()
    {
        if (_snapTarget == SnapTarget.None ||
            _appWindow?.Presenter is not OverlappedPresenter presenter ||
            !GetCursorPos(out var cursor))
        {
            return;
        }

        try
        {
            var display = DisplayArea.GetFromPoint(
                new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
            var work = display.WorkArea;
            if (_snapTarget == SnapTarget.Maximize)
            {
                if (presenter.State != OverlappedPresenterState.Maximized)
                {
                    presenter.Maximize();
                }
                return;
            }

            if (presenter.State == OverlappedPresenterState.Maximized)
            {
                presenter.Restore();
            }

            var halfWidth = work.Width / 2;
            var x = _snapTarget == SnapTarget.LeftHalf ? work.X : work.X + halfWidth;
            _appWindow.MoveAndResize(new RectInt32(x, work.Y, halfWidth, work.Height));
        }
        catch
        {
        }
    }

    private void EndWindowDrag()
    {
        if (!_isWindowDragging)
        {
            return;
        }

        _isWindowDragging = false;
        _windowDragTimer.Stop();
        _windowDragCts?.Cancel();
        ApplySnapTarget();
        _snapTarget = SnapTarget.None;
    }

    private void ToggleWindowMaximize()
    {
        if (_appWindow?.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }
        DispatcherQueue.TryEnqueue(UpdateCaptionGlyph);
    }

    private void MinimizeCaption_Click(object sender, RoutedEventArgs e)
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    private void MaximizeCaption_Click(object sender, RoutedEventArgs e)
    {
        if (_appWindow?.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }
        DispatcherQueue.TryEnqueue(UpdateCaptionGlyph);
    }

    private void CloseCaption_Click(object sender, RoutedEventArgs e) => Close();

    private void TabLayoutMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor)
        {
            _tabLayoutAnchor = anchor;
        }
        ShowTabLayoutFlyout();
    }

    private void ShowTabLayoutFlyout()
    {
        if ((DateTime.UtcNow - _lastTabLayoutMenu).TotalMilliseconds < 250)
        {
            return;
        }

        _lastTabLayoutMenu = DateTime.UtcNow;
        var flyout = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = CreateMaterialMenuStyle()
        };

        var toggleLayout = new MenuFlyoutItem
        {
            Text = App.Services.Settings.VerticalTabs
                ? "切换为水平标签页"
                : "切换为垂直标签页",
            Icon = new FontIcon { Glyph = "\uE8A9" }
        };
        toggleLayout.Click += (_, _) =>
        {
            App.Services.Settings.VerticalTabs = !App.Services.Settings.VerticalTabs;
            App.Services.SettingsStore.Save();
            ApplyVerticalTabs(true);
        };
        flyout.Items.Add(toggleLayout);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = "\u672c\u6b21\u4f7f\u7528\u8bb0\u5f55",
            IsEnabled = false
        });

        foreach (var entry in SessionHistory.Take(10))
        {
            var item = new MenuFlyoutItem
            {
                Text = entry.Title,
                MaxWidth = 300,
                Icon = new FontIcon { Glyph = "\uE81C" }
            };
            ToolTipService.SetToolTip(item, entry.Url);
            var target = entry.Url;
            item.Click += (_, _) => NavigateActive(target);
            flyout.Items.Add(item);
        }

        if (SessionHistory.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = "\u672c\u6b21\u8fd8\u6ca1\u6709\u8bb0\u5f55",
                IsEnabled = false
            });
        }

        var anchor = _tabLayoutAnchor ?? HorizontalTabLayoutButton;
        var placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
        try
        {
            var topLeft = anchor.TransformToVisual(RootGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
            var spaceBelow = RootGrid.ActualHeight - topLeft.Y - anchor.ActualHeight;
            placement = spaceBelow < 420
                ? FlyoutPlacementMode.TopEdgeAlignedLeft
                : FlyoutPlacementMode.BottomEdgeAlignedLeft;
        }
        catch
        {
        }
        flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = placement });
    }

    private void ToggleVerticalTabs()
    {
        // The title-bar button can receive both Tapped and Click depending on
        // the non-client hit-test path. Ignore the duplicate event.
        if ((DateTime.UtcNow - _lastVerticalTabsToggle).TotalMilliseconds < 250)
        {
            return;
        }

        _lastVerticalTabsToggle = DateTime.UtcNow;
        var settings = App.Services.Settings;
        settings.VerticalTabs = !settings.VerticalTabs;
        App.Services.SettingsStore.Save();
        ApplyVerticalTabs(true);
    }

    private void VerticalTabsCollapse_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.Settings;
        settings.VerticalTabs = true;
        settings.VerticalTabsIconsOnly = !settings.VerticalTabsIconsOnly;
        App.Services.SettingsStore.Save();
        ApplyVerticalTabs(true);
    }

    private void HorizontalTabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSelection)
        {
            return;
        }

        if (HorizontalTabList.SelectedItem is BrowserTab tab)
        {
            SelectTab(tab);
        }
    }

    private void VerticalTabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSelection)
        {
            return;
        }

        if (VerticalTabList.SelectedItem is BrowserTab tab)
        {
            SelectTab(tab);
        }
    }

    private void HorizontalTabList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BrowserTab tab)
        {
            SelectTab(tab);
        }
    }

    private void VerticalTabList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BrowserTab tab)
        {
            SelectTab(tab);
        }
    }

    private void IconTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            var tab = FindTabById(id);
            if (tab is not null)
            {
                SelectTab(tab);
            }
        }
    }

    private void CloseTabIcon_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            e.Handled = true;
            CloseTabById(id);
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string id)
        {
            CloseTabById(id);
        }
    }

    private void TabList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (FindTabItem(e.OriginalSource) is not { } item ||
            item.Content is not BrowserTab tab)
        {
            return;
        }

        ShowTabContextMenu(tab, item, e.GetPosition(item));
        e.Handled = true;
    }

    private void TabList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (FindTabItem(args.OriginalSource) is not { } item ||
            item.Content is not BrowserTab tab)
        {
            return;
        }

        if (!args.TryGetPosition(item, out var position))
        {
            position = new Windows.Foundation.Point(8, 8);
        }

        HideTabPreview();
        ShowTabContextMenu(tab, item, position);
        args.Handled = true;
    }

    private void TabList_PointerPressedForMenu(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint((UIElement)sender);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        if (FindTabItem(e.OriginalSource) is not { } item ||
            item.Content is not BrowserTab tab)
        {
            return;
        }

        HideTabPreview();
        ShowTabContextMenu(tab, item, e.GetCurrentPoint(item).Position);
        e.Handled = true;
    }

    private static ListViewItem? FindTabItem(object? source)
    {
        var node = source as DependencyObject;
        while (node is not null && node is not ListViewItem)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        return node as ListViewItem;
    }

    private void ShowTabContextMenu(BrowserTab tab, FrameworkElement anchor, Windows.Foundation.Point position)
    {
        var flyout = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = CreateMaterialMenuStyle()
        };

        var duplicate = new MenuFlyoutItem
        {
            Text = "复制标签页",
            Icon = new FontIcon { Glyph = "\uE8C8", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets") }
        };
        duplicate.Click += (_, _) => CreateTab(tab.Url, true, tab.IsPrivate, tab.IsHomePage);
        flyout.Items.Add(duplicate);

        var reload = new MenuFlyoutItem
        {
            Text = "刷新标签页",
            Icon = new FontIcon { Glyph = "\uE72C", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets") }
        };
        reload.Click += (_, _) => tab.Core?.Reload();
        flyout.Items.Add(reload);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var close = new MenuFlyoutItem
        {
            Text = "关闭标签页",
            Icon = new FontIcon { Glyph = "\uE711", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets") }
        };
        close.Click += (_, _) => CloseTabById(tab.Id);
        flyout.Items.Add(close);

        var closeOthers = new MenuFlyoutItem
        {
            Text = "关闭其他标签页",
            Icon = new FontIcon { Glyph = "\uE8BB", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets") }
        };
        closeOthers.Click += (_, _) =>
        {
            foreach (var other in Tabs.ToList())
            {
                if (!ReferenceEquals(other, tab))
                {
                    CloseTabById(other.Id);
                }
            }
            SelectTab(tab);
        };
        flyout.Items.Add(closeOthers);

        flyout.ShowAt(anchor, new FlyoutShowOptions { Position = position });
    }

    private void DuplicateTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        var tab = FindTabById(id);
        if (tab is null)
        {
            return;
        }

        CreateTab(tab.Url, true, tab.IsPrivate, tab.IsHomePage);
    }

    private void CloseTabFromMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            CloseTabById(id);
        }
    }

    private void CloseOtherTabs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        var keep = FindTabById(id);
        if (keep is null)
        {
            return;
        }

        foreach (var tab in Tabs.ToList())
        {
            if (!ReferenceEquals(tab, keep))
            {
                CloseTabById(tab.Id);
            }
        }

        SelectTab(keep);
    }

    private void CloseTabById(string id)
    {
        var tab = FindTabById(id);
        if (tab is null)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        if (tab.Core is not null && !string.IsNullOrWhiteSpace(tab.Url) &&
            !tab.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            _recentlyClosed.Insert(0, new HistoryEntry
            {
                Url = tab.Url,
                Title = string.IsNullOrWhiteSpace(tab.Title) ? tab.Url : tab.Title,
                VisitedAt = DateTime.Now
            });
            while (_recentlyClosed.Count > 20)
            {
                _recentlyClosed.RemoveAt(_recentlyClosed.Count - 1);
            }
        }

        Tabs.Remove(tab);
        if (tab.WebView is not null)
        {
            BrowserHost.Children.Remove(tab.WebView);
        }

        tab.Dispose();
        if (ReferenceEquals(_activeTab, tab))
        {
            _activeTab = null;
            if (Tabs.Count == 0)
            {
                CreateTab(null, true);
            }
            else
            {
                SelectTab(Tabs[Math.Clamp(index - 1, 0, Tabs.Count - 1)]);
            }
        }
        else
        {
            SyncSelection();
        }
    }

    private void CloseActiveTab()
    {
        if (_activeTab is not null)
        {
            CloseTabById(_activeTab.Id);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsInlineSettingsOpen) CloseInlineSettings();
        if (_activeTab?.Core?.CanGoBack == true)
        {
            _activeTab.Core.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsInlineSettingsOpen) CloseInlineSettings();
        if (_activeTab?.Core?.CanGoForward == true)
        {
            _activeTab.Core.GoForward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsInlineSettingsOpen) CloseInlineSettings();
        if (_activeTab?.Core is not null)
        {
            _activeTab.Core.Reload();
        }
        else if (_activeTab is not null)
        {
            _ = NavigateAsync(_activeTab, _activeTab.Url, true);
        }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        var home = GetHomePage();
        if (!string.IsNullOrWhiteSpace(home))
        {
            NavigateActive(home, true);
        }
    }

    private void TabAudioButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: BrowserTab tab })
        {
            return;
        }

        var muted = !tab.IsMuted;
        if (tab.Core is not null)
        {
            tab.Core.IsMuted = muted;
        }
        tab.IsMuted = muted;
    }

    private void AddressBox_GotFocus(object sender, RoutedEventArgs e)
    {
        AddressFocusRing.BorderBrush = new SolidColorBrush(
            ThemeAccent.Parse(App.Services.Settings.AccentColor));
        AddressDisplay.Visibility = Visibility.Collapsed;
        AddressBox.Opacity = 1;
        // An opaque focused field keeps selection repaints inside the
        // address bar instead of recompositing the Acrylic backdrop.
        var dark = AppSurface.IsDark(
            App.Services.Settings.NightMode,
            App.Services.Settings.ThemeMode,
            RootGrid.RequestedTheme);
        var fieldBrush = new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
            : Windows.UI.Color.FromArgb(255, 255, 255, 255));
        AddressBox.Background = fieldBrush;
        AddressBox.Resources["TextControlBackgroundFocused"] = fieldBrush;
        AddressBox.Resources["TextControlBackground"] = fieldBrush;
        // Pointer focus must not open the suggestion popup: dragging a
        // selection otherwise fights with popup/layout work on every move.
        if (AddressBox.FocusState == FocusState.Keyboard)
        {
            _addressTyping = true;
            ShowAddressSuggestions(AddressBox.Text);
            AddressBox.SelectAll();
        }
        else
        {
            _addressTyping = false;
            HideAddressSuggestions();
        }
    }

    private async void AddressBox_LostFocus(object sender, RoutedEventArgs e)
    {
        AddressFocusRing.BorderBrush = new SolidColorBrush(Colors.Transparent);
        AddressBox.Background = new SolidColorBrush(Colors.Transparent);
        AddressBox.Resources["TextControlBackgroundFocused"] = new SolidColorBrush(Colors.Transparent);
        AddressBox.Resources["TextControlBackground"] = new SolidColorBrush(Colors.Transparent);
        UpdateAddressDisplay(AddressBox.Text);
        await Task.Delay(140);
        if (AddressSuggestionsPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
        if (focused is not null &&
            (IsDescendantOf(focused, AddressSuggestionsPanel) ||
             IsDescendantOf(focused, AddressBox)))
        {
            return;
        }

        HideAddressSuggestions();
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
    {
        var node = child;
        while (node is not null)
        {
            if (ReferenceEquals(node, parent))
            {
                return true;
            }
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void UpdateAddressDisplay(string text)
    {
        if (AddressDisplay is null || AddressBox is null || AddressFocusRing is null)
        {
            return;
        }

        var value = text?.Trim() ?? string.Empty;
        AddressDisplay.Blocks.Clear();
        if (value.Length == 0)
        {
            AddressDisplay.Visibility = Visibility.Collapsed;
            AddressBox.Opacity = 1;
            return;
        }

        var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
        var light = RootGrid.ActualTheme == ElementTheme.Dark
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(105, 255, 255, 255))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(100, 0, 0, 0));
        var primary = new SolidColorBrush(RootGrid.ActualTheme == ElementTheme.Dark
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black);

        var schemeLength = 0;
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0)
        {
            schemeLength = schemeEnd + 3;
            paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = value[..schemeLength],
                Foreground = light
            });
        }

        var rest = value[schemeLength..];
        var domainEnd = rest.IndexOfAny(new[] { '/', '?', '#' });
        if (domainEnd < 0)
        {
            paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = rest,
                Foreground = primary
            });
        }
        else
        {
            paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = rest[..domainEnd],
                Foreground = primary
            });
            paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = rest[domainEnd..],
                Foreground = light
            });
        }

        AddressDisplay.Blocks.Add(paragraph);
        if (AddressBox.FocusState == FocusState.Unfocused)
        {
            AddressDisplay.Visibility = Visibility.Visible;
            AddressBox.Opacity = 0;
        }
        else
        {
            AddressDisplay.Visibility = Visibility.Collapsed;
            AddressBox.Opacity = 1;
        }
    }

    private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressAddressSuggestions && !_addressTyping &&
            AddressBox.FocusState != FocusState.Unfocused)
        {
            _addressTyping = true;
        }
        if (_suppressAddressSuggestions || !_addressTyping)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            _addressSuggestionsVersion++;
            HideAddressSuggestions();
            return;
        }

        _ = ShowAddressSuggestionsDelayedAsync(AddressBox.Text, ++_addressSuggestionsVersion);
    }

    private async Task ShowAddressSuggestionsDelayedAsync(string text, int version)
    {
        await Task.Delay(90);
        if (version != _addressSuggestionsVersion || !_addressTyping)
        {
            return;
        }

        ShowAddressSuggestions(text);
    }

    private void ShowAddressSuggestions(string input)
    {
        AddressSuggestions.Clear();
        var query = input.Trim();
        var historySource = query.Length == 0
            ? App.Services.History.Items
            : App.Services.History.Items.Where(item =>
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Url.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in historySource
                     .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .Take(query.Length == 0 ? 8 : 5))
        {
            AddressSuggestions.Add(new AddressSuggestion
            {
                Title = item.Title,
                Url = item.Url,
                Detail = item.Url,
                Glyph = "\uE81C"
            });
        }

        if (query.Length == 0)
        {
            if (AddressSuggestions.Count == 0)
            {
                HideAddressSuggestions();
                return;
            }
        }

        if (query.Length > 0)
        {
        foreach (var item in App.Services.Favorites.Items
                     .Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    item.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .Take(4))
        {
            if (AddressSuggestions.Any(existing =>
                    string.Equals(existing.Url, item.Url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddressSuggestions.Add(new AddressSuggestion
            {
                Title = item.Title,
                Url = item.Url,
                Detail = $"收藏夹 · {item.Folder}",
                Glyph = "\uE734"
            });
        }

        AddressSuggestions.Add(new AddressSuggestion
        {
            Title = $"搜索“{query}”",
            Url = query,
            Detail = $"使用 {App.Services.Settings.SearchEngine} 搜索",
            Glyph = "\uE721",
            IsSearch = true
        });
        }

        if (AddressBox.ActualWidth > 0)
        {
            AddressSuggestionsPanel.Width = Math.Max(420, AddressBox.ActualWidth + 76);
        }

        try
        {
            var point = AddressBox.TransformToVisual(RootGrid).TransformPoint(new Windows.Foundation.Point(0, 0));
            if (AddressSuggestionsPanel.RenderTransform is TranslateTransform transform)
            {
                transform.X = Math.Max(0, point.X);
                transform.Y = point.Y + AddressBox.ActualHeight + 2;
            }

            AddressSuggestionsPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not show address suggestions.", ex);
        }
    }

    private void HideAddressSuggestions()
    {
        AddressSuggestionsPanel.Visibility = Visibility.Collapsed;
        AddressSuggestionsList.SelectedItem = null;
    }

    private void AddressSuggestionsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AddressSuggestion suggestion)
        {
            return;
        }

        _addressTyping = false;
        HideAddressSuggestions();
        SetAddressBoxText(suggestion.Url);
        NavigateActive(suggestion.Url);
    }

    private void AddressGoButton_Click(object sender, RoutedEventArgs e) => CommitAddress();

    private void CommitAddress()
    {
        _addressTyping = false;
        HideAddressSuggestions();
        var target = AddressBox.Text;
        if (!string.IsNullOrWhiteSpace(target))
        {
            NavigateActive(target);
            _activeTab?.WebView?.Focus(FocusState.Programmatic);
        }
    }

    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter &&
            e.Key != Windows.System.VirtualKey.Escape &&
            e.Key != Windows.System.VirtualKey.Up &&
            e.Key != Windows.System.VirtualKey.Down)
        {
            _addressTyping = true;
        }

        if (AddressSuggestionsPanel.Visibility == Visibility.Visible &&
            (e.Key == Windows.System.VirtualKey.Down || e.Key == Windows.System.VirtualKey.Up))
        {
            var count = AddressSuggestions.Count;
            if (count > 0)
            {
                var index = AddressSuggestionsList.SelectedIndex;
                index = e.Key == Windows.System.VirtualKey.Down
                    ? Math.Min(count - 1, index + 1)
                    : Math.Max(0, index - 1);
                AddressSuggestionsList.SelectedIndex = index;
                AddressSuggestionsList.ScrollIntoView(AddressSuggestions[index]);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (AddressSuggestionsPanel.Visibility == Visibility.Visible &&
                AddressSuggestionsList.SelectedItem is AddressSuggestion suggestion)
            {
                HideAddressSuggestions();
                SetAddressBoxText(suggestion.Url);
                NavigateActive(suggestion.Url);
            }
            else
            {
                CommitAddress();
            }

            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (AddressSuggestionsPanel.Visibility == Visibility.Visible)
            {
                HideAddressSuggestions();
            }
            else
            {
                SetAddressBoxText(_activeTab?.Url ?? string.Empty);
                AddressBox.SelectAll();
            }

            e.Handled = true;
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e) =>
        AddOrShowFavorite(FavoriteButton);

    private void AddOrShowFavorite(FrameworkElement anchor)
    {
        HideAddressSuggestions();
        if (_activeTab is null || string.IsNullOrWhiteSpace(_activeTab.Url))
        {
            return;
        }

        var settings = App.Services.Settings;
        var existing = App.Services.Favorites.Find(_activeTab.Url);
        var entry = existing ?? App.Services.Favorites.Add(
            _activeTab.Url,
            _activeTab.Title,
            settings.DefaultFavoriteFolder);
        UpdateFavoriteState();
        if (existing is null)
        {
            SetTransientStatus($"已添加到收藏夹 · {entry.Folder}");
        }

        ShowFavoriteFlyout(entry, anchor);
    }

    private void ShowFavoriteFlyout(FavoriteEntry entry, FrameworkElement anchor)
    {
        favoriteFlyout?.Hide();
        var folders = new ObservableCollection<string>(
            App.Services.Favorites.GetFolders(App.Services.Settings.DefaultFavoriteFolder));
        var folderBox = new ComboBox
        {
            ItemsSource = folders,
            SelectedItem = entry.Folder,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var folderDark = AppSurface.IsDark(
            App.Services.Settings.NightMode,
            App.Services.Settings.ThemeMode,
            RootGrid.RequestedTheme);
        folderBox.RequestedTheme = folderDark ? ElementTheme.Dark : ElementTheme.Light;
        folderBox.Resources["ComboBoxDropDownBackground"] = new SolidColorBrush(folderDark
            ? Windows.UI.Color.FromArgb(232, 32, 34, 40)
            : Windows.UI.Color.FromArgb(232, 246, 247, 250));
        folderBox.Resources["ComboBoxItemBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var folderRow = new StackPanel { Spacing = 5 };
        folderRow.Children.Add(new TextBlock
        {
            Text = "保存到收藏夹",
            FontSize = 12,
            Opacity = 0.7
        });
        folderRow.Children.Add(folderBox);

        var newFolderBox = new TextBox
        {
            PlaceholderText = "新建收藏夹名称",
            FontSize = 12
        };
        var newFolderButton = new Button
        {
            Content = "新建",
            FontSize = 12,
            Padding = new Thickness(10, 4, 10, 4)
        };
        var newFolderRow = new Grid { ColumnSpacing = 6 };
        newFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        newFolderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        newFolderRow.Children.Add(newFolderBox);
        Grid.SetColumn(newFolderButton, 1);
        newFolderRow.Children.Add(newFolderButton);

        var done = new Button
        {
            Content = "完成",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var remove = new Button
        {
            Content = "移除收藏",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var panel = new StackPanel { Width = 300, Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(folderRow);
        panel.Children.Add(newFolderRow);
        panel.Children.Add(remove);
        panel.Children.Add(done);

        folderBox.SelectionChanged += (_, _) =>
        {
            if (folderBox.SelectedItem is string folder)
            {
                App.Services.Favorites.SetFolder(entry, folder);
            }
        };

        newFolderButton.Click += (_, _) =>
        {
            var name = newFolderBox.Text.Trim();
            if (name.Length == 0)
            {
                return;
            }

            App.Services.Favorites.CreateFolder(name);
            if (!folders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(name);
            }

            folderBox.SelectedItem = name;
            newFolderBox.Text = string.Empty;
        };

        remove.Click += (_, _) =>
        {
            App.Services.Favorites.Remove(entry);
            favoriteFlyout?.Hide();
            UpdateFavoriteState();
            SetTransientStatus("已从收藏夹移除");
        };

        done.Click += (_, _) => favoriteFlyout?.Hide();

        favoriteFlyout = new Flyout
        {
            Content = panel,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        favoriteFlyout.ShowAt(anchor);
    }

    private void FavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidebar("favorites");
        return;
        HideAddressSuggestions();
        if (string.Equals(App.Services.Settings.FavoritesDisplayMode, "Floating", StringComparison.OrdinalIgnoreCase))
        {
            ShowFavoritesFlyout(FavoritesButton);
        }
        else
        {
            OpenSidebar("favorites");
        }
    }

    private void SidebarPin_Click(object sender, RoutedEventArgs e)
    {
        var docked = !_sidebarDocked;
        ApplySidebarPresentation(docked);
        var value = docked ? "Sidebar" : "Floating";
        switch (_sidebarMode)
        {
            case "favorites":
                App.Services.Settings.FavoritesDisplayMode = value;
                break;
            case "downloads":
                App.Services.Settings.DownloadsDisplayMode = value;
                break;
            case "history":
                App.Services.Settings.HistoryDisplayMode = value;
                break;
        }
        App.Services.SettingsStore.Save();
    }

    private void UpdateFloatingPanelSize()
    {
        if (_sidebarDocked || SidebarPane.Visibility != Visibility.Visible)
        {
            return;
        }

        var availableWidth = ContentShell.ActualWidth > 0 ? ContentShell.ActualWidth : RootGrid.ActualWidth;
        var availableHeight = ContentShell.ActualHeight > 0 ? ContentShell.ActualHeight : RootGrid.ActualHeight;
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return;
        }

        SidebarPane.Width = Math.Clamp(availableWidth * 0.30, 300, 380);
        SidebarPane.Height = Math.Clamp(availableHeight * 0.90, 560, 780);
    }

    private void ApplySidebarPresentation(bool docked)
    {
        _sidebarDocked = docked;
        if (docked)
        {
            SidebarPane.Width = 340;
            SidebarPane.Height = double.NaN;
            SidebarPane.VerticalAlignment = VerticalAlignment.Stretch;
            SidebarPane.Margin = new Thickness(0);
            SidebarPane.CornerRadius = new CornerRadius(0);
            SidebarDismissLayer.Visibility = Visibility.Collapsed;
            SidebarPinIcon.Glyph = "\uE77A";
            ToolTipService.SetToolTip(SidebarPinButton, "取消固定");
        }
        else
        {
            SidebarPane.Width = 300;
            SidebarPane.Height = 640;
            SidebarPane.VerticalAlignment = VerticalAlignment.Top;
            UpdateFloatingPanelSize();
            SidebarPane.Margin = new Thickness(0, 6, 8, 0);
            SidebarPane.CornerRadius = new CornerRadius(22);
            SidebarDismissLayer.Visibility = SidebarPane.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
            SidebarPinIcon.Glyph = "\uE840";
            ToolTipService.SetToolTip(SidebarPinButton, "固定为侧栏");
        }
    }

    private void ToggleSidebar(string mode)
    {
        if (_sidebarTargetOpen && _sidebarMode == mode)
        {
            AnimateSidebar(false);
            return;
        }
        OpenSidebar(mode);
    }

    private void BuildFavoriteFolders()
    {
        FavoriteFoldersPanel.Children.Clear();
        var folders = App.Services.Favorites.GetFolders(App.Services.Settings.DefaultFavoriteFolder);
        foreach (var folder in folders)
        {
            var count = App.Services.Favorites.Items.Count(item =>
                string.Equals(string.IsNullOrWhiteSpace(item.Folder) ? "收藏夹" : item.Folder,
                    folder, StringComparison.OrdinalIgnoreCase));
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 7, 8, 7),
                Tag = folder,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8B7", FontSize = 15 },
                        new TextBlock { Text = folder, FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = count.ToString(), FontSize = 11, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            button.Click += (_, _) => ShowFavoriteFolder(folder);
            var menu = new MenuFlyout();
            var rename = new MenuFlyoutItem { Text = "重命名" };
            rename.Click += (_, _) => ShowRenameFolderFlyout(button, folder);
            menu.Items.Add(rename);
            var delete = new MenuFlyoutItem { Text = "删除文件夹" };
            delete.Click += (_, _) =>
            {
                if (App.Services.Favorites.DeleteFolder(folder, App.Services.Settings.DefaultFavoriteFolder))
                {
                    BuildFavoriteFolders();
                    SetTransientStatus($"已删除收藏夹：{folder}");
                }
            };
            menu.Items.Add(delete);
            button.ContextFlyout = menu;
            FavoriteFoldersPanel.Children.Add(button);
        }
    }

    private void ShowRenameFolderFlyout(FrameworkElement anchor, string folder)
    {
        var box = new TextBox { Text = folder, Width = 220, Margin = new Thickness(8) };
        var save = new Button { Content = "保存", Margin = new Thickness(8, 0, 8, 8) };
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(save);
        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        save.Click += (_, _) =>
        {
            if (App.Services.Favorites.RenameFolder(folder, box.Text, App.Services.Settings.DefaultFavoriteFolder))
            {
                flyout.Hide();
                BuildFavoriteFolders();
                SetTransientStatus("收藏夹已重命名");
            }
        };
        flyout.ShowAt(anchor);
        box.Focus(FocusState.Programmatic);
    }

    private void ShowFavoriteFolder(string folder)
    {
        FavoriteFoldersChrome.Visibility = Visibility.Visible;
        FavoriteFoldersPanel.Visibility = Visibility.Collapsed;
        FavoriteBackButton.Visibility = Visibility.Visible;
        FavoritesList.Visibility = Visibility.Visible;
        FavoritesList.Margin = new Thickness(0, 42, 0, 0);
        FavoritesList.ItemsSource = App.Services.Favorites.Items
            .Where(item => string.Equals(
                string.IsNullOrWhiteSpace(item.Folder) ? "收藏夹" : item.Folder,
                folder,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void FavoriteBack_Click(object sender, RoutedEventArgs e)
    {
        FavoriteFoldersChrome.Visibility = Visibility.Visible;
        FavoriteFoldersPanel.Visibility = Visibility.Visible;
        FavoriteBackButton.Visibility = Visibility.Collapsed;
        FavoritesList.Visibility = Visibility.Collapsed;
        FavoritesList.Margin = new Thickness(0);
        BuildFavoriteFolders();
    }

    private void SidebarAddFavorite_Click(object sender, RoutedEventArgs e)
    {
        AddOrShowFavorite(FavoriteButton);
    }

    private void SidebarNewFolder_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { PlaceholderText = "收藏夹名称", Width = 220, Margin = new Thickness(8) };
        var create = new Button { Content = "创建", Margin = new Thickness(8, 0, 8, 8) };
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(create);
        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        create.Click += (_, _) =>
        {
            var name = box.Text.Trim();
            if (name.Length == 0)
            {
                return;
            }
            App.Services.Favorites.CreateFolder(name);
            flyout.Hide();
            BuildFavoriteFolders();
        };
        flyout.ShowAt((FrameworkElement)sender);
        box.Focus(FocusState.Programmatic);
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HideAddressSuggestions();
        ToggleSidebar("history");
    }

    private void DownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidebar("downloads");
        return;
        HideAddressSuggestions();
        if (string.Equals(App.Services.Settings.DownloadsDisplayMode, "Floating", StringComparison.OrdinalIgnoreCase))
        {
            ShowDownloadsFlyout(DownloadsButton);
        }
        else
        {
            OpenSidebar("downloads");
        }
    }

    private void SettingsTabButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings("general");
    }

    private void ExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        HideAddressSuggestions();
        OpenSettings("extensions");
    }

    private void PasswordsButton_Click(object sender, RoutedEventArgs e) =>
        ShowPasswordsFlyout(PasswordsButton);

    private void ShowPasswordsFlyout(FrameworkElement anchor)
    {
        HideAddressSuggestions();
        var host = string.Empty;
        if (_activeTab is not null &&
            Uri.TryCreate(_activeTab.Url, UriKind.Absolute, out var pageUri))
        {
            host = pageUri.Host;
        }

        var matches = App.Services.Passwords.ForHost(host).ToList();
        var panel = new StackPanel { Width = 320, Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "密码管理器",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(host) ? "当前页面没有可用站点。" : host,
            FontSize = 12,
            Opacity = 0.7
        });

        if (matches.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "此网站还没有保存的登录信息。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.65,
                Margin = new Thickness(0, 4, 0, 4)
            });
        }
        else
        {
            foreach (var entry in matches)
            {
                var login = entry;
                var row = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = login.Username, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                            new TextBlock { Text = "••••••••", FontSize = 11, Opacity = 0.55 }
                        }
                    }
                };
                row.Click += async (_, _) =>
                {
                    passwordFlyout?.Hide();
                    await FillLoginAsync(login);
                };
                panel.Children.Add(row);
            }
        }

        var manage = new Button
        {
            Content = "管理密码",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 0)
        };
        manage.Click += (_, _) =>
        {
            passwordFlyout?.Hide();
            OpenSettings("passwords");
        };
        panel.Children.Add(manage);

        passwordFlyout = new Flyout
        {
            Content = panel,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        passwordFlyout.ShowAt(anchor);
    }

    private void CloseSidebarImmediate()
    {
        _sidebarTargetOpen = false;
        _sidebarAnimationVersion++;
        _sidebarAnimation?.Stop();
        SidebarTransform.Y = -28;
        SidebarPane.Opacity = 0;
        SidebarPane.Visibility = Visibility.Collapsed;
        SidebarDismissLayer.Visibility = Visibility.Collapsed;
    }

    private void CloseSidebar_Click(object sender, RoutedEventArgs e)
    {
        AnimateSidebar(false);
    }

    private void AnimateSidebar(bool open)
    {
        _sidebarTargetOpen = open;
        var version = ++_sidebarAnimationVersion;
        // Capture live values before Stop(), otherwise WinUI resets the
        // animated properties and the reversed animation jumps.
        var fromY = SidebarTransform.Y;
        var fromOpacity = SidebarPane.Opacity;
        _sidebarAnimation?.Stop();
        SidebarTransform.Y = fromY;
        SidebarPane.Opacity = fromOpacity;
        SidebarPane.Visibility = Visibility.Visible;
        if (open)
        {
            UpdateFloatingPanelSize();
            UpdateHistoryScrollHeight();
        }
        var toY = open ? 0 : -28;
        var toOpacity = open ? 1 : 0;
        SidebarDismissLayer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        void Finish()
        {
            if (version != _sidebarAnimationVersion || _sidebarTargetOpen != open)
            {
                return;
            }
            if (open)
            {
                SidebarTransform.Y = 0;
                SidebarPane.Opacity = 1;
                SidebarDismissLayer.Visibility = Visibility.Visible;
            }
            else
            {
                SidebarTransform.Y = -28;
                SidebarPane.Opacity = 0;
                SidebarDismissLayer.Visibility = Visibility.Collapsed;
                SidebarPane.Visibility = Visibility.Collapsed;
            }
        }

        var durationMs = Math.Max(open ? 160 : 220, App.Services.Settings.AnimationDuration);
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var easing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };

        var slide = new DoubleAnimation
        {
            From = fromY,
            To = toY,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(slide, SidebarTransform);
        Storyboard.SetTargetProperty(slide, "Y");

        var fade = new DoubleAnimation
        {
            From = fromOpacity,
            To = toOpacity,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(fade, SidebarPane);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) => Finish();
        _sidebarAnimation = storyboard;
        storyboard.Begin();
    }

    private void FloatSidebar_Click(object sender, RoutedEventArgs e)
    {
        OpenSidebar(_sidebarMode);
        return;
        var mode = _sidebarMode;
        _sidebarAnimation?.Stop();
        SidebarPane.Visibility = Visibility.Collapsed;
        if (mode == "downloads")
        {
            App.Services.Settings.DownloadsDisplayMode = "Floating";
            App.Services.SettingsStore.Save();
            ShowDownloadsFlyout(DownloadsButton);
        }
        else if (mode == "history")
        {
            ShowHistoryFlyout(MenuButton);
        }
        else
        {
            App.Services.Settings.FavoritesDisplayMode = "Floating";
            App.Services.SettingsStore.Save();
            ShowFavoritesFlyout(FavoritesButton);
        }
    }

    private void ShowHistoryFlyout(FrameworkElement anchor)
    {
        OpenSidebar("history");
    }

    private void ShowDownloadsFlyout(FrameworkElement anchor)
    {
        OpenSidebar("downloads");
    }

    private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _historyFilterTimer.Stop();
        _historyFilterTimer.Start();
    }

    private void HistoryAllTab_Click(object sender, RoutedEventArgs e)
    {
        _historyRecentOnly = false;
        ApplyHistoryFilter();
    }

    private void HistoryRecentTab_Click(object sender, RoutedEventArgs e)
    {
        _historyRecentOnly = true;
        ApplyHistoryFilter();
    }

    private void ApplyHistoryFilter()
    {
        if (HistoryList is null || HistorySearchBox is null)
        {
            return;
        }
        UpdateHistoryTabVisuals();

        IEnumerable<HistoryEntry> source = _historyRecentOnly
            ? _recentlyClosed
            : App.Services.History.Items;
        var query = HistorySearchBox.Text.Trim();
        if (query.Length > 0)
        {
            source = source.Where(item =>
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Url.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        BuildHistoryGroups(source.ToList());
    }

    private void UpdateHistoryTabVisuals()
    {
        if (HistoryAllTab is null || HistoryRecentTab is null)
        {
            return;
        }
        var selected = new SolidColorBrush(Windows.UI.Color.FromArgb(46, 31, 112, 184));
        var transparent = new SolidColorBrush(Colors.Transparent);
        HistoryAllTab.Background = _historyRecentOnly ? transparent : selected;
        HistoryRecentTab.Background = _historyRecentOnly ? selected : transparent;
        HistoryAllTab.FontWeight = _historyRecentOnly
            ? Microsoft.UI.Text.FontWeights.Normal
            : Microsoft.UI.Text.FontWeights.SemiBold;
        HistoryRecentTab.FontWeight = _historyRecentOnly
            ? Microsoft.UI.Text.FontWeights.SemiBold
            : Microsoft.UI.Text.FontWeights.Normal;
    }

    private static ImageSource? CreateHistoryFavicon(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var host = Uri.EscapeDataString(uri.Host);
        var sources = new[]
        {
            $"https://{uri.Host}/favicon.ico",
            $"https://favicon.im/{host}",
            $"https://icons.duckduckgo.com/ip3/{host}.ico",
            $"https://www.google.com/s2/favicons?domain={host}&sz=32"
        };

        var image = new BitmapImage();
        var index = 0;
        image.ImageFailed += (_, _) =>
        {
            index++;
            if (index < sources.Length)
            {
                image.UriSource = new Uri(sources[index]);
            }
        };
        image.UriSource = new Uri(sources[0]);
        return image;
    }

    private void BuildHistoryGroups(List<HistoryEntry> items)
    {
        HistoryGroupedPanel.Children.Clear();
        if (items.Count == 0)
        {
            HistoryGroupedPanel.Children.Add(new TextBlock
            {
                Text = "还没有浏览记录",
                FontSize = 13,
                Opacity = 0.6,
                Margin = new Thickness(8, 22, 8, 8)
            });
            return;
        }

        string? lastLabel = null;
        foreach (var entry in items.OrderByDescending(item => item.VisitedAt))
        {
            var today = DateTime.Today;
            var label = entry.VisitedAt.Date == today
                ? "今天"
                : entry.VisitedAt.Date == today.AddDays(-1)
                    ? "昨天"
                    : entry.VisitedAt.ToString("M月d日 dddd");
            if (label != lastLabel)
            {
                lastLabel = label;
                HistoryGroupedPanel.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(6, 14, 6, 6)
                });
            }

            var row = new Grid { Height = 38, ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var favicon = CreateHistoryFavicon(entry.Url);
            if (favicon is not null)
            {
                var icon = new Image
                {
                    Source = favicon,
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                row.Children.Add(icon);
            }
            var open = new Button
            {
                Content = new TextBlock { Text = entry.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4)
            };
            open.Click += (_, _) =>
            {
                NavigateActive(entry.Url);
                AnimateSidebar(false);
            };
            Grid.SetColumn(open, 1);
            row.Children.Add(open);
            var time = new TextBlock
            {
                Text = entry.VisitedAt.ToString("HH:mm"),
                FontSize = 11,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(time, 2);
            row.Children.Add(time);
            var menu = new MenuFlyout();
            var remove = new MenuFlyoutItem { Text = "删除记录" };
            remove.Click += (_, _) =>
            {
                App.Services.History.Remove(entry);
                ApplyHistoryFilter();
            };
            menu.Items.Add(remove);
            row.ContextFlyout = menu;
            HistoryGroupedPanel.Children.Add(row);
        }
    }

    private void SidebarDismissLayer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (SidebarPane.Visibility == Visibility.Visible)
        {
            CloseSidebar_Click(sender, new RoutedEventArgs());
        }
    }

    private void SidebarSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_sidebarMode == "history")
        {
            HistorySearchBox.Focus(FocusState.Programmatic);
            return;
        }

        var box = new TextBox
        {
            Width = 240,
            PlaceholderText = _sidebarMode == "downloads" ? "搜索下载" : "搜索收藏",
            Margin = new Thickness(8)
        };
        var flyout = new Flyout
        {
            Content = box,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        box.TextChanged += (_, _) =>
        {
            var query = box.Text.Trim();
            if (_sidebarMode == "downloads")
            {
                DownloadsList.ItemsSource = query.Length == 0
                    ? Downloads
                    : Downloads.Where(item =>
                        item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else
            {
                FavoritesList.ItemsSource = query.Length == 0
                    ? App.Services.Favorites.Items
                    : App.Services.Favorites.Items.Where(item =>
                        item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        item.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        };
        flyout.Closed += (_, _) =>
        {
            DownloadsList.ItemsSource = Downloads;
            FavoritesList.ItemsSource = App.Services.Favorites.Items;
        };
        flyout.ShowAt((FrameworkElement)sender);
        box.Focus(FocusState.Programmatic);
    }

    private void SidebarTrash_Click(object sender, RoutedEventArgs e)
    {
        App.Services.History.Clear();
        ApplyHistoryFilter();
        SetTransientStatus("浏览历史已清除");
    }

    private void SidebarMore_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = CreateMaterialMenuStyle()
        };
        var settings = new MenuFlyoutItem { Text = "打开设置" };
        settings.Click += (_, _) => OpenSettings(_sidebarMode);
        flyout.Items.Add(settings);
        flyout.ShowAt((FrameworkElement)sender);
    }

    private LibraryWindow? _favoritesLibraryWindow;
    private LibraryWindow? _historyLibraryWindow;
    private LibraryWindow? _downloadsLibraryWindow;

    public void OpenLibrarySidebar(string mode) => OpenSidebar(mode);

    public void OpenLibrarySettings(string mode) => OpenSettings(mode);

    private void OpenLibraryWindow(string mode)
    {
        LibraryWindow? window = mode switch
        {
            "favorites" => _favoritesLibraryWindow,
            "history" => _historyLibraryWindow,
            _ => _downloadsLibraryWindow
        };

        if (window is not null)
        {
            window.Activate();
            return;
        }

        window = new LibraryWindow(this, mode);
        window.Closed += (_, _) =>
        {
            switch (mode)
            {
                case "favorites": _favoritesLibraryWindow = null; break;
                case "history": _historyLibraryWindow = null; break;
                default: _downloadsLibraryWindow = null; break;
            }
        };
        switch (mode)
        {
            case "favorites": _favoritesLibraryWindow = window; break;
            case "history": _historyLibraryWindow = window; break;
            default: _downloadsLibraryWindow = window; break;
        }
        window.Activate();
    }

    private void ShowFavoritesFlyout(FrameworkElement anchor)
    {
        OpenSidebar("favorites");
    }

    private async void EditFavoriteFlyout_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string url)
        {
            return;
        }

        var entry = App.Services.Favorites.Find(url);
        if (entry is null)
        {
            return;
        }

        var titleBox = new TextBox { Header = "标题", Text = entry.Title };
        var folderBox = new TextBox { Header = "收藏夹", Text = entry.Folder };
        var urlBox = new TextBox { Header = "网址", Text = entry.Url };
        var panel = new StackPanel { Spacing = 10, Width = 420 };
        panel.Children.Add(titleBox);
        panel.Children.Add(folderBox);
        panel.Children.Add(urlBox);

        var dialog = new ContentDialog
        {
            Title = "编辑收藏",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            entry.Title = string.IsNullOrWhiteSpace(titleBox.Text) ? entry.Url : titleBox.Text.Trim();
            entry.Folder = string.IsNullOrWhiteSpace(folderBox.Text) ? entry.Folder : folderBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(urlBox.Text))
            {
                entry.Url = urlBox.Text.Trim();
            }

            var index = App.Services.Favorites.Items.IndexOf(entry);
            if (index >= 0)
            {
                App.Services.Favorites.Items.RemoveAt(index);
                App.Services.Favorites.Items.Insert(index, entry);
            }
            App.Services.Favorites.Save();
            RefreshFavoritesPanel();
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not edit favorite from flyout.", ex);
        }
    }

    private void DeleteFavoriteFlyout_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string url)
        {
            return;
        }

        var entry = App.Services.Favorites.Find(url);
        if (entry is not null)
        {
            App.Services.Favorites.Remove(entry);
            RefreshFavoritesPanel();
        }
    }

    private void FavoritesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FavoriteEntry entry)
        {
            NavigateActive(entry.Url);
            AnimateSidebar(false);
        }
    }

    private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HistoryEntry entry)
        {
            NavigateActive(entry.Url);
            AnimateSidebar(false);
        }
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string url)
        {
            return;
        }

        var entry = App.Services.Favorites.Items.FirstOrDefault(
            item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            App.Services.Favorites.Remove(entry);
        }
    }

    private void RemoveHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string url)
        {
            return;
        }

        var entry = App.Services.History.Items.FirstOrDefault(
            item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            App.Services.History.Remove(entry);
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string path)
        {
            return;
        }

        var item = Downloads.FirstOrDefault(download =>
            string.Equals(download.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        try
        {
            item.Operation?.Cancel();
        }
        catch
        {
        }

        item.IsFailed = true;
        item.IsComplete = false;
        item.State = "已取消";
        try
        {
            if (File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
            }
        }
        catch
        {
        }
    }

    private void DeleteDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;
        var item = Downloads.FirstOrDefault(download =>
            string.Equals(download.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        Downloads.Remove(item);
        SaveDownloadHistory();
        SetTransientStatus("已从下载列表移除：" + item.FileName, 4);
    }

    private void OpenDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            OpenDownloadFile(path);
        }
    }

    private void RevealDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            RevealDownloadFile(path);
        }
    }

    private void ZoomOutOverlayButton_Click(object sender, RoutedEventArgs e) =>
        SetBrowserZoom(App.Services.Settings.BrowserZoom - 10);

    private void ZoomInOverlayButton_Click(object sender, RoutedEventArgs e) =>
        SetBrowserZoom(App.Services.Settings.BrowserZoom + 10);

    private void ToolbarExpandButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.Settings;
        var flyout = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = CreateMaterialMenuStyle(),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };

        var count = 0;
        void AddItem(string text, string glyph, Action action)
        {
            var item = new MenuFlyoutItem
            {
                Text = Localization.T(text),
                Icon = new FontIcon
                {
                    Glyph = glyph,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets")
                }
            };
            item.Click += (_, _) =>
            {
                flyout.Hide();
                action();
            };
            flyout.Items.Add(item);
            count++;
        }

        if (!settings.ShowHomeButton)
        {
            AddItem("主页", "\uE80F", () => HomeButton_Click(ToolbarExpandButton, new RoutedEventArgs()));
        }
        if (!settings.ShowFavoriteButton)
        {
            AddItem("收藏此页", "\uE734", () => AddOrShowFavorite(ToolbarExpandButton));
        }
        if (!settings.ShowShieldButton)
        {
            AddItem("广告拦截", "\uE730", () => ShowShieldFlyout(ToolbarExpandButton));
        }
        if (!settings.ShowPasswordsButton)
        {
            AddItem("密码管理器", "\uE192", () => ShowPasswordsFlyout(ToolbarExpandButton));
        }
        if (!settings.ShowFavoritesButton)
        {
            AddItem("收藏夹", "\uE728", () => OpenSidebar("favorites"));
        }
        if (!settings.ShowExtensionsButton)
        {
            AddItem("扩展", "\uE8F1", () => OpenSettings("extensions"));
        }
        if (!settings.ShowDownloadsButton)
        {
            AddItem("下载内容", "\uE896", () => OpenSidebar("downloads"));
        }
        if (!settings.ShowHistoryButton)
        {
            AddItem("历史记录", "\uE81C", () => OpenSidebar("history"));
        }
        if (!settings.ShowTranslateButton)
        {
            AddItem("翻译", "\uE8C1", () => TranslateButton_Click(ToolbarExpandButton, new RoutedEventArgs()));
        }
        if (!settings.ShowProxyButton)
        {
            AddItem("代理", "\uE774", () => ProxyButton_Click(ToolbarExpandButton, new RoutedEventArgs()));
        }
        if (!settings.ShowGhostButton)
        {
            AddItem("Ghost Downloader", "\uE753", () => GhostButton_Click(ToolbarExpandButton, new RoutedEventArgs()));
        }


        if (AddExtensionMenuItems(flyout))
        {
            count++;
        }

        if (count == 0)
        {
            return;
        }

        flyout.ShowAt(ToolbarExpandButton, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        });
    }

    private async void CaptureWebsiteScreenshot()
    {
        var tab = _activeTab;
        if (tab?.Core is null)
        {
            SetTransientStatus("当前页面暂不可截图");
            return;
        }

        try
        {
            var json = await tab.Core.CallDevToolsProtocolMethodAsync(
                "Page.captureScreenshot",
                "{\"format\":\"png\",\"captureBeyondViewport\":true}");
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var dataElement))
            {
                SetTransientStatus("网站截图失败");
                return;
            }

            var bytes = Convert.FromBase64String(dataElement.GetString() ?? string.Empty);
            _pendingScreenshotBytes = bytes;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bitmap.SetSourceAsync(stream);
            ScreenshotPreviewImage.Source = bitmap;
            ScreenshotPreviewPanel.Visibility = Visibility.Visible;
            SetTransientStatus("网页截图已完成，可保存到指定位置");
        }
        catch (Exception ex)
        {
            Logger.Warn("Website screenshot failed.", ex);
            SetTransientStatus("网站截图失败");
        }
    }

    private void CloseScreenshotPreview_Click(object sender, RoutedEventArgs e)
    {
        ScreenshotPreviewPanel.Visibility = Visibility.Collapsed;
        ScreenshotPreviewImage.Source = null;
        _pendingScreenshotBytes = null;
    }

    private async void SaveScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingScreenshotBytes is null)
        {
            return;
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = $"EXET 网页截图 {DateTime.Now:yyyy-MM-dd HH-mm-ss}"
            };
            picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await FileIO.WriteBytesAsync(file, _pendingScreenshotBytes);
            SetTransientStatus($"网页截图已保存：{file.Path}");
            CloseScreenshotPreview_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            Logger.Warn("Saving website screenshot failed.", ex);
            SetTransientStatus("保存网页截图失败");
        }
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        HideAddressSuggestions();
        var browserSettings = App.Services.Settings;
        var flyout = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = CreateMaterialMenuStyle()
        };
        var newTab = new MenuFlyoutItem { Text = "新建标签页", Icon = new FontIcon { Glyph = "\uE710" } };
        newTab.Click += (_, _) => CreateTab(null, true);
        var privateTab = new MenuFlyoutItem { Text = "新建无痕标签页", Icon = new FontIcon { Glyph = "\uE727" } };
        privateTab.KeyboardAcceleratorTextOverride = "Ctrl+Shift+N";
        privateTab.Click += (_, _) => CreatePrivateTab();
        var newWindow = new MenuFlyoutItem { Text = "新建窗口", Icon = new FontIcon { Glyph = "\uE737" } };
        newWindow.Click += (_, _) =>
        {
            var window = new MainWindow();
            window.Activate();
        };
        var nightMode = new MenuFlyoutItem
        {
            Text = App.Services.Settings.NightMode ? "关闭夜间模式" : "夜间模式",
            Icon = new FontIcon { Glyph = "\uE708" }
        };
        nightMode.Click += (_, _) => ToggleNightMode();
        var layout = new MenuFlyoutItem
        {
            Text = App.Services.Settings.VerticalTabs
                ? "\u5207\u6362\u5230\u6c34\u5e73\u6807\u7b7e\u9875"
                : "\u5207\u6362\u5230\u5782\u76f4\u6807\u7b7e\u9875",
            Icon = new FontIcon { Glyph = "\uE8A0" }
        };
        layout.Click += (_, _) => ToggleVerticalTabs();
        var zoomMenu = new MenuFlyoutSubItem
        {
            Text = $"缩放  {Math.Clamp(browserSettings.BrowserZoom, 25, 300)}%"
        };
        foreach (var percent in new[] { 50, 75, 100, 125, 150, 200 })
        {
            var zoomItem = new ToggleMenuFlyoutItem
            {
                Text = $"{percent}%",
                IsChecked = Math.Clamp(browserSettings.BrowserZoom, 25, 300) == percent
            };
            zoomItem.Click += (_, _) => SetBrowserZoom(percent);
            zoomMenu.Items.Add(zoomItem);
        }

        zoomMenu.Items.Add(new MenuFlyoutSeparator());
        var zoomIn = new MenuFlyoutItem { Text = "放大 10%" };
        zoomIn.Click += (_, _) => SetBrowserZoom(browserSettings.BrowserZoom + 10);
        var zoomOut = new MenuFlyoutItem { Text = "缩小 10%" };
        zoomOut.Click += (_, _) => SetBrowserZoom(browserSettings.BrowserZoom - 10);
        var zoomReset = new MenuFlyoutItem { Text = "重置为 100%" };
        zoomReset.Click += (_, _) => SetBrowserZoom(100);
        zoomMenu.Items.Add(zoomIn);
        zoomMenu.Items.Add(zoomOut);
        zoomMenu.Items.Add(zoomReset);

        var favorites = new MenuFlyoutItem { Text = "收藏夹", Icon = new FontIcon { Glyph = "\uE728" } };
        favorites.Click += (_, _) => OpenSidebar("favorites");
        var history = new MenuFlyoutItem { Text = "历史记录", Icon = new FontIcon { Glyph = "\uE81C" } };
        history.Click += (_, _) => OpenSidebar("history");
        var downloads = new MenuFlyoutItem { Text = "下载内容", Icon = new FontIcon { Glyph = "\uE896" } };
        downloads.Click += (_, _) => OpenSidebar("downloads");
        var taskManager = new MenuFlyoutItem { Text = "浏览器任务管理器", Icon = new FontIcon { Glyph = "\uE9D9" } };
        taskManager.Click += (_, _) => OpenTaskManager();
        var settings = new MenuFlyoutItem { Text = "设置", Icon = new FontIcon { Glyph = "\uE713" } };
        settings.Click += (_, _) => OpenSettings("general");
        var extensions = new MenuFlyoutItem { Text = "扩展", Icon = new FontIcon { Glyph = "\uE8F1" } };
        extensions.Click += (_, _) => OpenSettings("extensions");
        var passwords = new MenuFlyoutItem { Text = "密码管理器", Icon = new FontIcon { Glyph = "\uE192" } };
        passwords.Click += (_, _) => OpenSettings("passwords");
        var devTools = new MenuFlyoutItem { Text = "开发者工具", Icon = new FontIcon { Glyph = "\uE943" } };
        devTools.Click += (_, _) => _activeTab?.Core?.OpenDevToolsWindow();
        var exit = new MenuFlyoutItem { Text = "退出 EXET", Icon = new FontIcon { Glyph = "\uE8BB" } };
        exit.Click += (_, _) => ExitApplication();

        flyout.Items.Add(newTab);
        flyout.Items.Add(privateTab);
        flyout.Items.Add(newWindow);
        flyout.Items.Add(nightMode);
        flyout.Items.Add(layout);
        flyout.Items.Add(zoomMenu);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(favorites);
        flyout.Items.Add(history);
        flyout.Items.Add(downloads);
        var screenshot = new MenuFlyoutItem
        {
            Text = "网站截图",
            Icon = new FontIcon { Glyph = "\uE722" }
        };
        screenshot.Click += (_, _) =>
        {
            flyout.Hide();
            CaptureWebsiteScreenshot();
        };
        flyout.Items.Add(screenshot);
        flyout.Items.Add(taskManager);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(settings);
        flyout.Items.Add(passwords);
        flyout.Items.Add(extensions);
        flyout.Items.Add(devTools);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(exit);
        LocalizeMenuItems(flyout.Items);
        flyout.ShowAt(MenuButton);
    }

    private static void LocalizeMenuItems(IEnumerable<MenuFlyoutItemBase> items)
    {
        foreach (var item in items)
        {
            if (item is MenuFlyoutSubItem submenu)
            {
                submenu.Text = Localization.T(submenu.Text);
                LocalizeMenuItems(submenu.Items);
            }
            else if (item is MenuFlyoutItem menuItem)
            {
                menuItem.Text = Localization.T(menuItem.Text);
            }
        }
    }

    private void SettingsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenSettings("general");
        args.Handled = true;
    }

    private void TaskManagerAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenTaskManager();
        args.Handled = true;
    }

    private void OpenTaskManager()
    {
        if (_taskManager is not null)
        {
            _taskManager.Activate();
            return;
        }

        _taskManager = new TaskManagerWindow();
        _taskManager.Closed += (_, _) => _taskManager = null;
        _taskManager.Activate();
    }

    private async Task ClearHistoryAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "清除浏览历史？",
            Content = "此操作会删除全部本地浏览历史记录，无法撤销。",
            PrimaryButtonText = "清除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Services.History.Clear();
            SetTransientStatus("浏览历史已清除");
        }
    }

    private async Task ClearFavoritesAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "清除全部收藏？",
            Content = "此操作会删除全部收藏夹条目，无法撤销。",
            PrimaryButtonText = "清除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Services.Favorites.Clear();
            SetTransientStatus("收藏夹已清空");
        }
    }

    private void ShieldButton_Click(object sender, RoutedEventArgs e) =>
        ShowShieldFlyout(ShieldButton);

    private void ShowShieldFlyout(FrameworkElement anchor)
    {
        var panel = new StackPanel { Width = 320, Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "广告拦截",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var enabled = new ToggleSwitch
        {
            IsOn = App.Services.AdBlock.Enabled,
            OnContent = "已启用",
            OffContent = "已停用",
            Header = "请求级广告过滤"
        };
        enabled.Toggled += (_, _) =>
        {
            if (App.Services.AdBlock.Enabled == enabled.IsOn)
            {
                return;
            }

            App.Services.AdBlock.Enabled = enabled.IsOn;
            App.Services.AdBlock.NotifySettingsChanged();
            _activeTab?.Core?.Reload();
            UpdateStatusBar();
        };
        panel.Children.Add(enabled);

        panel.Children.Add(new TextBlock
        {
            Text = $"本页已拦截 {_activeTab?.BlockedCount ?? 0} 项 · 累计 {App.Services.AdBlock.TotalBlocked:N0} 项",
            FontSize = 12,
            Opacity = 0.7
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{App.Services.AdBlock.RuleCount:N0} 条规则 · {App.Services.AdBlock.LastUpdate}",
            FontSize = 11,
            Opacity = 0.55
        });

        if (_activeTab is not null &&
            Uri.TryCreate(_activeTab.Url, UriKind.Absolute, out var pageUri))
        {
            var siteToggle = new ToggleSwitch
            {
                IsOn = App.Services.AdBlock.IsSiteAllowed(pageUri),
                Header = "在此网站暂停过滤",
                OnContent = "已暂停",
                OffContent = "过滤中"
            };
            siteToggle.Toggled += (_, _) =>
            {
                if (App.Services.AdBlock.IsSiteAllowed(pageUri) == siteToggle.IsOn)
                {
                    return;
                }

                App.Services.AdBlock.SetSiteAllowed(pageUri, siteToggle.IsOn);
                _activeTab.Core?.Reload();
                UpdateShieldState();
            };
            panel.Children.Add(siteToggle);
        }

        var update = new Button
        {
            Content = "更新拦截规则",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        update.Click += async (_, _) =>
        {
            update.IsEnabled = false;
            try
            {
                await App.Services.AdBlock.UpdateFiltersAsync(
                    new Progress<string>(message => SetTransientStatus(message, 3)));
                UpdateStatusBar();
            }
            finally
            {
                update.IsEnabled = true;
            }
        };
        panel.Children.Add(update);

        var more = new Button
        {
            Content = "更多设置",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        more.Click += (_, _) => OpenSettings("adguard");
        panel.Children.Add(more);

        var flyout = new Flyout
        {
            Content = panel,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        var target = anchor is { Visibility: Visibility.Visible, IsLoaded: true }
            ? anchor
            : MenuButton;
        flyout.ShowAt(target);
    }

    private void OpenSidebar(string mode)
    {
        if (_settingsHost?.Visibility == Visibility.Visible)
        {
            CloseInlineSettings();
        }
        var previousMode = _sidebarMode;
        var wasOpen = _sidebarTargetOpen;
        _sidebarMode = mode;
        var docked = mode switch
        {
            "favorites" => string.Equals(App.Services.Settings.FavoritesDisplayMode, "Sidebar", StringComparison.OrdinalIgnoreCase),
            "downloads" => string.Equals(App.Services.Settings.DownloadsDisplayMode, "Sidebar", StringComparison.OrdinalIgnoreCase),
            "history" => string.Equals(App.Services.Settings.HistoryDisplayMode, "Sidebar", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        ApplySidebarPresentation(docked);

        var wasVisible = SidebarPane.Visibility == Visibility.Visible;
        if (!_sidebarTargetOpen || !string.Equals(previousMode, mode, StringComparison.Ordinal))
        {
            AnimateSidebar(true);
        }
        else
        {
            SidebarPane.Visibility = Visibility.Visible;
        }
        SidebarTitle.Text = mode switch
        {
            "favorites" => "收藏夹",
            "history" => "历史记录",
            "downloads" => "下载内容",
            _ => "侧栏"
        };

        FavoritesList.Visibility = mode == "favorites" ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = Visibility.Collapsed;
        HistoryGroupedPanel.Visibility = mode == "history" ? Visibility.Visible : Visibility.Collapsed;
        DownloadsList.Visibility = mode == "downloads" ? Visibility.Visible : Visibility.Collapsed;
        HistoryChrome.Visibility = mode == "history" ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Margin = mode == "history" ? new Thickness(0, 92, 0, 0) : new Thickness(0);
        SidebarTrashButton.Visibility = mode == "history" ? Visibility.Visible : Visibility.Collapsed;
        SidebarAddFavoriteButton.Visibility = mode == "favorites" ? Visibility.Visible : Visibility.Collapsed;
        SidebarNewFolderButton.Visibility = mode == "favorites" ? Visibility.Visible : Visibility.Collapsed;
        FavoriteFoldersChrome.Visibility = mode == "favorites" ? Visibility.Visible : Visibility.Collapsed;
        FavoriteBackButton.Visibility = Visibility.Collapsed;
        FavoriteFoldersPanel.Visibility = Visibility.Visible;
        FavoritesList.Margin = new Thickness(0);
        if (mode == "favorites")
        {
            FavoritesList.Visibility = Visibility.Collapsed;
            FavoritesList.ItemsSource = Favorites;
            BuildFavoriteFolders();
        }
        if (mode == "history")
        {
            _historyRecentOnly = false;
            ApplyHistoryFilter();
        }

        if (wasOpen && _sidebarTargetOpen &&
            !string.Equals(previousMode, mode, StringComparison.Ordinal))
        {
            AnimateSidebarContent();
        }
    }

    private void AnimateSidebarContent()
    {
        SidebarContentTransform.Y = -14;
        SidebarContentRoot.Opacity = 0;
        var duration = new Duration(TimeSpan.FromMilliseconds(
            Math.Max(140, App.Services.Settings.AnimationDuration)));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation
        {
            From = -14,
            To = 0,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(slide, SidebarContentTransform);
        Storyboard.SetTargetProperty(slide, "Y");
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(fade, SidebarContentRoot);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }

    private void BrowserHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // WebView2 controls stretch with the host Grid.
    }

    public void RefreshStatusBar() => UpdateStatusBar();

    public void OpenUrlFromSettings(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            NavigateActive(url);
            Activate();
        }
    }

    public void NotifyExtensionsChanged()
    {
        _extensionReloadTimer.Stop();
        _extensionReloadTimer.Start();
    }

    private void NewTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CreateTab(null, true);
        args.Handled = true;
    }

    private void NewPrivateTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CreatePrivateTab();
        args.Handled = true;
    }

    private void CloseTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CloseActiveTab();
        args.Handled = true;
    }

    private void FocusAddressAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        AddressBox.Focus(FocusState.Programmatic);
        AddressBox.SelectAll();
        args.Handled = true;
    }

    private void ReloadAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _activeTab?.Core?.Reload();
        args.Handled = true;
    }

    private void FavoriteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FavoriteButton_Click(sender, new RoutedEventArgs());
        args.Handled = true;
    }

    private void DownloadsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenSidebar("downloads");
        args.Handled = true;
    }

    private void HistoryAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenSidebar("history");
        args.Handled = true;
    }

    private void FullScreenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ToggleFullScreen();
        args.Handled = true;
    }

    private void DevToolsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _activeTab?.Core?.OpenDevToolsWindow();
        args.Handled = true;
    }

    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        BackButton_Click(sender, new RoutedEventArgs());
        args.Handled = true;
    }

    private void ForwardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ForwardButton_Click(sender, new RoutedEventArgs());
        args.Handled = true;
    }
}



