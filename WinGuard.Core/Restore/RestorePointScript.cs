namespace WinGuard.Core;

public static class SystemProtection
{
    /// <summary>DisableSR = 1 means System Protection was off. Null (value absent) and 0 mean it was not explicitly off.</summary>
    public static bool WasDisabled(int? disableSr) => disableSr == 1;
}

/// <summary>PowerShell that creates a restore point, then puts the 24h throttle and System Protection back.</summary>
public static class RestorePointScript
{
    public static string Build(string quotedDescription) =>
        "$k='HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore';" +
        "$oldFreq=(Get-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency;" +
        "$disableSr=$null;" +
        "$probe=Get-ItemProperty -Path $k -Name DisableSR -ErrorAction SilentlyContinue;" +
        "if($null -ne $probe){ $disableSr=$probe.DisableSR };" +
        "Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord -ErrorAction SilentlyContinue;" +
        "try { Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction SilentlyContinue;" +
        $"Checkpoint-Computer -Description {quotedDescription} -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK' }}" +
        "catch { 'ERR: ' + $_.Exception.Message }" +
        "finally { " +
        "if($null -eq $oldFreq){ Remove-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue } " +
        "else { Set-ItemProperty -Path $k -Name SystemRestorePointCreationFrequency -Value $oldFreq -ErrorAction SilentlyContinue }; " +
        "if($disableSr -eq 1){ Disable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction SilentlyContinue; " +
        "Set-ItemProperty -Path $k -Name DisableSR -Value 1 -Type DWord -ErrorAction SilentlyContinue } " +
        "elseif($null -eq $disableSr){ Remove-ItemProperty -Path $k -Name DisableSR -ErrorAction SilentlyContinue } }";
}
