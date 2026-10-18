using WinGuard.Core;

namespace WinGuard.Services;

public static class RestorePointService
{
    /// <summary>Creates a system restore point. Lifts the 24h frequency limit, then restores that limit and the previous System Protection state (DisableSR). Never throws.</summary>
    public static async Task<(bool Ok, string Message)> CreateAsync(string description)
    {
        try
        {
            string script = RestorePointScript.Build(ProcessRunner.Ps(description));
            var r = await ProcessRunner.PowerShellAsync(script, 600_000).ConfigureAwait(false);
            bool ok = r.ExitCode >= 0 && r.Output.Contains("OK") && !r.Output.Contains("ERR:");
            if (!ok) AppLog.Warn("Restore point failed: " + r.Output);
            return (ok, ok ? Localization.Loc.T("Rp.Ok") : Localization.Loc.F("Rp.Failed", string.IsNullOrWhiteSpace(r.Output) ? $"exit {r.ExitCode}" : r.Output));
        }
        catch (Exception ex)
        {
            AppLog.Error("Restore point crashed", ex);
            return (false, Localization.Loc.F("Rp.Failed", ex.Message));
        }
    }
}
