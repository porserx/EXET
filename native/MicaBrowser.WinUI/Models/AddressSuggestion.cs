namespace MicaBrowser.Models;

public sealed class AddressSuggestion
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Glyph { get; set; } = "\uE81C";
    public bool IsSearch { get; set; }
}
