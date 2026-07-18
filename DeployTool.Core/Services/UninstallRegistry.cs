using Microsoft.Win32;

namespace DeployTool.Core.Services;

/// <summary>One installed program as registered in the Uninstall registry.</summary>
internal sealed record InstalledProgram(string DisplayName, string? UninstallString, string? QuietUninstallString);

/// <summary>
/// Scans "Programma's en onderdelen" (the Uninstall registry) across both bitness views + HKCU.
/// Shared by the already-installed check (InstallEngine) and the bloatware-removal actions.
/// </summary>
internal static class UninstallRegistry
{
    private static readonly (RegistryHive Hive, RegistryView View)[] Locations =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.CurrentUser, RegistryView.Registry64),
    ];

    /// <summary>All entries whose DisplayName matches. A single vendor — McAfee especially — can
    /// register several separate entries.</summary>
    public static List<InstalledProgram> FindInstalledPrograms(Func<string, bool> nameMatches)
    {
        var results = new List<InstalledProgram>();

        foreach (var (hive, view) in Locations)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstallKey is null) continue;

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                if (subKey?.GetValue("DisplayName") is not string displayName || string.IsNullOrWhiteSpace(displayName))
                    continue;

                if (!nameMatches(displayName)) continue;

                results.Add(new InstalledProgram(
                    displayName,
                    subKey.GetValue("UninstallString") as string,
                    subKey.GetValue("QuietUninstallString") as string));
            }
        }

        return results;
    }
}
