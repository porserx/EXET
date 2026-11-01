namespace MicaBrowser.Models;

public sealed class SessionEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsHomePage { get; set; }
}
