using System.Text.Json;
using WinGuard.Models;

namespace WinGuard.Core;

/// <summary>Reads backup JSON files and returns the earliest snapshot of each registry value.</summary>
public sealed class BackupCatalog : IBackupIndex
{
    private readonly string _folder;

    public BackupCatalog(string folder) => _folder = folder;

    public RegValue? Oldest(string hive, string path, string name)
    {
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder)) return null;
        RegValue? found = null;
        DateTime best = DateTime.MaxValue;
        string bestName = "\uffff";
        foreach (string file in Directory.EnumerateFiles(_folder, "backup-*.json"))
        {
            BackupFile? doc;
            try { doc = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(file), FileChangeStore.Json); }
            catch (JsonException) { continue; }
            catch (IOException) { continue; }
            if (doc?.Registry is null) continue;
            BackupEntry? entry = doc.Registry.FirstOrDefault(e =>
                e is not null
                && string.Equals(e.Hive, hive, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            string nameOnly = Path.GetFileName(file);
            if (doc.CreatedUtc < best || (doc.CreatedUtc == best && string.Compare(nameOnly, bestName, StringComparison.OrdinalIgnoreCase) < 0))
            {
                best = doc.CreatedUtc;
                bestName = nameOnly;
                found = new RegValue { Existed = entry.Existed, Kind = string.IsNullOrEmpty(entry.Kind) ? "DWord" : entry.Kind, Value = entry.Value };
            }
        }
        return found;
    }
}
