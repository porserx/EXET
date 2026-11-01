using MicaBrowser.Helpers;

namespace MicaBrowser.Models;

public sealed class HistoryEntry : ObservableObject
{
    private string _title = string.Empty;
    private DateTime _visitedAt = DateTime.Now;

    public string Url { get; set; } = string.Empty;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public DateTime VisitedAt
    {
        get => _visitedAt;
        set
        {
            if (SetProperty(ref _visitedAt, value))
            {
                OnPropertyChanged(nameof(VisitedText));
            }
        }
    }

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

    public string VisitedText => VisitedAt.ToString("MM-dd HH:mm");
}
