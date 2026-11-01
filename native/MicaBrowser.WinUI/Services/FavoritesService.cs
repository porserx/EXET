using System.Collections.ObjectModel;
using System.Text.Json;
using MicaBrowser.Models;

namespace MicaBrowser.Services;

public sealed class FavoritesService
{
    private readonly string _path;
    private readonly string _foldersPath;
    private readonly List<string> _folders = new();
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ObservableCollection<FavoriteEntry> Items { get; } = new();

    public FavoritesService(string path)
    {
        _path = path;
        _foldersPath = path + ".folders.json";
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_foldersPath))
            {
                var folders = JsonSerializer.Deserialize<List<string>>(
                    File.ReadAllText(_foldersPath), _json);
                if (folders is not null)
                {
                    foreach (var folder in folders.Where(f => !string.IsNullOrWhiteSpace(f)))
                    {
                        if (!_folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                        {
                            _folders.Add(folder.Trim());
                        }
                    }
                }
            }

            if (!File.Exists(_path))
            {
                return;
            }

            var entries = JsonSerializer.Deserialize<List<FavoriteEntry>>(File.ReadAllText(_path), _json);
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Url)))
            {
                if (string.IsNullOrWhiteSpace(entry.Folder))
                {
                    entry.Folder = "收藏夹";
                }

                Items.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read favorites.json.", ex);
        }
    }

    public bool Contains(string url) =>
        Items.Any(f => string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase));

    public FavoriteEntry Add(string url, string title, string folder)
    {
        var existing = Find(url);
        if (existing is not null)
        {
            existing.Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title;
            existing.Folder = string.IsNullOrWhiteSpace(folder) ? existing.Folder : folder.Trim();
            Save();
            return existing;
        }

        var entry = new FavoriteEntry
        {
            Url = url,
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            Folder = string.IsNullOrWhiteSpace(folder) ? "收藏夹" : folder.Trim(),
            AddedAt = DateTime.Now
        };
        Items.Insert(0, entry);
        Save();
        return entry;
    }

    public FavoriteEntry? Find(string url) =>
        Items.FirstOrDefault(f => string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> GetFolders(string fallback)
    {
        var folders = new List<string> { "收藏夹" };
        if (!string.IsNullOrWhiteSpace(fallback) && !folders.Contains(fallback, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(fallback);
        }

        foreach (var folder in _folders.Concat(Items.Select(item => item.Folder))
                     .Where(folder => !string.IsNullOrWhiteSpace(folder)))
        {
            if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    public bool CreateFolder(string folder)
    {
        var name = folder?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return false;
        }

        if (!_folders.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _folders.Add(name);
            SaveFolders();
        }

        return true;
    }

    public bool RenameFolder(string oldName, string newName, string fallback)
    {
        oldName = oldName?.Trim() ?? string.Empty;
        newName = newName?.Trim() ?? string.Empty;
        if (oldName.Length == 0 || newName.Length == 0 ||
            string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(oldName, "收藏夹", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(oldName, fallback?.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(newName, "收藏夹", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var i = 0; i < _folders.Count; i++)
        {
            if (string.Equals(_folders[i], oldName, StringComparison.OrdinalIgnoreCase))
            {
                _folders[i] = newName;
            }
        }
        if (!_folders.Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            _folders.Add(newName);
        }
        foreach (var item in Items.Where(item =>
                     string.Equals(item.Folder, oldName, StringComparison.OrdinalIgnoreCase)))
        {
            item.Folder = newName;
        }
        SaveFolders();
        Save();
        return true;
    }

    public bool DeleteFolder(string folder, string fallback)
    {
        var name = folder?.Trim() ?? string.Empty;
        if (name.Length == 0 ||
            string.Equals(name, "收藏夹", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "其他收藏", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, fallback?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var removed = _folders.RemoveAll(
            item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0 && !Items.Any(item =>
                string.Equals(item.Folder, name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var destination = string.IsNullOrWhiteSpace(fallback) ? "收藏夹" : fallback.Trim();
        foreach (var item in Items.Where(item =>
                     string.Equals(item.Folder, name, StringComparison.OrdinalIgnoreCase)))
        {
            item.Folder = destination;
        }

        SaveFolders();
        Save();
        return true;
    }

    private void SaveFolders()
    {
        try
        {
            var temp = _foldersPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_folders, _json));
            File.Move(temp, _foldersPath, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save favorite folders.", ex);
        }
    }

    public void SetFolder(FavoriteEntry entry, string folder)
    {
        entry.Folder = string.IsNullOrWhiteSpace(folder) ? "收藏夹" : folder.Trim();
        Save();
    }

    public void Remove(FavoriteEntry entry)
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
            Logger.Error("Could not save favorites.json.", ex);
        }
    }
}
