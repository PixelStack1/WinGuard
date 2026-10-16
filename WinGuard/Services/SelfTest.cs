using System.IO;
using System.Text;
using WinGuard.Models;

namespace WinGuard.Services;

/// <summary>Headless "WinGuard.exe --selftest [ids]": for every reversible setting, flips it, re-detects, flips it back,
/// re-detects, and ends in the starting state. Apps are only detected (uninstalling is not reversible) and the two
/// security-critical settings are only detected. Report: %LocalAppData%\WinGuard\selftest.txt.</summary>
public static class SelfTest
{
    private static readonly HashSet<string> DetectOnly = new(StringComparer.OrdinalIgnoreCase) { "smartscreen", "spynet" };

    private static string SnipState()
    {
        using var k = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy");
        return $"LetAppsRunInBackground={k?.GetValue("LetAppsRunInBackground") ?? "-"} ForceAllow={string.Join(";", k?.GetValue("LetAppsRunInBackground_ForceAllowTheseApps") as string[] ?? new[] { "-" })}";
    }

    public static async Task RunAsync(string[] onlyIds)
    {
        var engine = new TweakEngine();
        var sb = new StringBuilder();
        var all = SettingsRepository.Load();
        var list = onlyIds.Length > 0 ? all.Where(s => onlyIds.Contains(s.Id, StringComparer.OrdinalIgnoreCase)).ToList() : all;
        // Backup first, like the UI does.
        try { new BackupService(engine.Registry).Create(list.Where(s => !s.Actions.Any(a => a.Type == ActionType.Appx)).ToList(), true); } catch (Exception ex) { sb.AppendLine("backup failed: " + ex.Message); }
        int pass = 0, fail = 0, skipped = 0;
        foreach (Setting s in list)
        {
            var (a0, n0) = await engine.DetectAsync(s);
            string head = $"{s.Id,-34} start={(a0 ? "ON" : "off")}{(n0 != NativeKind.None ? "/" + n0 : "")}";
            if (DetectOnly.Contains(s.Id) || s.Actions.Any(a => a.Type == ActionType.Appx) || n0 != NativeKind.None)
            { sb.AppendLine(head + "  detect-only"); skipped++; continue; }
            var logs = new List<string>();
            bool first = !a0;
            bool ok1 = await engine.ApplyAsync(s, first, logs.Add);
            var (a1, _) = await engine.DetectAsync(s);
            if (s.Id is "appbg" or "bgappsuser") logs.Add("mid: " + SnipState());
            bool ok2 = await engine.ApplyAsync(s, !first, logs.Add);
            if (s.Id is "appbg" or "bgappsuser") logs.Add("end: " + SnipState());
            var (a2, _) = await engine.DetectAsync(s);
            bool good = a1 == first && a2 == a0;
            if (good) pass++; else fail++;
            sb.AppendLine($"{head}  step1({(first ? "apply" : "revert")})→{(a1 ? "ON" : "off")} ok={ok1}  step2→{(a2 ? "ON" : "off")} ok={ok2}  {(good ? "PASS" : "FAIL")}");
            if (!good || !ok1 || !ok2 || s.Id is "appbg" or "bgappsuser") foreach (var l in logs) sb.AppendLine("      " + l.Trim());
        }
        sb.Insert(0, $"WinGuard self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {pass} pass, {fail} fail, {skipped} detect-only\n");
        await File.WriteAllTextAsync(Path.Combine(AppLog.Folder, "selftest.txt"), sb.ToString());
    }
}
