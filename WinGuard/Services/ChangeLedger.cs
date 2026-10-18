using System.IO;
using WinGuard.Core;

namespace WinGuard.Services;

/// <summary>
/// Remembers originals WinGuard itself overwrote (%LocalAppData%\WinGuard\changes.json).
/// A legacy v1 file is a list of markers without previous values: those markers are not treated as registry or task originals.
/// </summary>
public static class ChangeLedger
{
    private static FileChangeStore? _store;
    public static FileChangeStore Store => _store ??= new FileChangeStore(Path.Combine(AppLog.Folder, "changes.json"));

    public static string AppKey(string pkg) => ChangeKeys.App(pkg);
    public static string TaskKey(string task) => ChangeKeys.Task(task);
    public static string RegistryKey(string hive, string path, string name) => ChangeKeys.Registry(hive, path, name);

    public static bool Contains(string key) => Store.Find(key) is not null;
    public static void Add(string key) => Store.RememberMarker(key);
    public static void Remove(string key) => Store.Forget(key);
}
