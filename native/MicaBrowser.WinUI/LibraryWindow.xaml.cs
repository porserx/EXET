using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MicaBrowser.Backdrop;
using MicaBrowser.Models;
using MicaBrowser.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;

namespace MicaBrowser;

public sealed partial class LibraryWindow : Window
{
    private readonly MainWindow _owner;
    private readonly string _mode;
    private readonly AppWindow _appWindow;
    private DispatcherTimer? _refreshTimer;

    public LibraryWindow(MainWindow owner, string mode)
    {
        _owner = owner;
        _mode = mode;
        InitializeComponent();

        Title = mode switch
        {
            "favorites" => "收藏夹",
            "history" => "历史记录",
            "downloads" => "下载",
            _ => "EXET"
        };
        TitleText.Text = Title;

        var hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.Title = Title;
        _appWindow.Resize(new SizeInt32(430, 820));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        SystemBackdrop = new MicaBackdrop
        {
            Kind = App.Services.Settings.MicaMaterial == "Alt"
                ? MicaKind.BaseAlt
                : MicaKind.Base
        };

        var dark = AppSurface.IsDark(
            App.Services.Settings.NightMode,
            App.Services.Settings.ThemeMode,
            ElementTheme.Default);
        RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;

        SidebarButton.Click += (_, _) =>
        {
            _owner.OpenLibrarySidebar(_mode);
            Close();
        };
        SettingsButton.Click += (_, _) =>
        {
            _owner.OpenLibrarySettings(_mode);
            Close();
        };
        CloseButton.Click += (_, _) => Close();

        Closed += (_, _) => _refreshTimer?.Stop();
        ShowMode();
        PositionBesideOwner();
    }

    private void PositionBesideOwner()
    {
        try
        {
            var ownerHwnd = WindowNative.GetWindowHandle(_owner);
            var ownerWindow = AppWindow.GetFromWindowId(
                Win32Interop.GetWindowIdFromWindow(ownerHwnd));
            var display = DisplayArea.GetFromWindowId(
                ownerWindow.Id, DisplayAreaFallback.Nearest);
            var work = display.WorkArea;
            var width = Math.Min(430, work.Width);
            var height = Math.Min(820, work.Height);
            var x = Math.Clamp(
                ownerWindow.Position.X + ownerWindow.Size.Width - width - 24,
                work.X,
                work.X + work.Width - width);
            var y = Math.Clamp(
                ownerWindow.Position.Y + 64,
                work.Y,
                work.Y + work.Height - height);
            _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
        catch
        {
        }
    }

    private void ShowMode()
    {
        _refreshTimer?.Stop();
        ContentHost.Children.Clear();
        switch (_mode)
        {
            case "favorites":
                BuildFavorites();
                break;
            case "history":
                BuildHistory();
                break;
            default:
                BuildDownloads();
                break;
        }
    }

    private ScrollViewer CreateScroll(UIElement content) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = content
    };

    private static TextBlock EmptyText(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Opacity = 0.65,
        Margin = new Thickness(4, 28, 4, 4)
    };

    private void BuildFavorites()
    {
        var panel = new StackPanel { Spacing = 10 };
        var favorites = App.Services.Favorites.Items.ToList();
        foreach (var folder in favorites
                     .GroupBy(item => string.IsNullOrWhiteSpace(item.Folder) ? "收藏夹" : item.Folder)
                     .OrderBy(group => group.Key == "收藏夹" ? 0 : 1))
        {
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(4, 12, 4, 4)
            };
            header.Children.Add(new FontIcon
            {
                Glyph = "\uE8B7",
                FontSize = 15,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            });
            header.Children.Add(new TextBlock
            {
                Text = folder.Key,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            panel.Children.Add(header);

            foreach (var entry in folder.OrderBy(item => item.Title))
            {
                var row = new Grid { Height = 46, ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                    Child = new TextBlock
                    {
                        Text = "★",
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                row.Children.Add(icon);

                var open = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(2, 2, 2, 2),
                    Content = new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock { Text = entry.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock
                            {
                                Text = entry.Host,
                                FontSize = 11,
                                Opacity = 0.6,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        }
                    }
                };
                Grid.SetColumn(open, 1);
                open.Click += (_, _) => _owner.OpenUrlFromSettings(entry.Url);
                row.Children.Add(open);

                var more = new Button
                {
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Content = new FontIcon { Glyph = "\uE712", FontSize = 11 }
                };
                var menu = new MenuFlyout();
                var openItem = new MenuFlyoutItem { Text = "打开" };
                openItem.Click += (_, _) => _owner.OpenUrlFromSettings(entry.Url);
                menu.Items.Add(openItem);
                var copy = new MenuFlyoutItem { Text = "复制链接" };
                copy.Click += (_, _) => CopyText(entry.Url);
                menu.Items.Add(copy);
                var remove = new MenuFlyoutItem { Text = "删除收藏" };
                remove.Click += (_, _) =>
                {
                    App.Services.Favorites.Remove(entry);
                    ShowMode();
                };
                menu.Items.Add(remove);
                more.Flyout = menu;
                Grid.SetColumn(more, 2);
                row.Children.Add(more);
                panel.Children.Add(row);
            }
        }

        if (favorites.Count == 0)
        {
            panel.Children.Add(EmptyText("还没有收藏"));
        }

        ContentHost.Children.Add(CreateScroll(panel));
    }

    private void BuildHistory()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var search = new TextBox
        {
            PlaceholderText = "搜索历史记录",
            Margin = new Thickness(0, 4, 0, 10),
            Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8)
        };
        Grid.SetRow(search, 0);
        root.Children.Add(search);

        var panel = new StackPanel { Spacing = 2 };
        var scroll = CreateScroll(panel);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        ContentHost.Children.Add(root);

        void Rebuild()
        {
            panel.Children.Clear();
            var query = search.Text.Trim();
            var items = App.Services.History.Items
                .Where(item => query.Length == 0 ||
                               item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               item.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.VisitedAt)
                .ToList();

            if (items.Count == 0)
            {
                panel.Children.Add(EmptyText(query.Length == 0 ? "还没有浏览记录" : "没有匹配的历史记录"));
                return;
            }

            string? lastDate = null;
            foreach (var entry in items)
            {
                var date = entry.VisitedAt.ToString("yyyy年M月d日 dddd");
                if (date != lastDate)
                {
                    lastDate = date;
                    panel.Children.Add(new TextBlock
                    {
                        Text = date,
                        FontSize = 13,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Margin = new Thickness(4, 12, 4, 5)
                    });
                }

                var row = new Grid { Height = 42, ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var open = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 2, 4, 2),
                    Content = new TextBlock
                    {
                        Text = entry.Title,
                        FontSize = 13,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                };
                open.Click += (_, _) => _owner.OpenUrlFromSettings(entry.Url);
                row.Children.Add(open);
                var time = new TextBlock
                {
                    Text = entry.VisitedAt.ToString("HH:mm"),
                    FontSize = 11,
                    Opacity = 0.55,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(time, 1);
                row.Children.Add(time);
                var flyout = new MenuFlyout();
                var copy = new MenuFlyoutItem { Text = "复制链接" };
                copy.Click += (_, _) => CopyText(entry.Url);
                flyout.Items.Add(copy);
                var remove = new MenuFlyoutItem { Text = "删除记录" };
                remove.Click += (_, _) =>
                {
                    App.Services.History.Remove(entry);
                    Rebuild();
                };
                flyout.Items.Add(remove);
                row.ContextFlyout = flyout;
                panel.Children.Add(row);
            }
        }

        search.TextChanged += (_, _) => Rebuild();
        Rebuild();
    }

    private void BuildDownloads()
    {
        var panel = new StackPanel { Spacing = 4 };
        ContentHost.Children.Add(CreateScroll(panel));

        void Rebuild()
        {
            panel.Children.Clear();
            foreach (var item in _owner.Downloads.ToList())
            {
                var exists = !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath);
                var row = new Grid
                {
                    Height = 72,
                    ColumnSpacing = 6,
                    Opacity = exists ? 1 : 0.42
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var details = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                details.Children.Add(new TextBlock
                {
                    Text = item.FileName,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                details.Children.Add(new TextBlock
                {
                    Text = exists ? $"{item.State} · {item.EtaText}" : "文件已删除",
                    FontSize = 11,
                    Opacity = 0.62
                });
                if (!item.IsComplete && exists)
                {
                    details.Children.Add(new ProgressBar
                    {
                        Value = item.Progress,
                        Height = 3,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                row.Children.Add(details);

                var actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (item.IsComplete && exists)
                {
                    actions.Children.Add(IconButton("\uE8A7", "打开", () =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FilePath) { UseShellExecute = true }); }
                        catch { }
                    }));
                    actions.Children.Add(IconButton("\uE838", "在资源管理器中显示", () =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{item.FilePath}\"") { UseShellExecute = true }); }
                        catch { }
                    }));
                }
                else if (!item.IsComplete && exists)
                {
                    actions.Children.Add(IconButton("\uE769", "暂停/继续", () =>
                    {
                        if (item.Operation is null) return;
                        if (item.State == "已暂停")
                        {
                            item.Operation.Resume();
                            item.State = "正在下载";
                        }
                        else
                        {
                            item.Operation.Pause();
                            item.State = "已暂停";
                        }
                        Rebuild();
                    }));
                }
                actions.Children.Add(IconButton("\uE74D", "删除", () =>
                {
                    try { if (File.Exists(item.FilePath)) File.Delete(item.FilePath); } catch { }
                    _owner.Downloads.Remove(item);
                    Rebuild();
                }));
                Grid.SetColumn(actions, 2);
                row.Children.Add(actions);
                panel.Children.Add(row);
            }

            if (_owner.Downloads.Count == 0)
            {
                panel.Children.Add(EmptyText("还没有下载"));
            }
        }

        Rebuild();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => Rebuild();
        _refreshTimer.Start();
    }

    private static Button IconButton(string glyph, string tooltip, Action action)
    {
        var button = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 12 }
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
