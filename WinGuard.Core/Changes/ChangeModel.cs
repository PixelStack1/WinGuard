namespace WinGuard.Core;

public sealed class ChangeRecord
{
    public string Key { get; set; } = "";
    /// <summary>False for a legacy marker that only says "WinGuard touched this" without the previous value.</summary>
    public bool HasOriginal { get; set; }
    public bool Existed { get; set; }
    public string Kind { get; set; } = "DWord";
    public string? Value { get; set; }
}

public interface IChangeStore
{
    ChangeRecord? Find(string key);
    /// <summary>Records the original once. A second call does not overwrite it. Returns true when a new record was stored.</summary>
    bool RememberOriginal(string key, bool existed, string kind, string? value);
    /// <summary>Marker without an original value (legacy-compatible). Does not overwrite an existing record.</summary>
    bool RememberMarker(string key);
    void Forget(string key);
}

public interface IBackupIndex
{
    /// <summary>Earliest snapshot of this value, or null when no backup mentions it.</summary>
    RegValue? Oldest(string hive, string path, string name);
}

public static class ChangeKeys
{
    public static string Registry(string hive, string path, string name) => $"reg|{hive}|{path}|{name}";
    public static string Task(string task) => "task|" + task;
    public static string App(string pkg) => "appx|" + pkg;
}

public readonly record struct RevertPlan(bool Known, bool Existed, string Kind, string? Value, string Source);

public static class OriginalValuePolicy
{
    /// <summary>Ledger original wins. Otherwise the oldest backup snapshot. Otherwise nothing is known and the caller must not write.</summary>
    public static RevertPlan Plan(ChangeRecord? ledger, RegValue? oldestBackup)
    {
        if (ledger is { HasOriginal: true })
            return new RevertPlan(true, ledger.Existed, string.IsNullOrEmpty(ledger.Kind) ? "DWord" : ledger.Kind, ledger.Value, "ledger");
        if (oldestBackup is not null)
            return new RevertPlan(true, oldestBackup.Existed, oldestBackup.Kind, oldestBackup.Value, "backup");
        return new RevertPlan(false, false, "DWord", null, "none");
    }

    /// <summary>A pre-existing match is not "applied by WinGuard" unless we stored an original or an older backup differs from the live value.</summary>
    public static bool IsRegistryOwned(ChangeRecord? ledger, RegValue? oldest, RegValue current)
    {
        if (ledger is { HasOriginal: true }) return true;
        if (oldest is null) return false;
        return !RegValue.Same(oldest, current);
    }
}

public static class TaskPolicy
{
    public static bool ShouldRecordDisable(string state) =>
        state is "Ready" or "Running" or "Queued";

    /// <summary>Enable only when we stored a prior state that was not already Disabled. Legacy markers are not enough.</summary>
    public static bool WasDisabledByUs(ChangeRecord? rec) =>
        rec is { HasOriginal: true } && !string.Equals(rec.Value, "Disabled", StringComparison.OrdinalIgnoreCase);
}

public static class AppxPolicy
{
    /// <summary>Any appx record, including a legacy marker, means WinGuard removed the package (those markers were written only after a successful removal).</summary>
    public static bool WasRemovedByUs(ChangeRecord? rec) => rec is not null;
}

public enum RevertResult { Restored, Unchanged, Unknown, ServiceMissing, ReadFailed }

/// <summary>Applies and reverts one registry value using the ledger and backups. Never writes a guessed default.</summary>
public sealed class RegistryEditor
{
    private readonly IRegistryStore _reg;
    private readonly IChangeStore _changes;
    private readonly IBackupIndex _backups;

    public RegistryEditor(IRegistryStore reg, IChangeStore changes, IBackupIndex backups)
    {
        _reg = reg;
        _changes = changes;
        _backups = backups;
    }

    public void Apply(string hive, string path, string name, string kind, string? enabledValue)
    {
        if (ServiceKeyPolicy.IsServiceStart(path, name) && !_reg.SubKeyExists(hive, path))
            throw new ServiceMissingException(path);
        RegValue current = _reg.Read(hive, path, name);
        string key = ChangeKeys.Registry(hive, path, name);
        bool added = _changes.RememberOriginal(key, current.Existed, current.Existed ? current.Kind : kind, current.Value);
        try
        {
            _reg.Write(hive, path, name, kind, enabledValue);
        }
        catch
        {
            if (added) _changes.Forget(key);
            throw;
        }
    }

    public RevertResult Revert(string hive, string path, string name)
    {
        string key = ChangeKeys.Registry(hive, path, name);
        ChangeRecord? ledger = _changes.Find(key);
        RevertPlan plan = OriginalValuePolicy.Plan(ledger, _backups.Oldest(hive, path, name));
        if (!plan.Known) return RevertResult.Unknown;
        RegValue current;
        try { current = _reg.Read(hive, path, name); }
        catch { return RevertResult.ReadFailed; }
        var target = new RegValue { Existed = plan.Existed, Kind = plan.Kind, Value = plan.Value };
        if (RegValue.Same(target, current))
        {
            if (ledger is { HasOriginal: true }) _changes.Forget(key);
            return RevertResult.Unchanged;
        }
        if (ServiceKeyPolicy.IsServiceStart(path, name) && !_reg.SubKeyExists(hive, path))
            return RevertResult.ServiceMissing;
        _reg.Write(hive, path, name, plan.Kind, plan.Existed ? plan.Value : null);
        _changes.Forget(key);
        return RevertResult.Restored;
    }
}
