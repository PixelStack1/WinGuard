using System.IO;
using System.Text.Json;
using WinGuard.Models;

namespace WinGuard.Services;

public sealed class BackupService
{
    private readonly RegistryService _reg;
    public BackupService(RegistryService reg) => _reg = reg;

    public static string BackupFolder
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGuard", "Backups");
            try { Directory.CreateDirectory(dir); } catch (Exception ex) { AppLog.Warn("Cannot create backup folder: " + ex.Message); }
            return dir;
        }
    }

    /// <summary>Saves current registry values touched by the given settings. Returns the file path.
    /// The file is written to a unique name via temp-file + move, so an existing good backup is never overwritten
    /// and an interrupted write never leaves a partial .json behind. Throws if the backup could not be saved.</summary>
    public string Create(IEnumerable<Setting> settings, bool applying)
    {
        var file = new BackupFile();
        foreach (Setting s in settings)
        {
            if (applying) file.AppliedSettingIds.Add(s.Id);
            foreach (SettingAction a in s.Actions.Where(a => a.Type == ActionType.Registry && !string.IsNullOrEmpty(a.Hive) && !string.IsNullOrEmpty(a.Path)))
                file.Registry.Add(_reg.Snapshot(a.Hive!, a.Path!, a.Name ?? ""));
        }
        string json = JsonSerializer.Serialize(file, SettingsRepository.Json);
        string path = Path.Combine(BackupFolder, $"backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        for (int i = 1; File.Exists(path); i++)
            path = Path.Combine(BackupFolder, $"backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{i}.json");
        string tmp = path + ".partial";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: false);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        AppLog.Info($"Backup created: {path} ({file.Registry.Count} values)");
        return path;
    }

    /// <summary>Loads and validates a backup. Throws InvalidDataException with a readable message for missing/corrupt files.</summary>
    public BackupFile Load(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException(Localization.Loc.F("Err.BackupMissing", path));
        BackupFile? f;
        try { f = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(path), SettingsRepository.Json); }
        catch (JsonException ex) { throw new InvalidDataException(Localization.Loc.F("Err.BackupCorrupt", Path.GetFileName(path), ex.Message), ex); }
        if (f is null) throw new InvalidDataException(Localization.Loc.F("Err.BackupCorrupt", Path.GetFileName(path), "empty"));
        f.Registry ??= new();
        f.AppliedSettingIds ??= new();
        // Drop entries that can't be restored rather than failing the whole restore.
        int before = f.Registry.Count;
        f.Registry = f.Registry.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Hive) && !string.IsNullOrWhiteSpace(e.Path)).ToList();
        if (f.Registry.Count != before) AppLog.Warn($"Backup {path}: skipped {before - f.Registry.Count} invalid entries.");
        return f;
    }

    /// <summary>Restores registry values in reverse order. Returns number of failures. Never throws.</summary>
    public int RestoreRegistry(BackupFile file, Action<string> log)
    {
        int failed = 0;
        foreach (BackupEntry e in Enumerable.Reverse(file.Registry))
        {
            try { _reg.Restore(e); }
            catch (UnauthorizedAccessException) { failed++; log(Localization.Loc.F("Log.AccessDenied", $@"{e.Hive}\{e.Path}\{e.Name}")); }
            catch (Exception ex) { failed++; log($@"  ✖ {e.Hive}\{e.Path}\{e.Name}: {ex.Message}"); AppLog.Warn($@"Restore failed {e.Hive}\{e.Path}\{e.Name}: {ex.Message}"); }
        }
        return failed;
    }
}
