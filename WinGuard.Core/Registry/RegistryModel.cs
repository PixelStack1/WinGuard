namespace WinGuard.Core;

/// <summary>A registry value as text, so apply/revert can be tested without Windows.</summary>
public sealed class RegValue
{
    public bool Existed { get; init; }
    public string Kind { get; init; } = "DWord";
    public string? Value { get; init; }

    public static RegValue Missing(string kind = "DWord") => new() { Existed = false, Kind = kind, Value = null };

    public static bool Same(RegValue a, RegValue b)
    {
        if (a.Existed != b.Existed) return false;
        if (!a.Existed) return true;
        return string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Value ?? "", b.Value ?? "", StringComparison.OrdinalIgnoreCase);
    }
}

public interface IRegistryStore
{
    /// <summary>Missing key or value returns <see cref="RegValue.Existed"/> false. Throws on access or I/O failure.</summary>
    RegValue Read(string hive, string path, string name);
    bool SubKeyExists(string hive, string path);
    /// <summary>Null value deletes. Must not create a missing service key.</summary>
    void Write(string hive, string path, string name, string kind, string? value);
}

public sealed class ServiceMissingException : InvalidOperationException
{
    public ServiceMissingException(string path) : base($"Service key does not exist: {path}") { }
}

public static class ServiceKeyPolicy
{
    private const string Prefix = @"SYSTEM\CurrentControlSet\Services\";

    /// <summary>True for HKLM\SYSTEM\CurrentControlSet\Services\&lt;name&gt;\Start. Nested paths are not service roots.</summary>
    public static bool IsServiceStart(string path, string name)
    {
        if (!name.Equals("Start", StringComparison.OrdinalIgnoreCase)) return false;
        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string rest = path[Prefix.Length..];
        return rest.Length > 0 && !rest.Contains('\\');
    }
}
