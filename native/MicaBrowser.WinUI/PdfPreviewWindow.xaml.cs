using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MicaBrowser.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace MicaBrowser;

public sealed partial class PdfPreviewWindow : Window
{
    private readonly string _source;
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private PdfDocument? _document;
    private string? _workingPath;
    private bool _deleteWorkingPath;
    private bool _loaded;
    private bool _fitWidth = true;
    private double _zoom = 1.0;
    private int _pageIndex;
    private int _renderVersion;

    public PdfPreviewWindow(string source, string? displayName = null)
    {
        InitializeComponent();
        _source = source;

        var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1100, 850));

        var theme = App.Services.Settings.NightMode
            ? ElementTheme.Dark
            : App.Services.Settings.ThemeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        RootGrid.RequestedTheme = theme;
        DocumentTitleText.Text = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(source)
            : displayName;

        RootGrid.Loaded += RootGrid_Loaded;
        Closed += (_, _) =>
        {
            _http.Dispose();
            _document = null;
            if (_deleteWorkingPath && !string.IsNullOrWhiteSpace(_workingPath))
            {
                try
                {
                    File.Delete(_workingPath);
                }
                catch
                {
                    // The cache can be cleaned up on a later run.
                }
            }
        };
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        ZoomCombo.SelectedIndex = 0;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            SetError(null);
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;
            StatusText.Text = "正在加载 PDF…";

            _workingPath = await ResolveSourceAsync();
            var file = await StorageFile.GetFileFromPathAsync(_workingPath);
            _document = await PdfDocument.LoadFromFileAsync(file);
            _pageIndex = 0;

            StatusText.Text = $"{_document.PageCount} 页";
            PageText.Text = $"1 / {_document.PageCount}";
            DocumentTitleText.Text = string.IsNullOrWhiteSpace(DocumentTitleText.Text)
                ? Path.GetFileName(_workingPath)
                : DocumentTitleText.Text;
            await RenderCurrentPageAsync();
        }
        catch (Exception ex)
        {
            SetError($"无法打开 PDF：{ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<string> ResolveSourceAsync()
    {
        if (Uri.TryCreate(_source, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            StatusText.Text = "正在下载 PDF…";
            var name = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(name) ||
                !name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                name = "document.pdf";
            }

            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..16];
            var path = Path.Combine(App.Services.Paths.PdfCache, $"{hash}-{safeName}");

            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 100L * 1024 * 1024)
            {
                throw new InvalidOperationException("PDF 文件超过 100 MB。");
            }

            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(path))
            {
                await input.CopyToAsync(output);
            }

            _deleteWorkingPath = true;
            return path;
        }

        if (Uri.TryCreate(_source, UriKind.Absolute, out uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return _source;
    }

    private async Task RenderCurrentPageAsync()
    {
        if (_document is null)
        {
            return;
        }

        var version = ++_renderVersion;
        LoadingRing.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        try
        {
            var viewport = PageScroll.ViewportWidth > 0 ? PageScroll.ViewportWidth - 48 : 900;
            var width = _fitWidth
                ? viewport
                : Math.Max(320, viewport * _zoom);
            width = Math.Clamp(width, 320, 4000);

            using var page = _document.GetPage((uint)_pageIndex);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(
                stream,
                new PdfPageRenderOptions { DestinationWidth = (uint)width });
            if (version != _renderVersion)
            {
                return;
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            PageImage.Source = bitmap;
            PageImage.Width = width;
            PageText.Text = $"{_pageIndex + 1} / {_document.PageCount}";
            PrevButton.IsEnabled = _pageIndex > 0;
            NextButton.IsEnabled = _pageIndex + 1 < (int)_document.PageCount;
            PageScroll.ChangeView(null, 0, null);
        }
        catch (Exception ex)
        {
            SetError($"无法渲染 PDF：{ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void SetError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        StatusText.Text = "PDF 加载失败";
    }

    private async void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || _pageIndex <= 0)
        {
            return;
        }

        _pageIndex--;
        await RenderCurrentPageAsync();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || _pageIndex + 1 >= (int)_document.PageCount)
        {
            return;
        }

        _pageIndex++;
        await RenderCurrentPageAsync();
    }

    private async void ZoomCombo_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (!_loaded || ZoomCombo.SelectedItem is not Microsoft.UI.Xaml.Controls.ComboBoxItem item ||
            item.Tag is not string value)
        {
            return;
        }

        if (value == "fit")
        {
            _fitWidth = true;
            _zoom = 1.0;
        }
        else
        {
            _fitWidth = false;
            _zoom = double.TryParse(value, out var zoom) ? zoom : 1.0;
        }

        await RenderCurrentPageAsync();
    }

    private void OpenExternalButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _workingPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetError($"无法在外部打开：{ex.Message}");
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _renderVersion++;
        await LoadAsync();
    }
}
