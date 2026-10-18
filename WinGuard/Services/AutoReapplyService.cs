using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WinGuard.Models;

namespace WinGuard.Services;

/// <summary>
/// Windows feature updates often reset privacy settings. When enabled, WinGuard saves the active profile and registers
/// a logon scheduled task that runs "WinGuard.exe --reapply" with highest privileges to silently re-apply it.
/// </summary>
public static class AutoReapplyService
{
    private const string TaskName = "WinGuard Auto-Reapply";
    // Second trigger: Windows Update can reset settings without a logon, so also re-check daily.
    private const string DailyTaskName = "WinGuard Auto-Reapply (Daily)";
    public static string ProfilePath => Path.Combine(Path.GetDirectoryName(BackupService.BackupFolder)!, "autoprofile.json");

    public static async Task<bool> IsEnabledAsync()
    {
        var r = await ProcessRunner.RunAsync("schtasks.exe", $"/Query /TN \"{TaskName}\"", 30_000).ConfigureAwait(false);
        return r.ExitCode == 0;
    }

    /// <summary>Creates/deletes the scheduled tasks. Never throws; returns a user-facing message.</summary>
    public static async Task<string> SetEnabledAsync(bool enable)
    {
        try
        {
            if (!enable)
            {
                await ProcessRunner.RunAsync("schtasks.exe", $"/Delete /TN \"{DailyTaskName}\" /F", 30_000).ConfigureAwait(false);
                var d = await ProcessRunner.RunAsync("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", 30_000).ConfigureAwait(false);
                // A task that doesn't exist is already "disabled".
                bool gone = d.ExitCode == 0 || !(await IsEnabledAsync().ConfigureAwait(false));
                return gone ? Localization.Loc.T("Auto.Disabled") : Localization.Loc.F("Auto.Failed", d.Output);
            }
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { try { exe = Process.GetCurrentProcess().MainModule?.FileName; } catch { } }
            if (string.IsNullOrEmpty(exe)) return Localization.Loc.F("Auto.Failed", "executable path unknown");
            var r = await ProcessRunner.RunAsync("schtasks.exe",
                $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /DELAY 0001:00 /TR \"\\\"{exe}\\\" --reapply\"", 30_000).ConfigureAwait(false);
            if (r.ExitCode == 0)
            {
                var r2 = await ProcessRunner.RunAsync("schtasks.exe",
                    $"/Create /F /TN \"{DailyTaskName}\" /SC DAILY /ST 12:30 /RL HIGHEST /TR \"\\\"{exe}\\\" --reapply\"", 30_000).ConfigureAwait(false);
                if (r2.ExitCode != 0) AppLog.Warn("Daily auto-reapply task not created: " + r2.Output);
            }
            else AppLog.Warn("Auto-reapply task creation failed: " + r.Output);
            return r.ExitCode == 0 ? Localization.Loc.T("Auto.Enabled") : Localization.Loc.F("Auto.Failed", r.Output);
        }
        catch (Exception ex)
        {
            AppLog.Error("Auto-reapply toggle failed", ex);
            return Localization.Loc.F("Auto.Failed", ex.Message);
        }
    }

    public static void SaveProfile(IEnumerable<ProfileEntry> profile) =>
        AppLog.WriteAllTextAtomic(ProfilePath, JsonSerializer.Serialize(profile, SettingsRepository.Json));

    /// <summary>Headless mode: re-apply every setting marked applied in the saved profile. Each setting is isolated.
    /// Returns false when a restore point or backup could not be created: nothing is written in that case.</summary>
    public static async Task<bool> ReapplyAsync()
    {
        AppLog.Info("Headless --reapply started.");
        if (!File.Exists(ProfilePath)) { AppLog.Info("No auto-reapply profile; nothing to do."); return true; }
        List<ProfileEntry> profile;
        try { profile = JsonSerializer.Deserialize<List<ProfileEntry>>(File.ReadAllText(ProfilePath), SettingsRepository.Json) ?? new(); }
        catch (Exception ex) { AppLog.Error("Auto-reapply profile is corrupt; skipping.", ex); return false; }
        var wanted = profile.Where(p => p is not null && p.Applied).Select(p => p.Id).ToHashSet();
        var engine = new TweakEngine();
        string log = Path.Combine(BackupService.BackupFolder, "reapply.log");
        void L(string m) { AppLog.Info("reapply: " + m.Trim()); try { File.AppendAllText(log, m + Environment.NewLine); } catch { } }
        int done = 0, failed = 0;
        var pending = new List<Setting>();
        foreach (Setting s in SettingsRepository.Load().Where(s => wanted.Contains(s.Id)))
        {
            try
            {
                if (await engine.IsAppliedAsync(s).ConfigureAwait(false)) continue;
                pending.Add(s);
            }
            catch (Exception ex) { failed++; L($"{DateTime.Now:s} FAILED {s.Id}: {ex.Message}"); }
        }
        if (pending.Count == 0)
        {
            AppLog.Info($"Headless --reapply finished: nothing to write ({failed} read errors).");
            return failed == 0;
        }

        var (rpOk, rpMsg) = await RestorePointService.CreateAsync($"WinGuard reapply {DateTime.Now:yyyy-MM-dd HH:mm}").ConfigureAwait(false);
        L(rpOk ? "Restore point: " + rpMsg : "Restore point failed: " + rpMsg);
        if (!rpOk)
        {
            L("No restore point, so nothing was changed.");
            return false;
        }
        try { new BackupService(engine.Registry).Create(pending, true); }
        catch (Exception ex)
        {
            AppLog.Error("Reapply backup failed; nothing was changed.", ex);
            L("Backup failed, so nothing was changed: " + ex.Message);
            return false;
        }

        foreach (Setting s in pending)
        {
            try
            {
                bool ok = await engine.ApplyAsync(s, true, L).ConfigureAwait(false);
                if (ok) done++; else failed++;
                L($"{DateTime.Now:s} re-applied {s.Id}{(ok ? "" : " (with warnings)")}");
            }
            catch (Exception ex) { failed++; L($"{DateTime.Now:s} FAILED {s.Id}: {ex.Message}"); }
        }
        AppLog.Info($"Headless --reapply finished: {done} re-applied, {failed} with errors.");
        return true;
    }
}
