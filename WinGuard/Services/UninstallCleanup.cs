using System.IO;
using WinGuard.Models;

namespace WinGuard.Services;

/// <summary>Headless "WinGuard.exe --uninstall", run by the uninstaller before files are removed: deletes the
/// auto-reapply scheduled tasks and profile, then undoes every setting WinGuard itself applied (registry, tasks it
/// disabled, hosts block, firewall rules, Snipping Tool exception). Removed apps are not reinstalled here (that needs
/// the Store and can take minutes); they're listed in the log. Returns false when a restore point or backup could not
/// be created: nothing is written in that case. Never throws.</summary>
public static class UninstallCleanup
{
    public static async Task<bool> RunAsync()
    {
        AppLog.Info("Uninstall cleanup started.");
        var (rpOk, rpMsg) = await RestorePointService.CreateAsync($"WinGuard uninstall {DateTime.Now:yyyy-MM-dd HH:mm}").ConfigureAwait(false);
        AppLog.Info(rpOk ? "Restore point: " + rpMsg : "Restore point failed: " + rpMsg);
        if (!rpOk)
        {
            AppLog.Warn("Uninstall aborted: no restore point, so nothing was changed.");
            return false;
        }

        var engine = new TweakEngine();
        var toRevert = new List<Setting>();
        foreach (Setting s in SettingsRepository.Load())
        {
            try
            {
                var (applied, native) = await engine.DetectAsync(s).ConfigureAwait(false);
                if (!applied || native != NativeKind.None) continue;
                if (s.Actions.Any(a => a.Type == ActionType.Appx))
                {
                    AppLog.Info($"Uninstall: {s.Id} left as is (removed app; reinstall from the Store if wanted).");
                    continue;
                }
                toRevert.Add(s);
            }
            catch (Exception ex) { AppLog.Warn($"Uninstall detect {s.Id}: {ex.Message}"); }
        }

        if (toRevert.Count > 0)
        {
            try { new BackupService(engine.Registry).Create(toRevert, false); }
            catch (Exception ex)
            {
                AppLog.Error("Uninstall backup failed; nothing was changed.", ex);
                return false;
            }
        }

        try { AppLog.Info(await AutoReapplyService.SetEnabledAsync(false).ConfigureAwait(false)); }
        catch (Exception ex) { AppLog.Warn("Task removal: " + ex.Message); }
        foreach (var n in new[] { "WinGuard Auto-Reapply", "WinGuard Auto-Reapply (Daily)" })
            try { await ProcessRunner.RunAsync("schtasks.exe", $"/Delete /TN \"{n}\" /F", 30_000).ConfigureAwait(false); } catch { }
        try { if (File.Exists(AutoReapplyService.ProfilePath)) File.Delete(AutoReapplyService.ProfilePath); } catch { }

        int reverted = 0;
        foreach (Setting s in toRevert)
        {
            try
            {
                await engine.ApplyAsync(s, false, m => AppLog.Info("uninstall: " + m.Trim())).ConfigureAwait(false);
                reverted++;
            }
            catch (Exception ex) { AppLog.Warn($"Uninstall revert {s.Id}: {ex.Message}"); }
        }
        TweakEngine.SyncSnippingExemption(m => AppLog.Info(m.Trim()));
        AppLog.Info($"Uninstall cleanup finished: {reverted} settings reverted.");
        return true;
    }
}
