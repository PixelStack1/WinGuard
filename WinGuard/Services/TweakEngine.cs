using WinGuard.Models;

namespace WinGuard.Services;

/// <summary>Evaluates and applies settings across registry, scheduled tasks and Appx packages.</summary>
public sealed class TweakEngine
{
    public RegistryService Registry { get; } = new();

    public static bool NeedsProcess(Setting s) => s.Actions.Any(a => a.Type != ActionType.Registry);

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
                    ActionType.ScheduledTask => !string.IsNullOrEmpty(a.Target) && await TaskStateAsync(a.Target).ConfigureAwait(false) is "Disabled" or "Missing",
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
                        if (enable) Registry.Apply(a); else Registry.Revert(a);
                        break;

                    case ActionType.ScheduledTask:
                    {
                        if (string.IsNullOrEmpty(a.Target)) { Fail(Localization.Loc.F("Log.ActionFailed", a, "no target")); break; }
                        (string dir, string name) = SplitTask(a.Target);
                        string verb = enable ? "Disable-ScheduledTask" : "Enable-ScheduledTask";
                        var r = await ProcessRunner.PowerShellAsync(
                            $"$t=Get-ScheduledTask -TaskPath {ProcessRunner.Ps(dir)} -TaskName {ProcessRunner.Ps(name)} -ErrorAction SilentlyContinue; " +
                            $"if($t){{ try {{ $t | {verb} -ErrorAction Stop | Out-Null; 'OK' }} catch {{ 'ERR: ' + $_.Exception.Message }} }} else {{ 'Missing' }}", 120_000).ConfigureAwait(false);
                        if (r.Output.Contains("Missing")) Log(Localization.Loc.F("Log.TaskMissing", a.Target));
                        else if (r.ExitCode != 0 || !r.Output.Contains("OK")) Fail(Localization.Loc.F("Log.TaskFailed", a.Target, Short(r.Output)));
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
                        }
                        else if (await AppxInstalledAsync(a.Target).ConfigureAwait(false) == true)
                        {
                            // Already present: nothing to reinstall.
                        }
                        else if (!string.IsNullOrEmpty(a.StoreId))
                        {
                            Log(Localization.Loc.F("Log.Reinstalling", a.Target));
                            var rr = await ProcessRunner.RunAsync("winget",
                                $"install --id {a.StoreId} --source msstore --accept-package-agreements --accept-source-agreements --silent").ConfigureAwait(false);
                            if (rr.ExitCode != 0) Fail(Localization.Loc.F("Log.WingetFailed", a.Target, Short(rr.Output)));
                        }
                        else Log(Localization.Loc.F("Log.NoStoreId", a.Target));
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
        return allOk;
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
