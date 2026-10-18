using WinGuard.Core;
using WinGuard.Models;

namespace WinGuard.Services;

/// <summary>Evaluates and applies settings across registry, scheduled tasks and Appx packages.</summary>
public enum NativeKind { None, NotInstalled, AlreadyOff, AlreadySet }

public sealed class TweakEngine
{
    public RegistryService Registry { get; } = new();
    private readonly BackupCatalog _backups;
    private readonly RegistryEditor _editor;

    public TweakEngine()
    {
        _backups = new BackupCatalog(BackupService.BackupFolder);
        _editor = new RegistryEditor(Registry, ChangeLedger.Store, _backups);
    }

    public static bool NeedsProcess(Setting s) => s.Actions.Any(a => a.Type != ActionType.Registry);

    /// <summary>Applied state plus whether WinGuard changed it. A registry-only setting already at the target value,
    /// with no stored original, is AlreadySet. An app/task WinGuard did not remove or disable is NotInstalled or AlreadyOff.</summary>
    public async Task<(bool Applied, NativeKind Native)> DetectAsync(Setting s)
    {
        bool applied = await IsAppliedAsync(s).ConfigureAwait(false);
        if (!applied) return (false, NativeKind.None);
        if (s.Actions.All(a => a.Type is ActionType.Appx or ActionType.ScheduledTask))
            return (true, await TaskAppxNativeAsync(s).ConfigureAwait(false));
        if (s.Actions.Count > 0 && s.Actions.All(a => a.Type == ActionType.Registry) && !RegistryOwned(s))
            return (true, NativeKind.AlreadySet);
        return (true, NativeKind.None);
    }

    /// <summary>Appx/task-only setting that already matches, and that WinGuard did not change.</summary>
    private async Task<NativeKind> TaskAppxNativeAsync(Setting s)
    {
        NativeKind kind = NativeKind.NotInstalled;
        foreach (SettingAction a in s.Actions)
        {
            if (string.IsNullOrEmpty(a.Target)) return NativeKind.None;
            if (a.Type == ActionType.Appx)
            {
                if (AppxPolicy.WasRemovedByUs(ChangeLedger.Store.Find(ChangeLedger.AppKey(a.Target)))) return NativeKind.None;
            }
            else
            {
                (string? real, string state) = await ResolveTaskAsync(a.Target).ConfigureAwait(false);
                if (real is not null && TaskPolicy.WasDisabledByUs(ChangeLedger.Store.Find(ChangeLedger.TaskKey(real)))) return NativeKind.None;
                if (state != "Missing") kind = NativeKind.AlreadyOff;
            }
        }
        return kind;
    }

    private bool RegistryOwned(Setting s)
    {
        foreach (SettingAction a in s.Actions)
        {
            if (a.Type != ActionType.Registry || string.IsNullOrEmpty(a.Hive) || string.IsNullOrEmpty(a.Path)) continue;
            string key = ChangeLedger.RegistryKey(a.Hive, a.Path, a.Name ?? "");
            try
            {
                RegValue current = Registry.ReadStrict(a.Hive, a.Path, a.Name ?? "");
                if (OriginalValuePolicy.IsRegistryOwned(ChangeLedger.Store.Find(key), _backups.Oldest(a.Hive, a.Path, a.Name ?? ""), current))
                    return true;
            }
            catch (Exception ex) { AppLog.Warn($"Ownership read failed for {s.Id}: {ex.Message}"); }
        }
        return false;
    }

    /// <summary>True when every action is in its "privacy" state. Never throws: an action whose state can't be read counts as not applied.</summary>
    public async Task<bool> IsAppliedAsync(Setting s)
    {
        if (s.Actions.Count == 0) return false;
        foreach (SettingAction a in s.Actions)
        {
            bool ok;
            try
            {
                ok = a.Type switch
                {
                    ActionType.Registry => Registry.IsApplied(a),
                    ActionType.ScheduledTask => !string.IsNullOrEmpty(a.Target) && (await ResolveTaskAsync(a.Target).ConfigureAwait(false)).State is "Disabled" or "Missing",
                    ActionType.Appx => !string.IsNullOrEmpty(a.Target) && await AppxInstalledAsync(a.Target).ConfigureAwait(false) == false,
                    ActionType.Hosts => HostsService.IsApplied(s.Id),
                    ActionType.Firewall => !string.IsNullOrEmpty(a.Target) && await FirewallRuleExistsAsync(s.Id, a.Target).ConfigureAwait(false),
                    _ => false
                };
            }
            catch (Exception ex)
            {
                AppLog.Warn($"State read failed for {s.Id} ({a}): {ex.Message}");
                ok = false;
            }
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>Applies every action. Errors on one action don't stop the others; they're reported via log. Never throws.
    /// Returns false if at least one action genuinely failed (a "not applicable" skip is not a failure).</summary>
    public async Task<bool> ApplyAsync(Setting s, bool enable, Action<string> log)
    {
        bool allOk = true;
        void Log(string m) { try { log(m); } catch { } AppLog.Info($"[{s.Id}] {m.Trim()}"); }
        void Fail(string m) { allOk = false; try { log(m); } catch { } AppLog.Warn($"[{s.Id}] {m.Trim()}"); }

        foreach (SettingAction a in s.Actions)
        {
            try
            {
                switch (a.Type)
                {
                    case ActionType.Registry:
                        if (string.IsNullOrEmpty(a.Hive) || string.IsNullOrEmpty(a.Path)) { Fail(Localization.Loc.F("Log.ActionFailed", a, "no hive/path")); break; }
                        if (enable)
                        {
                            try { _editor.Apply(a.Hive, a.Path, a.Name ?? "", a.Kind, a.EnabledValue); }
                            catch (ServiceMissingException) { Fail(Localization.Loc.F("Log.ServiceMissing", a.Path)); }
                        }
                        else
                        {
                            switch (_editor.Revert(a.Hive, a.Path, a.Name ?? ""))
                            {
                                case RevertResult.Unknown:
                                    Log(Localization.Loc.F("Log.OriginalUnknown", $@"{a.Hive}\{a.Path}\{a.Name}"));
                                    break;
                                case RevertResult.ServiceMissing:
                                    Fail(Localization.Loc.F("Log.ServiceMissing", a.Path));
                                    break;
                                case RevertResult.ReadFailed:
                                    Fail(Localization.Loc.F("Log.ActionFailed", a, "read failed"));
                                    break;
                            }
                        }
                        break;

                    case ActionType.ScheduledTask:
                    {
                        if (string.IsNullOrEmpty(a.Target)) { Fail(Localization.Loc.F("Log.ActionFailed", a, "no target")); break; }
                        (string? real, string state) = await ResolveTaskAsync(a.Target).ConfigureAwait(false);
                        if (real is null) { Log(Localization.Loc.F("Log.TaskMissing", a.Target)); break; }
                        string taskKey = ChangeLedger.TaskKey(real);
                        if (enable)
                        {
                            if (!TaskPolicy.ShouldRecordDisable(state))
                            {
                                Log(Localization.Loc.F("Log.TaskNotOurs", real));
                                break;
                            }
                        }
                        else if (!TaskPolicy.WasDisabledByUs(ChangeLedger.Store.Find(taskKey)))
                        {
                            Log(Localization.Loc.F("Log.OriginalUnknown", real));
                            break;
                        }
                        (string dir, string name) = SplitTask(real);
                        string verb = enable ? "Disable-ScheduledTask" : "Enable-ScheduledTask";
                        var r = await ProcessRunner.PowerShellAsync(
                            $"$t=Get-ScheduledTask -TaskPath {ProcessRunner.Ps(dir)} -TaskName {ProcessRunner.Ps(name)} -ErrorAction SilentlyContinue; " +
                            $"if($t){{ $was=[string]$t.State; try {{ $t | {verb} -ErrorAction Stop | Out-Null; 'OK ' + $was }} catch {{ 'ERR: ' + $_.Exception.Message }} }} else {{ 'Missing' }}", 120_000).ConfigureAwait(false);
                        if (r.Output.Contains("Missing")) Log(Localization.Loc.F("Log.TaskMissing", real));
                        else if (r.ExitCode != 0 || !r.Output.Contains("OK")) Fail(Localization.Loc.F("Log.TaskFailed", real, Short(r.Output)));
                        else if (enable) ChangeLedger.Store.RememberOriginal(taskKey, true, "State", state);
                        else ChangeLedger.Remove(taskKey);
                        break;
                    }

                    case ActionType.Hosts:
                        if (enable) HostsService.Apply(s.Id, (a.Target ?? "").Split('|')); else HostsService.Revert(s.Id);
                        break;

                    case ActionType.Firewall:
                    {
                        if (string.IsNullOrEmpty(a.Target)) { Fail(Localization.Loc.F("Log.ActionFailed", a, "no target")); break; }
                        string rule = RuleName(s.Id, a.Target);
                        var fr = await ProcessRunner.PowerShellAsync(enable
                            ? $"try {{ Get-NetFirewallRule -DisplayName {ProcessRunner.Ps(rule)} -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue; New-NetFirewallRule -DisplayName {ProcessRunner.Ps(rule)} -Group 'WinGuard' -Direction Outbound -Action Block -Program {ProcessRunner.Ps(a.Target)} -ErrorAction Stop | Out-Null; 'OK' }} catch {{ 'ERR: ' + $_.Exception.Message }}"
                            : $"try {{ Get-NetFirewallRule -DisplayName {ProcessRunner.Ps(rule)} -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction Stop; 'OK' }} catch {{ 'ERR: ' + $_.Exception.Message }}", 120_000).ConfigureAwait(false);
                        if (fr.ExitCode != 0 || !fr.Output.Contains("OK")) Fail(Localization.Loc.F("Log.FirewallFailed", Short(fr.Output)));
                        break;
                    }

                    case ActionType.Appx:
                        if (string.IsNullOrEmpty(a.Target)) { Fail(Localization.Loc.F("Log.ActionFailed", a, "no target")); break; }
                        if (enable)
                        {
                            var rr = await ProcessRunner.PowerShellAsync(
                                $"$p=Get-AppxPackage -AllUsers -Name {ProcessRunner.Ps(a.Target)} -ErrorAction SilentlyContinue; " +
                                "if(-not $p){ 'Missing' } else { try { $p | Remove-AppxPackage -AllUsers -ErrorAction Stop; 'OK' } catch { 'ERR: ' + $_.Exception.Message } }").ConfigureAwait(false);
                            if (rr.Output.Contains("Missing")) Log(Localization.Loc.F("Log.AppxMissing", a.Target));
                            else if (rr.ExitCode != 0 || rr.Output.Contains("ERR:")) Fail(Localization.Loc.F("Log.AppxFailed", a.Target, Short(rr.Output)));
                            else ChangeLedger.Store.RememberOriginal(ChangeLedger.AppKey(a.Target), true, "State", "installed");
                        }
                        else if (await AppxInstalledAsync(a.Target).ConfigureAwait(false) == true)
                        {
                            ChangeLedger.Remove(ChangeLedger.AppKey(a.Target)); // already present: nothing to reinstall
                        }
                        else if (!AppxPolicy.WasRemovedByUs(ChangeLedger.Store.Find(ChangeLedger.AppKey(a.Target))))
                        {
                            // Never installed on this PC (WinGuard didn't remove it): nothing to undo.
                            Log(Localization.Loc.F("Log.AppxNotOurs", a.Target));
                        }
                        else if (!string.IsNullOrEmpty(a.StoreId))
                        {
                            Log(Localization.Loc.F("Log.Reinstalling", a.Target));
                            var rr = await ProcessRunner.RunAsync("winget",
                                $"install --id {a.StoreId} --source msstore --accept-package-agreements --accept-source-agreements --silent").ConfigureAwait(false);
                            if (rr.ExitCode != 0) { Fail(Localization.Loc.F("Log.WingetFailed", a.Target, Short(rr.Output))); ManualReinstall.Add(a.Target); }
                            else ChangeLedger.Remove(ChangeLedger.AppKey(a.Target));
                        }
                        else { Log(Localization.Loc.F("Log.NoStoreId", a.Target)); ManualReinstall.Add(a.Target); }
                        break;
                }
            }
            catch (UnauthorizedAccessException)
            {
                Fail(Localization.Loc.F("Log.AccessDenied", a));
            }
            catch (System.Security.SecurityException)
            {
                Fail(Localization.Loc.F("Log.AccessDenied", a));
            }
            catch (System.IO.IOException ex) when (a.Type == ActionType.Hosts)
            {
                Fail(Localization.Loc.F("Log.HostsLocked", ex.Message));
            }
            catch (Exception ex)
            {
                Fail(Localization.Loc.F("Log.ActionFailed", a, ex.Message));
            }
        }
        if (s.Id is "appbg" or "bgappsuser") SyncSnippingExemption(log);
        return allOk;
    }

    private const string AppPrivacyKey = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string ForceAllowValue = "LetAppsRunInBackground_ForceAllowTheseApps";
    /// <summary>Snipping Tool (Print Screen overlay) must be able to run in the background, otherwise Win11 cold-starts it on
    /// every Print Screen and the capture overlay stays up until a second press. When background apps are blocked (policy
    /// or the per-user global switch), WinGuard keeps Snipping Tool on the policy allow-list; it removes that entry again
    /// once neither block is active. Never throws.</summary>
    public static readonly string[] BackgroundExempt = { "Microsoft.ScreenSketch_8wekyb3d8bbwe" };

    public static void SyncSnippingExemption(Action<string> log)
    {
        try
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var hkcu = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications");
            object? pol = hklm.OpenSubKey(AppPrivacyKey)?.GetValue("LetAppsRunInBackground");
            bool blocked = (pol is int p && p == 2) || (hkcu?.GetValue("GlobalUserDisabled") is int g && g == 1);
            using var key = hklm.CreateSubKey(AppPrivacyKey, true);
            var list = (key.GetValue(ForceAllowValue) as string[] ?? Array.Empty<string>()).ToList();
            bool changed = false;
            foreach (string app in BackgroundExempt)
            {
                bool has = list.Contains(app, StringComparer.OrdinalIgnoreCase);
                if (blocked && !has) { list.Add(app); changed = true; }
                else if (!blocked && has) { list.RemoveAll(x => x.Equals(app, StringComparison.OrdinalIgnoreCase)); changed = true; }
            }
            // The allow-list is only honoured when the policy itself is configured: with only the per-user switch off,
            // configure it as "user in control" (0), and remove that again later only if WinGuard set it.
            object? cur = key.GetValue("LetAppsRunInBackground");
            if (blocked && cur is null) { key.SetValue("LetAppsRunInBackground", 0, Microsoft.Win32.RegistryValueKind.DWord); ChangeLedger.Add("snip|pol0"); changed = true; }
            else if (!blocked && ChangeLedger.Contains("snip|pol0"))
            {
                if (cur is int c0 && c0 == 0) key.DeleteValue("LetAppsRunInBackground", false);
                ChangeLedger.Remove("snip|pol0"); changed = true;
            }
            if (!changed) return;
            if (list.Count == 0) key.DeleteValue(ForceAllowValue, false);
            else key.SetValue(ForceAllowValue, list.ToArray(), Microsoft.Win32.RegistryValueKind.MultiString);
            string m = Localization.Loc.T(blocked ? "Log.SnipExempt" : "Log.SnipExemptRemoved");
            try { log(m); } catch { }
            AppLog.Info(m.Trim());
        }
        catch (Exception ex) { AppLog.Warn("Snipping Tool exemption sync failed: " + ex.Message); }
    }

    private static string Short(string s)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 300 ? s[..300] + "…" : s;
    }

    private static string RuleName(string id, string program) => $"WinGuard {id} ({System.IO.Path.GetFileName(program)})";

    private static async Task<bool> FirewallRuleExistsAsync(string id, string program)
    {
        var r = await ProcessRunner.PowerShellAsync(
            $"if(Get-NetFirewallRule -DisplayName {ProcessRunner.Ps(RuleName(id, program))} -ErrorAction SilentlyContinue){{'Yes'}}else{{'No'}}", 60_000);
        return r.ExitCode == 0 && r.Output.Contains("Yes");
    }

    private static (string Dir, string Name) SplitTask(string full)
    {
        int i = full.LastIndexOf('\\');
        if (i < 0) return ("\\", full);
        return (full[..(i + 1)], full[(i + 1)..]);
    }

    /// <summary>Packages WinGuard removed that couldn't be put back automatically during the current batch (UI drains it).</summary>
    public System.Collections.Concurrent.ConcurrentBag<string> ManualReinstall { get; } = new();

    /// <summary>A task target may list alternates separated by '|' (Windows renamed some tasks, e.g. 24H2's
    /// "Microsoft Compatibility Appraiser Exp"). Returns the first one that exists and its state, or (null, "Missing").</summary>
    private static async Task<(string? Real, string State)> ResolveTaskAsync(string target)
    {
        foreach (string t in target.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string st = await TaskStateAsync(t).ConfigureAwait(false);
            if (st != "Missing") return (t, st);
        }
        return (null, "Missing");
    }

    private static async Task<string> TaskStateAsync(string full)
    {
        (string dir, string name) = SplitTask(full);
        var r = await ProcessRunner.PowerShellAsync(
            $"$t=Get-ScheduledTask -TaskPath {ProcessRunner.Ps(dir)} -TaskName {ProcessRunner.Ps(name)} -ErrorAction SilentlyContinue; if($t){{ [string]$t.State }} else {{ 'Missing' }}", 60_000).ConfigureAwait(false);
        if (r.ExitCode != 0) return "Unknown";
        string o = r.Output.Trim();
        // Take the last non-empty line (PowerShell may emit warnings before the state).
        return o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "Unknown";
    }

    /// <summary>true = installed, false = not installed, null = could not determine (PowerShell failed).</summary>
    private static async Task<bool?> AppxInstalledAsync(string pkg)
    {
        var r = await ProcessRunner.PowerShellAsync(
            $"if(Get-AppxPackage -AllUsers -Name {ProcessRunner.Ps(pkg)} -ErrorAction SilentlyContinue){{'Installed'}}else{{'Missing'}}", 60_000).ConfigureAwait(false);
        if (r.Output.Contains("Installed")) return true;
        if (r.Output.Contains("Missing")) return false;
        return null;
    }
}
