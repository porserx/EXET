using MicaBrowser.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace MicaBrowser.Models;

public sealed class BrowserTab : ObservableObject
{
    private string _title = "新标签页";
    private string _url = string.Empty;
    private bool _isActive;
    private bool _isLoading;
    private bool _isNewTab = true;
    private bool _isHomePage;
    private bool _isPrivate;
    private bool _showTitle = true;
    private double _horizontalTabWidth = 240;
    private bool _isSuspended;
    private bool _isAudioPlaying;
    private bool _isMuted;
    private bool _canGoBack;
    private bool _canGoForward;
    private ImageSource? _favicon;
    private int _blockedCount;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public WebView2? WebView { get; set; }
    public CoreWebView2? Core { get; set; }
    public bool IsSettings { get; set; }
    public string? DefaultUserAgent { get; set; }
    public ImageSource? PreviewImage { get; set; }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, string.IsNullOrWhiteSpace(value) ? "新标签页" : value))
            {
                OnPropertyChanged(nameof(Initial));
            }
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value))
            {
                OnPropertyChanged(nameof(Host));
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(ActiveVisibility));
                OnPropertyChanged(nameof(SelectionOpacity));
            }
        }
    }

    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public double SelectionOpacity => IsActive ? 0.16 : 0;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(IconVisibility));
                OnPropertyChanged(nameof(AudioVisibility));
            }
        }
    }

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IconVisibility => IsLoading || IsAudioPlaying ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AudioVisibility => IsLoading || !IsAudioPlaying ? Visibility.Collapsed : Visibility.Visible;

    public bool IsAudioPlaying
    {
        get => _isAudioPlaying;
        set
        {
            if (SetProperty(ref _isAudioPlaying, value))
            {
                OnPropertyChanged(nameof(AudioVisibility));
                OnPropertyChanged(nameof(IconVisibility));
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(AudioGlyph));
            }
        }
    }

    public string AudioGlyph => IsMuted ? "\uE74F" : "\uE767";

    public bool IsNewTab
    {
        get => _isNewTab;
        set
        {
            if (SetProperty(ref _isNewTab, value))
            {
                OnPropertyChanged(nameof(Initial));
            }
        }
    }

    public bool IsHomePage
    {
        get => _isHomePage;
        set => SetProperty(ref _isHomePage, value);
    }

    public bool IsPrivate
    {
        get => _isPrivate;
        set
        {
            if (SetProperty(ref _isPrivate, value))
            {
                OnPropertyChanged(nameof(Initial));
                OnPropertyChanged(nameof(PrivacyLabel));
            }
        }
    }

    public bool ShowTitle
    {
        get => _showTitle;
        set
        {
            if (SetProperty(ref _showTitle, value))
            {
                OnPropertyChanged(nameof(TitleVisibility));
                OnPropertyChanged(nameof(CloseVisibility));
                OnPropertyChanged(nameof(IconAlignment));
                OnPropertyChanged(nameof(HorizontalPadding));
                OnPropertyChanged(nameof(HorizontalCloseVisibility));
                OnPropertyChanged(nameof(HorizontalCloseButtonSize));
                OnPropertyChanged(nameof(HorizontalCloseGlyphSize));
                OnPropertyChanged(nameof(HorizontalColumnSpacing));
                OnPropertyChanged(nameof(HorizontalCloseColumn));
                OnPropertyChanged(nameof(HorizontalCloseColumnSpan));
                OnPropertyChanged(nameof(HorizontalCloseAlignment));
            }
        }
    }

    public double HorizontalTabWidth
    {
        get => _horizontalTabWidth;
        set
        {
            if (SetProperty(ref _horizontalTabWidth, value))
            {
                OnPropertyChanged(nameof(HorizontalPadding));
                OnPropertyChanged(nameof(HorizontalCloseVisibility));
                OnPropertyChanged(nameof(HorizontalCloseButtonSize));
                OnPropertyChanged(nameof(HorizontalCloseGlyphSize));
                OnPropertyChanged(nameof(HorizontalColumnSpacing));
                OnPropertyChanged(nameof(HorizontalCloseColumn));
                OnPropertyChanged(nameof(HorizontalCloseColumnSpan));
                OnPropertyChanged(nameof(HorizontalCloseAlignment));
            }
        }
    }

    public Thickness HorizontalPadding => ShowTitle
        ? new Thickness(10, 0, 0, 0)
        : new Thickness(2, 0, 0, 0);

    public double HorizontalColumnSpacing => ShowTitle ? 8 : 2;

    public Visibility HorizontalCloseVisibility =>
        HorizontalTabWidth >= 36 ? Visibility.Visible : Visibility.Collapsed;

    public double HorizontalCloseButtonSize =>
        Math.Clamp(HorizontalTabWidth * 0.45, 18, 22);

    public int HorizontalCloseColumn => 2;

    public int HorizontalCloseColumnSpan => 1;

    public HorizontalAlignment HorizontalCloseAlignment => HorizontalAlignment.Right;

    public double HorizontalCloseGlyphSize =>
        HorizontalCloseButtonSize <= 18 ? 10 : 9;

    public Visibility TitleVisibility => ShowTitle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CloseVisibility => ShowTitle ? Visibility.Visible : Visibility.Collapsed;
    public HorizontalAlignment IconAlignment => ShowTitle ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;

    public bool IsSuspended
    {
        get => _isSuspended;
        set
        {
            if (SetProperty(ref _isSuspended, value))
            {
                OnPropertyChanged(nameof(TabOpacity));
            }
        }
    }

    public double TabOpacity => IsSuspended ? 0.55 : 1.0;

    public string PrivacyLabel => IsPrivate ? "无痕" : string.Empty;

    public bool CanGoBack
    {
        get => _canGoBack;
        set => SetProperty(ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        set => SetProperty(ref _canGoForward, value);
    }

    public ImageSource? Favicon
    {
        get => _favicon;
        set => SetProperty(ref _favicon, value);
    }

    public int BlockedCount
    {
        get => _blockedCount;
        set
        {
            if (SetProperty(ref _blockedCount, value))
            {
                OnPropertyChanged(nameof(BlockedBadge));
            }
        }
    }

    public string BlockedBadge => BlockedCount > 99 ? "99+" : BlockedCount.ToString();

    public string Initial
    {
        get
        {
            if (IsPrivate)
            {
                return "私";
            }

            if (IsNewTab)
            {
                return "E";
            }

            var source = !string.IsNullOrWhiteSpace(Title) ? Title :
                (!string.IsNullOrWhiteSpace(Url) ? Url : "E");
            return source.Trim()[..1].ToUpperInvariant();
        }
    }

    public string Host
    {
        get
        {
            try
            {
                return Uri.TryCreate(Url, UriKind.Absolute, out var uri)
                    ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public void Dispose()
    {
        try
        {
            WebView?.Close();
        }
        catch
        {
            // Ignore teardown failures.
        }

        WebView = null;
        Core = null;
    }
}
