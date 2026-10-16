using System.Globalization;
using System.IO;
using Microsoft.Win32;
using WinGuard.Models;

namespace WinGuard.Services;

/// <summary>Reads/writes registry values. Missing keys are treated as "not set" (works on Home and Pro alike).</summary>
public sealed class RegistryService
{
    private static RegistryKey OpenBase(string hive) => hive.ToUpperInvariant() switch
    {
        "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        "HKCU" or "HKEY_CURRENT_USER" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
        "HKCR" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64),
        "HKU" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64),
        _ => throw new ArgumentException($"Unknown hive '{hive}'")
    };

    public object? Read(string hive, string path, string name)
    {
        try
        {
            using RegistryKey root = OpenBase(hive);
            using RegistryKey? key = root.OpenSubKey(path, false);
            return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch { return null; }
    }

    public RegistryValueKind? ReadKind(string hive, string path, string name)
    {
        try
        {
            using RegistryKey root = OpenBase(hive);
            using RegistryKey? key = root.OpenSubKey(path, false);
            if (key is null || key.GetValue(name) is null) return null;
            return key.GetValueKind(name);
        }
        catch { return null; }
    }

    public bool Matches(SettingAction a, string? expected)
    {
        if (string.IsNullOrEmpty(a.Hive) || string.IsNullOrEmpty(a.Path)) return false;
        object? current = Read(a.Hive, a.Path, a.Name ?? "");
        if (expected is null) return current is null;
        if (current is null) return false;
        return a.Kind.ToLowerInvariant() switch
        {
            "dword" or "qword" => long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out long e)
                                  && ToLong(current) == e,
            _ => string.Equals(System.Convert.ToString(current, CultureInfo.InvariantCulture), expected, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static long ToLong(object v) => v switch
    {
        int i => i,
        long l => l,
        uint u => u,
        ulong ul => unchecked((long)ul),
        byte[] => long.MinValue,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long r) => r,
        _ => long.MinValue
    };

    public bool IsApplied(SettingAction a) => Matches(a, a.EnabledValue);
    public void Apply(SettingAction a) => Write(a.Hive!, a.Path!, a.Name ?? "", a.Kind, a.EnabledValue);
    public void Revert(SettingAction a) => Write(a.Hive!, a.Path!, a.Name ?? "", a.Kind, a.DisabledValue);

    /// <summary>Writes a value; null deletes the value (missing key/value is ignored).
    /// Access problems surface as UnauthorizedAccessException so callers can log a clear "access denied".</summary>
    public void Write(string hive, string path, string name, string kind, string? value)
    {
        if (string.IsNullOrEmpty(hive) || string.IsNullOrEmpty(path))
            throw new ArgumentException("Registry action has no hive/path.");
        try { WriteCore(hive, path, name, kind, value); }
        catch (System.Security.SecurityException ex) { throw new UnauthorizedAccessException(ex.Message, ex); }
        catch (FormatException ex) { throw new InvalidDataException($"Invalid {kind} value '{value}' for {hive}\\{path}\\{name}", ex); }
        catch (OverflowException ex) { throw new InvalidDataException($"Out-of-range {kind} value '{value}' for {hive}\\{path}\\{name}", ex); }
    }

    private static void WriteCore(string hive, string path, string name, string kind, string? value)
    {
        using RegistryKey root = OpenBase(hive);
        if (value is null)
        {
            using RegistryKey? probe = root.OpenSubKey(path, false);
            if (probe is null || probe.GetValue(name) is null) return; // nothing to delete: no write access needed
            using RegistryKey? k = root.OpenSubKey(path, true);
            k?.DeleteValue(name, false);
            return;
        }
        (object data, RegistryValueKind rk) = ToRegistryData(kind, value); // validate before touching the registry
        using RegistryKey key = root.CreateSubKey(path, true)
            ?? throw new InvalidOperationException($@"Cannot create {hive}\{path}");
        key.SetValue(name, data, rk);
    }

    public static (object, RegistryValueKind) ToRegistryData(string kind, string value) => kind.ToLowerInvariant() switch
    {
        "dword" => (unchecked((int)long.Parse(value, CultureInfo.InvariantCulture)), RegistryValueKind.DWord),
        "qword" => (long.Parse(value, CultureInfo.InvariantCulture), RegistryValueKind.QWord),
        "expandstring" => (value, RegistryValueKind.ExpandString),
        "multistring" => (value.Split('|'), RegistryValueKind.MultiString),
        _ => (value, RegistryValueKind.String)
    };

    public BackupEntry Snapshot(string hive, string path, string name)
    {
        object? v = Read(hive, path, name);
        RegistryValueKind? k = ReadKind(hive, path, name);
        return new BackupEntry
        {
            Hive = hive, Path = path, Name = name, Existed = v is not null,
            Kind = k switch
            {
                RegistryValueKind.QWord => "QWord",
                RegistryValueKind.String => "String",
                RegistryValueKind.ExpandString => "ExpandString",
                RegistryValueKind.MultiString => "MultiString",
                _ => "DWord"
            },
            Value = v switch
            {
                null => null,
                string[] arr => string.Join("|", arr),
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                _ => System.Convert.ToString(v, CultureInfo.InvariantCulture)
            }
        };
    }

    public void Restore(BackupEntry e) => Write(e.Hive, e.Path, e.Name, e.Kind, e.Existed ? e.Value : null);
}
