using System.Text.Json.Serialization;
using MicaBrowser.Helpers;

namespace MicaBrowser.Models;

public sealed class SavedLogin : ObservableObject
{
    private string _site = string.Empty;
    private string _url = string.Empty;
    private string _username = string.Empty;
    private string _encryptedPassword = string.Empty;
    private DateTime _updatedAt = DateTime.Now;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Site
    {
        get => _site;
        set => SetProperty(ref _site, value);
    }

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    /// <summary>DPAPI-protected password, base64 encoded. Never stored as plaintext.</summary>
    public string EncryptedPassword
    {
        get => _encryptedPassword;
        set => SetProperty(ref _encryptedPassword, value);
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (SetProperty(ref _updatedAt, value))
            {
                OnPropertyChanged(nameof(UpdatedText));
            }
        }
    }

    [JsonIgnore]
    public string MaskedPassword => "••••••••";

    [JsonIgnore]
    public string UpdatedText => UpdatedAt.ToString("MM-dd HH:mm");
}
