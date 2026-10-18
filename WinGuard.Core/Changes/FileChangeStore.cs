using System.Text.Json;

namespace WinGuard.Core;

/// <summary>Persists originals in changes.json. Version 1 was a string array of markers; version 2 stores the previous value.</summary>
public sealed class FileChangeStore : IChangeStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, ChangeRecord> _map;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileChangeStore(string path)
    {
        _path = path;
        _map = Load(path);
    }

    public ChangeRecord? Find(string key)
    {
        lock (_gate) return _map.TryGetValue(key, out ChangeRecord? rec) ? rec : null;
    }

    public bool RememberOriginal(string key, bool existed, string kind, string? value)
    {
        lock (_gate)
        {
            if (_map.ContainsKey(key)) return false;
            _map[key] = new ChangeRecord
            {
                Key = key, HasOriginal = true, Existed = existed,
                Kind = string.IsNullOrEmpty(kind) ? "DWord" : kind, Value = value
            };
            try { Save(); }
            catch
            {
                _map.Remove(key);
                throw;
            }
            return true;
        }
    }

    public bool RememberMarker(string key)
    {
        lock (_gate)
        {
            if (_map.ContainsKey(key)) return false;
            _map[key] = new ChangeRecord { Key = key, HasOriginal = false };
            try { Save(); }
            catch
            {
                _map.Remove(key);
                throw;
            }
            return true;
        }
    }

    public void Forget(string key)
    {
        lock (_gate)
        {
            if (!_map.Remove(key)) return;
            Save();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var dto = new FileDto { Version = 2, Entries = _map.Values.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList() };
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dto, Json));
        File.Move(tmp, _path, overwrite: true);
    }

    private static Dictionary<string, ChangeRecord> Load(string path)
    {
        var map = new Dictionary<string, ChangeRecord>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return map;
        string text = File.ReadAllText(path).Trim();
        if (text.Length == 0) return map;
        if (text[0] == '[')
        {
            foreach (string key in JsonSerializer.Deserialize<List<string>>(text) ?? new())
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                map[key] = new ChangeRecord { Key = key, HasOriginal = false };
            }
            return map;
        }
        FileDto? dto = JsonSerializer.Deserialize<FileDto>(text, Json);
        if (dto?.Entries is null) return map;
        foreach (ChangeRecord rec in dto.Entries)
        {
            if (rec is null || string.IsNullOrWhiteSpace(rec.Key)) continue;
            map[rec.Key] = rec;
        }
        return map;
    }

    private sealed class FileDto
    {
        public int Version { get; set; } = 2;
        public List<ChangeRecord> Entries { get; set; } = new();
    }
}
