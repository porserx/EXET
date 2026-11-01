using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MicaBrowser.Models;

namespace MicaBrowser.Services;

/// <summary>
/// Local password store. Passwords are protected per Windows user with DPAPI
/// and never written to disk or logs in plaintext.
/// </summary>
public sealed class PasswordStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Mica.PasswordManager.v1");
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ObservableCollection<SavedLogin> Items { get; } = new();

    public PasswordStore(string path)
    {
        _path = path;
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var entries = JsonSerializer.Deserialize<List<SavedLogin>>(File.ReadAllText(_path), _json);
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Site)).Take(2000))
            {
                Items.Add(entry);
            }

            Logger.Info($"Password manager loaded {Items.Count} saved login(s).");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read passwords.json.", ex);
        }
    }

    public void Save()
    {
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Items, _json));
            File.Move(temp, _path, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save passwords.json.", ex);
        }
    }

    public SavedLogin? FindById(string id) =>
        Items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<SavedLogin> ForHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Array.Empty<SavedLogin>();
        }

        return Items.Where(item =>
            string.Equals(item.Site, host, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + item.Site, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasEquivalent(string url, string username, string password)
    {
        var host = NormalizeHost(url);
        foreach (var item in Items.Where(item =>
                     string.Equals(item.Site, host, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            var existing = UnprotectPassword(item.EncryptedPassword);
            if (existing is not null && string.Equals(existing, password, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public SavedLogin SaveOrUpdate(string url, string username, string password, SavedLogin? existing = null)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        var normalizedUrl = NormalizeUrl(url);
        var host = NormalizeHost(normalizedUrl);
        var entry = existing;
        if (entry is null)
        {
            entry = Items.FirstOrDefault(item =>
                string.Equals(item.Site, host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));
        }

        if (entry is null)
        {
            entry = new SavedLogin
            {
                Site = host,
                Url = normalizedUrl,
                Username = username.Trim(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            Items.Insert(0, entry);
        }
        else
        {
            entry.Site = host;
            entry.Url = normalizedUrl;
            entry.Username = username.Trim();
            entry.UpdatedAt = DateTime.Now;
        }

        entry.EncryptedPassword = ProtectPassword(password);
        Save();
        return entry;
    }

    public void Remove(SavedLogin entry)
    {
        Items.Remove(entry);
        Save();
    }

    public string? GetPassword(SavedLogin entry)
    {
        try
        {
            return UnprotectPassword(entry.EncryptedPassword);
        }
        catch
        {
            return null;
        }
    }

    public static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0)
        {
            throw new UriFormatException("网站地址不能为空。");
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new UriFormatException("请输入有效的 HTTP 或 HTTPS 网站地址。");
        }

        return uri.AbsoluteUri;
    }

    public static string NormalizeHost(string url)
    {
        var normalized = NormalizeUrl(url);
        return new Uri(normalized).Host.ToLowerInvariant();
    }

    public static string GeneratePassword(int length = 20)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*()-_=+";
        const string all = upper + lower + digits + symbols;
        length = Math.Clamp(length, 12, 64);

        var chars = new List<char>(length)
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };

        while (chars.Count < length)
        {
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        }

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars.ToArray());
    }

    private static string ProtectPassword(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string? UnprotectPassword(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(encrypted);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
