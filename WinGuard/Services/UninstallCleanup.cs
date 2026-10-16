using System.IO;
using WinGuard.Models;

namespace WinGuard.Services;

/// <summary>Headless "WinGuard.exe --uninstall", run by the uninstaller before files are removed: deletes the
/// auto-reapply scheduled tasks and profile, then undoes every setting WinGuard itself applied (registry, tasks it
/// disabled, hosts block, firewall rules, Snipping Tool exception). Removed apps are not reinstalled here (that needs
/// the Store and can take minutes); they're listed in the log. Never throws.</summary>
public static class UninstallCleanup
{
    public static async Task RunAsync()
    {
        AppLog.Info("Uninstall cleanup started.");
        try { AppLog.Info(await AutoReapplyService.SetEnabledAsync(false)); } catch (Exception ex) { AppLog.Warn("Task removal: " + ex.Message); }
        foreach (var n in new[] { "WinGuard Auto-Reapply", "WinGuard Auto-Reapply (Daily)" })
            try { await ProcessRunner.RunAsync("schtasks.exe", $"/Delete /TN \"{n}\" /F", 30_000); } catch { }
        try { if (File.Exists(AutoReapplyService.ProfilePath)) File.Delete(AutoReapplyService.ProfilePath); } catch { }
        var engine = new TweakEngine();
        int reverted = 0;
        foreach (Setting s in SettingsRepository.Load())
        {
            try
            {
                var (applied, native) = await engine.DetectAsync(s);
                if (!applied || native != NativeKind.None) continue;
                if (s.Actions.Any(a => a.Type == ActionType.Appx)) { AppLog.Info($"Uninstall: {s.Id} left as is (removed app; reinstall from the Store if wanted)."); continue; }
                await engine.ApplyAsync(s, false, m => AppLog.Info("uninstall: " + m.Trim()));
                reverted++;
            }
            catch (Exception ex) { AppLog.Warn($"Uninstall revert {s.Id}: {ex.Message}"); }
        }
        TweakEngine.SyncSnippingExemption(m => AppLog.Info(m.Trim()));
        AppLog.Info($"Uninstall cleanup finished: {reverted} settings reverted.");
    }
}
