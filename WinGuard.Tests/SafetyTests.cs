using System.Text.Json;
using WinGuard.Core;
using WinGuard.Models;
using Xunit;

namespace WinGuard.Tests;

public sealed class MemoryRegistry : IRegistryStore
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RegValue> _values = new(StringComparer.OrdinalIgnoreCase);
    public bool FailReads { get; set; }
    public int Writes { get; private set; }

    private static string KeyId(string hive, string path) => hive + "|" + path;
    private static string ValId(string hive, string path, string name) => hive + "|" + path + "|" + name;

    public void SeedKey(string hive, string path) => _keys.Add(KeyId(hive, path));
    public void Seed(string hive, string path, string name, string kind, string value)
    {
        SeedKey(hive, path);
        _values[ValId(hive, path, name)] = new RegValue { Existed = true, Kind = kind, Value = value };
    }

    public RegValue Read(string hive, string path, string name)
    {
        if (FailReads) throw new IOException("read failed");
        if (!_keys.Contains(KeyId(hive, path))) return RegValue.Missing();
        return _values.TryGetValue(ValId(hive, path, name), out RegValue? v) ? v : RegValue.Missing();
    }

    public bool SubKeyExists(string hive, string path) => _keys.Contains(KeyId(hive, path));

    public void Write(string hive, string path, string name, string kind, string? value)
    {
        if (ServiceKeyPolicy.IsServiceStart(path, name) && !SubKeyExists(hive, path))
            throw new ServiceMissingException(path);
        Writes++;
        if (value is null)
        {
            _values.Remove(ValId(hive, path, name));
            return;
        }
        _keys.Add(KeyId(hive, path));
        _values[ValId(hive, path, name)] = new RegValue { Existed = true, Kind = kind, Value = value };
    }
}

public sealed class MemoryChanges : IChangeStore
{
    private readonly Dictionary<string, ChangeRecord> _map = new(StringComparer.OrdinalIgnoreCase);
    public ChangeRecord? Find(string key) => _map.TryGetValue(key, out ChangeRecord? r) ? r : null;
    public bool RememberOriginal(string key, bool existed, string kind, string? value)
    {
        if (_map.ContainsKey(key)) return false;
        _map[key] = new ChangeRecord { Key = key, HasOriginal = true, Existed = existed, Kind = kind, Value = value };
        return true;
    }
    public bool RememberMarker(string key)
    {
        if (_map.ContainsKey(key)) return false;
        _map[key] = new ChangeRecord { Key = key, HasOriginal = false };
        return true;
    }
    public void Forget(string key) => _map.Remove(key);
}

public sealed class MemoryBackups : IBackupIndex
{
    public RegValue? Value { get; set; }
    public RegValue? Oldest(string hive, string path, string name) => Value;
}

public class RegistryEditorTests
{
    private static RegistryEditor New(MemoryRegistry reg, MemoryBackups? backups = null) =>
        new(reg, new MemoryChanges(), backups ?? new MemoryBackups());

    [Fact]
    public void Apply_then_revert_restores_previous_value()
    {
        var reg = new MemoryRegistry();
        reg.Seed("HKCU", @"Software\Demo", "Enabled", "DWord", "1");
        var editor = New(reg);
        editor.Apply("HKCU", @"Software\Demo", "Enabled", "DWord", "0");
        Assert.Equal("0", reg.Read("HKCU", @"Software\Demo", "Enabled").Value);
        Assert.Equal(RevertResult.Restored, editor.Revert("HKCU", @"Software\Demo", "Enabled"));
        Assert.Equal("1", reg.Read("HKCU", @"Software\Demo", "Enabled").Value);
    }

    [Fact]
    public void Apply_then_revert_deletes_a_value_that_did_not_exist()
    {
        var reg = new MemoryRegistry();
        reg.SeedKey("HKLM", @"SOFTWARE\Policies\Demo");
        var editor = New(reg);
        editor.Apply("HKLM", @"SOFTWARE\Policies\Demo", "Flag", "DWord", "1");
        Assert.True(reg.Read("HKLM", @"SOFTWARE\Policies\Demo", "Flag").Existed);
        Assert.Equal(RevertResult.Restored, editor.Revert("HKLM", @"SOFTWARE\Policies\Demo", "Flag"));
        Assert.False(reg.Read("HKLM", @"SOFTWARE\Policies\Demo", "Flag").Existed);
    }

    [Fact]
    public void Second_apply_keeps_the_first_original()
    {
        var reg = new MemoryRegistry();
        reg.Seed("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", "DWord", "3");
        var changes = new MemoryChanges();
        var editor = new RegistryEditor(reg, changes, new MemoryBackups());
        editor.Apply("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", "DWord", "4");
        reg.Seed("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", "DWord", "4");
        editor.Apply("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", "DWord", "4");
        editor.Revert("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start");
        Assert.Equal("3", reg.Read("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start").Value);
    }

    [Fact]
    public void Revert_without_ledger_or_backup_does_not_write()
    {
        var reg = new MemoryRegistry();
        reg.Seed("HKLM", @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", "DWord", "0");
        var editor = New(reg);
        int before = reg.Writes;
        Assert.Equal(RevertResult.Unknown, editor.Revert("HKLM", @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp"));
        Assert.Equal(before, reg.Writes);
        Assert.Equal("0", reg.Read("HKLM", @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp").Value);
    }

    [Fact]
    public void Revert_uses_oldest_backup_when_ledger_has_no_original()
    {
        var reg = new MemoryRegistry();
        reg.Seed("HKLM", @"SOFTWARE\Demo", "AllowTelemetry", "DWord", "0");
        var backups = new MemoryBackups { Value = new RegValue { Existed = true, Kind = "DWord", Value = "3" } };
        var editor = New(reg, backups);
        Assert.Equal(RevertResult.Restored, editor.Revert("HKLM", @"SOFTWARE\Demo", "AllowTelemetry"));
        Assert.Equal("3", reg.Read("HKLM", @"SOFTWARE\Demo", "AllowTelemetry").Value);
    }

    [Fact]
    public void Legacy_marker_is_not_an_original()
    {
        var changes = new MemoryChanges();
        changes.RememberMarker(ChangeKeys.Registry("HKLM", @"SOFTWARE\Demo", "X"));
        var plan = OriginalValuePolicy.Plan(changes.Find(ChangeKeys.Registry("HKLM", @"SOFTWARE\Demo", "X")), oldestBackup: null);
        Assert.False(plan.Known);
    }

    [Fact]
    public void Missing_service_key_is_not_created()
    {
        var reg = new MemoryRegistry();
        var editor = New(reg);
        Assert.Throws<ServiceMissingException>(() =>
            editor.Apply("HKLM", @"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start", "DWord", "4"));
        Assert.False(reg.SubKeyExists("HKLM", @"SYSTEM\CurrentControlSet\Services\dmwappushservice"));
        Assert.Equal(0, reg.Writes);
    }

    [Fact]
    public void Nested_service_parameter_is_not_treated_as_a_service_root()
    {
        Assert.False(ServiceKeyPolicy.IsServiceStart(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1"));
        Assert.True(ServiceKeyPolicy.IsServiceStart(@"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"));
    }

    [Fact]
    public void Read_failure_on_apply_does_not_write()
    {
        var reg = new MemoryRegistry { FailReads = true };
        reg.SeedKey("HKLM", @"SOFTWARE\Demo");
        var editor = New(reg);
        Assert.Throws<IOException>(() => editor.Apply("HKLM", @"SOFTWARE\Demo", "Flag", "DWord", "1"));
        Assert.Equal(0, reg.Writes);
    }

    [Fact]
    public void Preexisting_match_is_not_owned_without_a_record()
    {
        var current = new RegValue { Existed = true, Kind = "DWord", Value = "0" };
        Assert.False(OriginalValuePolicy.IsRegistryOwned(null, null, current));
        var same = new RegValue { Existed = true, Kind = "DWord", Value = "0" };
        Assert.False(OriginalValuePolicy.IsRegistryOwned(null, same, current));
        var older = new RegValue { Existed = true, Kind = "DWord", Value = "1" };
        Assert.True(OriginalValuePolicy.IsRegistryOwned(null, older, current));
    }
}

public class TaskAndAppxPolicyTests
{
    [Theory]
    [InlineData("Disabled")]
    [InlineData("Missing")]
    [InlineData("Unknown")]
    public void Already_off_or_missing_task_is_not_recorded(string state) =>
        Assert.False(TaskPolicy.ShouldRecordDisable(state));

    [Theory]
    [InlineData("Ready")]
    [InlineData("Running")]
    [InlineData("Queued")]
    public void Live_task_is_recorded(string state) =>
        Assert.True(TaskPolicy.ShouldRecordDisable(state));

    [Fact]
    public void Revert_does_not_enable_a_task_without_a_stored_prior_state()
    {
        Assert.False(TaskPolicy.WasDisabledByUs(null));
        Assert.False(TaskPolicy.WasDisabledByUs(new ChangeRecord { Key = "task|x", HasOriginal = false }));
        Assert.False(TaskPolicy.WasDisabledByUs(new ChangeRecord { HasOriginal = true, Value = "Disabled" }));
        Assert.True(TaskPolicy.WasDisabledByUs(new ChangeRecord { HasOriginal = true, Value = "Ready" }));
    }

    [Fact]
    public void Legacy_appx_marker_still_counts_as_removed_by_us()
    {
        Assert.True(AppxPolicy.WasRemovedByUs(new ChangeRecord { Key = "appx|Microsoft.People", HasOriginal = false }));
        Assert.False(AppxPolicy.WasRemovedByUs(null));
    }
}

public class FileChangeStoreTests
{
    [Fact]
    public void Version1_array_migrates_as_markers_without_originals()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "changes.json");
        File.WriteAllText(path, "[\"task|\\\\Microsoft\\\\Windows\\\\Foo\",\"appx|Microsoft.People\"]");
        var store = new FileChangeStore(path);
        ChangeRecord? task = store.Find(@"task|\Microsoft\Windows\Foo");
        Assert.NotNull(task);
        Assert.False(task!.HasOriginal);
        Assert.False(TaskPolicy.WasDisabledByUs(task));
        Assert.True(AppxPolicy.WasRemovedByUs(store.Find("appx|Microsoft.People")));
    }

    [Fact]
    public void Original_is_not_overwritten_and_survives_reload()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "changes.json");
        var store = new FileChangeStore(path);
        Assert.True(store.RememberOriginal("reg|HKLM|A|Start", true, "DWord", "2"));
        Assert.False(store.RememberOriginal("reg|HKLM|A|Start", true, "DWord", "4"));
        var again = new FileChangeStore(path);
        Assert.Equal("2", again.Find("reg|HKLM|A|Start")!.Value);
    }
}

public class BackupCatalogTests
{
    [Fact]
    public void Oldest_snapshot_wins_over_a_later_backup()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Write(dir, "backup-new.json", new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc), "4");
        Write(dir, "backup-old.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "2");
        var catalog = new BackupCatalog(dir);
        RegValue? v = catalog.Oldest("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start");
        Assert.NotNull(v);
        Assert.Equal("2", v!.Value);
    }

    private static void Write(string dir, string name, DateTime created, string value)
    {
        var file = new BackupFile
        {
            CreatedUtc = created,
            Registry = new List<BackupEntry>
            {
                new() { Hive = "HKLM", Path = @"SYSTEM\CurrentControlSet\Services\DiagTrack", Name = "Start", Existed = true, Kind = "DWord", Value = value }
            }
        };
        File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(file, FileChangeStore.Json));
    }
}

public class RestorePointScriptTests
{
    [Fact]
    public void Script_lifts_the_throttle_and_restores_protection_state()
    {
        string script = RestorePointScript.Build("'WinGuard test'");
        Assert.Contains("SystemRestorePointCreationFrequency", script);
        Assert.Contains("Checkpoint-Computer -Description 'WinGuard test'", script);
        Assert.Contains("DisableSR", script);
        Assert.Contains("Disable-ComputerRestore", script);
        Assert.Contains("finally", script);
        Assert.Equal(0, script.Count(c => c == '{') - script.Count(c => c == '}'));
        Assert.True(SystemProtection.WasDisabled(1));
        Assert.False(SystemProtection.WasDisabled(0));
        Assert.False(SystemProtection.WasDisabled(null));
    }
}

public class CatalogueTests
{
    private static List<Setting> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "settings.json");
        return JsonSerializer.Deserialize<List<Setting>>(File.ReadAllText(path), FileChangeStore.Json)!;
    }

    [Fact]
    public void Paint_and_wifi_use_the_documented_keys_and_ncsi_is_not_recommended()
    {
        List<Setting> all = Load();
        Setting paint = all.Single(s => s.Id == "paintai");
        Assert.All(paint.Actions, a => Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", a.Path));
        Setting wifi = all.Single(s => s.Id == "wifisense");
        Assert.Contains(wifi.Actions, a => a.Path == @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config" && a.Name == "AutoConnectAllowedOEM");
        Assert.DoesNotContain(wifi.Actions, a => (a.Path ?? "").Contains("PolicyManager", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(Recommendation.Recommended, all.Single(s => s.Id == "ncsi").Recommendation);
    }

    [Fact]
    public void DisableUAR_belongs_to_a_single_setting()
    {
        var owners = Load().Where(s => s.Actions.Any(a => a.Name == "DisableUAR")).Select(s => s.Id).ToList();
        Assert.Equal(new[] { "steps" }, owners);
    }
}
