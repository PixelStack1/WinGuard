using System.IO;
using System.Text.Json;

namespace WinGuard.Services;

/// <summary>
/// Remembers which apps WinGuard itself uninstalled and which scheduled tasks it itself disabled
/// (%LocalAppData%\WinGuard\changes.json). Lets the UI tell "WinGuard did this" apart from
/// "this PC never had it / already had it off", so revert never pretends to undo what it didn't do. Thread-safe.
/// </summary>
public static class ChangeLedger
{
    private static readonly object Gate = new();
    private static HashSet<string>? _set;
    private static string FilePath => Path.Combine(AppLog.Folder, "changes.json");

    private static HashSet<string> Set
    {
        get
        {
            if (_set is not null) return _set;
            _legacy = !File.Exists(FilePath);
            try
            {
                if (!_legacy)
                    _set = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FilePath)) is { } s
                        ? new HashSet<string>(s, StringComparer.OrdinalIgnoreCase) : null;
            }
            catch (Exception ex) { AppLog.Warn("changes.json unreadable: " + ex.Message); }
            return _set ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>True when changes.json didn't exist yet (first run of a build with the ledger). During that first
    /// detection, tasks found disabled are assumed to be WinGuard's (older builds didn't track it) so revert still works.</summary>
    public static bool IsLegacy { get { lock (Gate) { _ = Set; return _legacy; } } }
    private static bool _legacy;
    public static void MarkInitialized() { lock (Gate) { _ = Set; if (_legacy) { _legacy = false; Save(); } } }

    public static string AppKey(string pkg) => "appx|" + pkg;
    public static string TaskKey(string task) => "task|" + task;

    public static bool Contains(string key) { lock (Gate) return Set.Contains(key); }
    public static void Add(string key) { lock (Gate) { if (Set.Add(key)) Save(); } }
    public static void Remove(string key) { lock (Gate) { if (Set.Remove(key)) Save(); } }

    private static void Save()
    {
        try { AppLog.WriteAllTextAtomic(FilePath, JsonSerializer.Serialize(Set)); }
        catch (Exception ex) { AppLog.Warn("Could not save changes.json: " + ex.Message); }
    }
}
