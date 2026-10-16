using System.IO;
using System.Text;

namespace WinGuard.Services;

/// <summary>Adds/removes tagged hosts-file blocks. Each line carries a "# WinGuard:id" marker so revert is exact.</summary>
public static class HostsService
{
    private static string HostsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
    private static string Marker(string id) => $"# WinGuard:{id}";

    private static List<string> ReadLines()
    {
        if (!File.Exists(HostsPath)) return new List<string>();
        // Shared read: antivirus / DNS client often hold the file open.
        using var fs = new FileStream(HostsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (sr.ReadLine() is { } l) lines.Add(l);
        return lines;
    }

    public static bool IsApplied(string id)
    {
        try { return ReadLines().Any(l => l.EndsWith(Marker(id), StringComparison.Ordinal)); }
        catch (Exception ex) { AppLog.Warn($"hosts read failed ({id}): {ex.Message}"); return false; }
    }

    public static void Apply(string id, IEnumerable<string> domains)
    {
        BackupOnce();
        var lines = ReadLines().Where(l => !l.EndsWith(Marker(id), StringComparison.Ordinal)).ToList();
        lines.AddRange(domains.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => $"0.0.0.0 {d.Trim()} {Marker(id)}"));
        Write(lines);
    }

    public static void Revert(string id)
    {
        if (!File.Exists(HostsPath)) return;
        var all = ReadLines();
        var kept = all.Where(l => !l.EndsWith(Marker(id), StringComparison.Ordinal)).ToList();
        if (kept.Count == all.Count) return; // nothing of ours present: no write needed
        Write(kept);
    }

    /// <summary>Clears read-only, retries while locked (AV/DNS cache), restores read-only afterwards.
    /// Throws IOException/UnauthorizedAccessException with a clear message if it still fails (caller logs and skips).</summary>
    private static void Write(List<string> lines)
    {
        bool wasReadOnly = false;
        if (File.Exists(HostsPath))
        {
            FileAttributes attrs = File.GetAttributes(HostsPath);
            wasReadOnly = attrs.HasFlag(FileAttributes.ReadOnly);
            if (wasReadOnly) File.SetAttributes(HostsPath, attrs & ~FileAttributes.ReadOnly);
        }
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                try { File.WriteAllLines(HostsPath, lines, new UTF8Encoding(false)); return; }
                catch (IOException) when (attempt < 4) { Thread.Sleep(250 * attempt); }
                catch (IOException ex) { throw new IOException("hosts file is locked (antivirus or another program): " + ex.Message, ex); }
            }
        }
        finally
        {
            if (wasReadOnly) try { File.SetAttributes(HostsPath, File.GetAttributes(HostsPath) | FileAttributes.ReadOnly); } catch { }
        }
    }

    private static void BackupOnce()
    {
        try
        {
            string copy = Path.Combine(BackupService.BackupFolder, "hosts.original");
            if (!File.Exists(copy) && File.Exists(HostsPath)) File.Copy(HostsPath, copy);
        }
        catch (Exception ex) { AppLog.Warn("hosts backup copy failed: " + ex.Message); }
    }
}
