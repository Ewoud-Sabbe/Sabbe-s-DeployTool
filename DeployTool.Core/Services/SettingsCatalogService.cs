using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DeployTool.Core.Models;
using DeployTool.Core.Polyfills;
using Microsoft.Win32;

namespace DeployTool.Core.Services;

/// <summary>Hardcoded, extensible list of Windows-setting actions. Add new entries here.</summary>
public sealed class SettingsCatalogService
{
    public List<SettingAction> GetAll() =>
    [
        new SettingAction
        {
            Name = "\"Laat Windows mijn standaardprinter beheren\" uitschakelen",
            DefaultSelected = true,
            Execute = (_) =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows");
                key.SetValue("LegacyDefaultPrinterMode", 1, RegistryValueKind.DWord);
                return Task.CompletedTask;
            }
        },
        new SettingAction
        {
            Name = "\"Deze pc\" en gebruikersmap tonen op bureaublad",
            DefaultSelected = true,
            Execute = (_) =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
                key.SetValue("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0, RegistryValueKind.DWord); // Deze pc
                key.SetValue("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", 0, RegistryValueKind.DWord); // Gebruikersmap

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                return Task.CompletedTask;
            }
        },
        new SettingAction
        {
            Name = "Datum en tijd synchroniseren",
            DefaultSelected = true,
            Execute = async ct =>
            {
                // w32tm /resync needs the W32Time service running — on a fresh client it's
                // Manual (Triggered) and often stopped, which fails resync with 0x80070426.
                var status = await RunAsync("sc.exe", "query w32time", ct);
                if (status.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    await RunAsync("sc.exe", "start w32time", ct);
                    for (var i = 0; i < 10; i++)
                    {
                        await Task.Delay(300, ct);
                        status = await RunAsync("sc.exe", "query w32time", ct);
                        if (status.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0) break;
                    }
                }

                await RunAsync("w32tm.exe", "/resync", ct);
            }
        },
        new SettingAction
        {
            Name = "Aan/uit-knop op \"Afsluiten\" zetten",
            DefaultSelected = true,
            Execute = async ct =>
            {
                // The classic Control Panel "power buttons" page applies to every power scheme
                // on the machine, not just the active one — set it everywhere so both the
                // modern Settings app and Control Panel show the same value.
                var list = await RunAsync("powercfg.exe", "/list", ct);
                var guids = Regex.Matches(list, @"Power Scheme GUID:\s*([0-9a-fA-F-]{36})")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToList();

                if (guids.Count == 0)
                    throw new InvalidOperationException("Geen energieschema's gevonden via 'powercfg /list'.");

                foreach (var guid in guids)
                {
                    await RunAsync("powercfg.exe", $"/setacvalueindex {guid} sub_buttons pbuttonaction 3", ct);
                    await RunAsync("powercfg.exe", $"/setdcvalueindex {guid} sub_buttons pbuttonaction 3", ct);
                }

                await RunAsync("powercfg.exe", "/setactive scheme_current", ct);
            }
        },
        new SettingAction
        {
            Name = "NumLock automatisch aanzetten",
            DefaultSelected = true,
            Execute = (_) =>
            {
                using (var userKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Keyboard"))
                    userKey.SetValue("InitialKeyboardIndicators", "2", RegistryValueKind.String);

                using (var defaultKey = Registry.Users.CreateSubKey(@".DEFAULT\Control Panel\Keyboard"))
                    defaultKey.SetValue("InitialKeyboardIndicators", "2", RegistryValueKind.String);

                return Task.CompletedTask;
            }
        },
        new SettingAction
        {
            Name = "Bloatware verwijderen (McAfee, NordVPN)",
            DefaultSelected = true,
            Execute = async ct =>
            {
                var matches = FindInstalledPrograms(name =>
                    name.IndexOf("mcafee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("nordvpn", StringComparison.OrdinalIgnoreCase) >= 0);

                var failures = new List<string>();
                foreach (var program in matches)
                {
                    try
                    {
                        await UninstallProgramAsync(program, ct);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{program.DisplayName}: {ex.Message}");
                    }
                }

                if (failures.Count > 0)
                    throw new InvalidOperationException(
                        $"{failures.Count} van {matches.Count} programma('s) niet volledig verwijderd: {string.Join("; ", failures)}");
            }
        },
    ];

    private static async Task<string> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Kon {fileName} niet starten.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} {arguments} gaf exitcode {process.ExitCode}: {stderr}{stdout}".Trim());

        return stdout;
    }

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private sealed record InstalledProgram(string DisplayName, string? UninstallString, string? QuietUninstallString);

    /// <summary>Scans the Uninstall registry (both bitness views + HKCU) for entries whose DisplayName matches.
    /// A single vendor — McAfee especially — can register several separate entries.</summary>
    private static List<InstalledProgram> FindInstalledPrograms(Func<string, bool> nameMatches)
    {
        var results = new List<InstalledProgram>();

        (RegistryHive Hive, RegistryView View)[] locations =
        [
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
        ];

        foreach (var (hive, view) in locations)
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

    /// <summary>
    /// Runs the program's own registered uninstaller silently. Vendor uninstallers report success
    /// with wildly inconsistent exit codes, so instead of trusting the exit code, this re-checks
    /// the registry afterwards — if the entry is gone, it worked, regardless of what it returned.
    /// </summary>
    private static async Task UninstallProgramAsync(InstalledProgram program, CancellationToken ct)
    {
        var command = program.QuietUninstallString ?? program.UninstallString;
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("geen uninstall-commando gevonden in het register.");

        var (fileName, arguments) = ParseUninstallCommand(command);

        // MSI-based uninstalls have a reliable silent switch — force it even if the registry
        // string itself wasn't already silent (most QuietUninstallString values are, but plain
        // UninstallString rarely is).
        if (fileName.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) &&
            arguments.IndexOf("/qn", StringComparison.OrdinalIgnoreCase) < 0 &&
            arguments.IndexOf("/quiet", StringComparison.OrdinalIgnoreCase) < 0)
        {
            arguments += " /qn /norestart";
        }

        var psi = new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("kon de uninstaller niet starten.");
        await process.WaitForExitAsync(ct);

        var stillPresent = FindInstalledPrograms(n => string.Equals(n, program.DisplayName, StringComparison.OrdinalIgnoreCase)).Count > 0;
        if (stillPresent)
            throw new InvalidOperationException($"nog steeds aanwezig na uninstall-poging (exitcode {process.ExitCode}).");
    }

    /// <summary>Splits a registry uninstall command into its executable and argument string — handles
    /// both a quoted path ("C:\...\uninstall.exe" -args) and an unquoted one (MsiExec.exe /X{guid}).</summary>
    private static (string FileName, string Arguments) ParseUninstallCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var closingQuote = command.IndexOf('"', 1);
            if (closingQuote > 0)
                return (command[1..closingQuote], command[(closingQuote + 1)..].Trim());
        }

        var spaceIndex = command.IndexOf(' ');
        return spaceIndex < 0 ? (command, string.Empty) : (command[..spaceIndex], command[(spaceIndex + 1)..].Trim());
    }
}
