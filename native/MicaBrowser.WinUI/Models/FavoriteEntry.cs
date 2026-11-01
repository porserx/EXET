using MicaBrowser.Helpers;

namespace MicaBrowser.Models;

public sealed class FavoriteEntry : ObservableObject
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    private string _folder = "收藏夹";
    public string Folder
    {
        get => _folder;
        set => SetProperty(ref _folder, string.IsNullOrWhiteSpace(value) ? "收藏夹" : value);
    }
    public DateTime AddedAt { get; set; } = DateTime.Now;

    public string Host
    {
        get
        {
            try
            {
                return new Uri(Url).Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return Url;
            }
        }
    }
}
