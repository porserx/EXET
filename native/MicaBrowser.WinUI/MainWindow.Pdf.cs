using MicaBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MicaBrowser;

public sealed partial class MainWindow
{
    private readonly List<PdfPreviewWindow> _pdfWindows = new();

    private static bool IsPdfSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            var path = uri.IsFile ? uri.LocalPath : uri.AbsolutePath;
            return string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(Path.GetExtension(source), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenPdfPreview(string source, string? displayName = null)
    {
        try
        {
            var preview = new PdfPreviewWindow(source, displayName);
            _pdfWindows.Add(preview);
            preview.Closed += (_, _) => _pdfWindows.Remove(preview);
            preview.Activate();
        }
        catch (Exception ex)
        {
            SetTransientStatus($"无法打开 PDF：{ex.Message}", 6);
        }
    }

    private async void OpenPdfFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pdf");
        InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            OpenPdfPreview(file.Path, file.Name);
        }
    }

    private void PreviewDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path && File.Exists(path))
        {
            OpenPdfPreview(path, Path.GetFileName(path));
        }
    }

    private void ClosePdfPreviews()
    {
        foreach (var preview in _pdfWindows.ToList())
        {
            try
            {
                preview.Close();
            }
            catch
            {
                // Best effort while the main window is closing.
            }
        }

        _pdfWindows.Clear();
    }
}
