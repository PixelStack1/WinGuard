using System.Security.Principal;
using Microsoft.Win32;

namespace WinGuard.Services;

public static class SystemInfo
{
    public static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string Edition()
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string name = k?.GetValue("ProductName") as string ?? "Windows";
            string build = k?.GetValue("CurrentBuild") as string ?? "";
            string disp = k?.GetValue("DisplayVersion") as string ?? "";
            if (int.TryParse(build, out int b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
            return $"{name} {disp} (build {build})";
        }
        catch { return "Windows"; }
    }

    public static bool IsHomeEdition()
    {
        using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return (k?.GetValue("EditionID") as string ?? "").StartsWith("Core", StringComparison.OrdinalIgnoreCase);
    }
}
