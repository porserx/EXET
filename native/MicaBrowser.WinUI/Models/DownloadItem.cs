using MicaBrowser.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace MicaBrowser.Models;

public sealed class DownloadItem : ObservableObject
{
    private long _receivedBytes;
    private long _totalBytes;
    private string _state = "正在下载";
    private bool _isComplete;
    private bool _isFailed;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "download";
    public string FilePath { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public CoreWebView2DownloadOperation? Operation { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public bool IsPdf =>
        string.Equals(Path.GetExtension(FilePath), ".pdf", StringComparison.OrdinalIgnoreCase) ||
        (Uri.TryCreate(SourceUrl, UriKind.Absolute, out var source) &&
         string.Equals(Path.GetExtension(source.AbsolutePath), ".pdf", StringComparison.OrdinalIgnoreCase));

    public Visibility PdfVisibility => IsPdf ? Visibility.Visible : Visibility.Collapsed;

    public long ReceivedBytes
    {
        get => _receivedBytes;
        set
        {
            if (SetProperty(ref _receivedBytes, value))
            {
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(EtaText));
            }
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (SetProperty(ref _totalBytes, value))
            {
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(SizeText));
            }
        }
    }

    public string State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(EtaText));
            }
        }
    }

    public bool IsComplete
    {
        get => _isComplete;
        set
        {
            if (SetProperty(ref _isComplete, value))
            {
                OnPropertyChanged(nameof(CancelVisibility));
                OnPropertyChanged(nameof(DeleteVisibility));
            }
        }
    }

    public bool IsFailed
    {
        get => _isFailed;
        set
        {
            if (SetProperty(ref _isFailed, value))
            {
                OnPropertyChanged(nameof(CancelVisibility));
                OnPropertyChanged(nameof(DeleteVisibility));
            }
        }
    }

    public Visibility DeleteVisibility =>
        CancelVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CancelVisibility =>
        IsComplete || IsFailed || State is "已取消" or "下载已中断"
            ? Visibility.Collapsed
            : Visibility.Visible;

    public double Progress => TotalBytes > 0
        ? Math.Clamp(ReceivedBytes * 100.0 / TotalBytes, 0, 100)
        : (IsComplete ? 100 : 0);

    public string EtaText
    {
        get
        {
            if (IsComplete)
            {
                return "已完成";
            }
            if (IsFailed)
            {
                return "下载失败";
            }
            if (State == "已暂停")
            {
                return "已暂停";
            }
            if (ReceivedBytes <= 0 || TotalBytes <= 0)
            {
                return "计算预计时间…";
            }

            var elapsed = (DateTime.Now - StartedAt).TotalSeconds;
            if (elapsed < 1)
            {
                return "计算预计时间…";
            }

            var speed = ReceivedBytes / elapsed;
            if (speed <= 0)
            {
                return "计算预计时间…";
            }

            var remaining = (TotalBytes - ReceivedBytes) / speed;
            var eta = TimeSpan.FromSeconds(Math.Max(0, remaining));
            return eta.TotalHours >= 1
                ? $"剩余 {eta.Hours} 小时 {eta.Minutes} 分"
                : eta.TotalMinutes >= 1
                    ? $"剩余 {eta.Minutes} 分 {eta.Seconds} 秒"
                    : $"剩余 {Math.Max(1, eta.Seconds)} 秒";
        }
    }

    public string SizeText
    {
        get
        {
            var received = FormatBytes(ReceivedBytes);
            return TotalBytes > 0 ? $"{received} / {FormatBytes(TotalBytes)}" : received;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024:F1} MB";
        }
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }
}
