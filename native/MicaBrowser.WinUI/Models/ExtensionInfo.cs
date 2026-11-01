using MicaBrowser.Helpers;
using Microsoft.Web.WebView2.Core;

namespace MicaBrowser.Models;

public sealed class ExtensionInfo : ObservableObject
{
    private bool _isEnabled;

    public ExtensionInfo(
        CoreWebView2BrowserExtension extension,
        string? folder = null,
        string? popupPath = null,
        string? iconPath = null)
    {
        Extension = extension;
        _isEnabled = extension.IsEnabled;
        Folder = folder ?? string.Empty;
        PopupPath = popupPath ?? string.Empty;
        IconPath = iconPath ?? string.Empty;
    }

    public CoreWebView2BrowserExtension Extension { get; }
    public string Id => Extension.Id;
    public string Folder { get; }
    public string PopupPath { get; }
    public string IconPath { get; }
    public bool HasPopup => PopupPath.Length > 0 && Folder.Length > 0;
    public string PopupUri => HasPopup
        ? "chrome-extension://" + Id + "/" + PopupPath.Replace('\\', '/')
        : string.Empty;
    public string Name => Extension.Name;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public string StateText => IsEnabled ? "已启用" : "已停用";
}
