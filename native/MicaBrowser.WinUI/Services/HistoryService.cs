using System.Collections.ObjectModel;
using System.Text.Json;
using MicaBrowser.Models;

namespace MicaBrowser.Services;

public sealed class HistoryService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ObservableCollection<HistoryEntry> Items { get; } = new();

    public HistoryService(string path)
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

            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path), _json);
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Url)).Take(1000))
            {
                Items.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read history.json.", ex);
        }
    }

    public void Record(string url, string title)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var existing = Items.FirstOrDefault(
            e => string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase) &&
                 DateTime.Now - e.VisitedAt < TimeSpan.FromMinutes(10));
        if (existing is not null)
        {
            Items.Remove(existing);
            existing.Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title;
            existing.VisitedAt = DateTime.Now;
            Items.Insert(0, existing);
        }
        else
        {
            Items.Insert(0, new HistoryEntry
            {
                Url = url,
                Title = string.IsNullOrWhiteSpace(title) ? uri.Host : title,
                VisitedAt = DateTime.Now
            });
        }

        while (Items.Count > 1000)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        Save();
    }

    public void Remove(HistoryEntry entry)
    {
        Items.Remove(entry);
        Save();
    }

    public void Clear()
    {
        Items.Clear();
        Save();
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
            Logger.Error("Could not save history.json.", ex);
        }
    }
}
