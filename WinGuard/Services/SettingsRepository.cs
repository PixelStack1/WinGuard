using System.IO;
using System.Reflection;
using System.Text.Json;
using WinGuard.Models;

namespace WinGuard.Services;

public static class SettingsRepository
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Loads the embedded catalogue. Malformed entries (no id / duplicate id) are skipped and logged.</summary>
    public static List<Setting> Load()
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinGuard.Data.settings.json");
        if (s is null) throw new InvalidOperationException("Embedded settings.json not found.");
        List<Setting?> raw;
        try { raw = JsonSerializer.Deserialize<List<Setting?>>(s, Json) ?? new(); }
        catch (JsonException ex) { throw new InvalidDataException("settings.json is malformed: " + ex.Message, ex); }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<Setting>(raw.Count);
        foreach (Setting? x in raw)
        {
            if (x is null || string.IsNullOrWhiteSpace(x.Id) || !seen.Add(x.Id))
            { AppLog.Warn($"Skipped invalid/duplicate setting entry '{x?.Id}'."); continue; }
            x.Actions ??= new();
            list.Add(x);
        }
        return list;
    }
}
