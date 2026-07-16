using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DeployTool.Core.Models;
using Microsoft.Win32;

namespace DeployTool.Core.Services;

/// <summary>Hardcoded, extensible list of Windows-setting actions. Add new entries here.</summary>
public sealed class SettingsCatalogService(ShareLayout layout)
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
                if (!status.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                {
                    await RunAsync("sc.exe", "start w32time", ct);
                    for (var i = 0; i < 10; i++)
                    {
                        await Task.Delay(300, ct);
                        status = await RunAsync("sc.exe", "query w32time", ct);
                        if (status.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) break;
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
            Name = "McAfee verwijderen",
            DefaultSelected = true,
            Execute = async ct =>
            {
                var installed = FindInstalledPrograms(name => name.IndexOf("mcafee", StringComparison.OrdinalIgnoreCase) >= 0);
                if (installed.Count == 0) return;

                // McAfee's own per-product uninstallers (LiveSafe, WebAdvisor, Safe Connect, ...)
                // essentially never honor silent flags — they open an interactive wizard
                // regardless of what's passed. McAfee's own cleanup engine normally handles this
                // (mccleanup.exe, bundled inside their MCPR removal tool) — but recent MCPR builds
                // deliberately reject running mccleanup.exe standalone (exitcode 2, confirmed with
                // both the current and an older/OldCert-signed build), and their McClnUI.exe GUI
                // wrapper that *does* work still pops an interactive wizard even with -s. So this
                // tries mccleanup.exe first as a best effort (works for some components on some
                // machines), then falls back to each still-installed entry's own registered
                // uninstaller (same generic path as "NordVPN verwijderen", already fixed to handle
                // the /I-vs-/X MSI bug and Inno Setup's /VERYSILENT requirement) for whatever's
                // left. Needs Config\McCleanup\ staged on the share; see README.
                var mccleanupSourceDir = Path.Combine(layout.ConfigDir, "McCleanup");
                var mccleanupSource = Path.Combine(mccleanupSourceDir, "mccleanup.exe");
                if (File.Exists(mccleanupSource))
                {
                    var localDir = Path.Combine(Path.GetTempPath(), "PCSetup", "McCleanup");
                    CopyDirectory(mccleanupSourceDir, localDir);
                    var localMccleanup = Path.Combine(localDir, "mccleanup.exe");

                    // Exact same component list McAfee's own StartCleanup.bat passes to McClnUI.exe.
                    const string components = "StopServices,MFSY,PEF,MXD,CSP,Sustainability,MOCP,MFP,APPSTATS,Auth,EMproxy,FWdiver,HW,MAS,MAT,MBK,MCPR,McProxy,McSvcHost,VUL,MHN,MNA,MOBK,MPFP,MPFPCU,MPS,SHRED,MPSCU,MQC,MQCCU,MSAD,MSHR,MSK,MSKCU,MWL,NMC,RedirSvc,VS,REMEDIATION,MSC,YAP,TRUEKEY,LAM,PCB,Symlink,SafeConnect,MGS,WMIRemover,RESIDUE";

                    try
                    {
                        var psi = new ProcessStartInfo(localMccleanup, $"-p {components} -s")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = localDir,
                        };
                        using var process = Process.Start(psi) ?? throw new InvalidOperationException("kon mccleanup.exe niet starten.");
                        await process.WaitForExitAsync(ct);
                    }
                    finally
                    {
                        TryDeleteDirectory(localDir);
                    }
                }

                // Deliberately not falling back to each remaining entry's own registered
                // uninstaller here (unlike NordVPN): confirmed on a real machine that McAfee's
                // core product uninstaller opens an interactive window regardless of flags, and
                // worse, the app then hangs waiting for that window to close even after the user
                // finishes it by hand — an unattended session has no way to click it, so it can
                // only ever get stuck. mccleanup.exe above already silently removes what it can
                // (e.g. WebAdvisor); whatever's left after that is reported here so it's visible
                // in the log, without launching anything that could block the session.
                var stillPresent = FindInstalledPrograms(name => name.IndexOf("mcafee", StringComparison.OrdinalIgnoreCase) >= 0);
                if (stillPresent.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"{stillPresent.Count} programma('s) vereisen handmatige verwijdering (McAfee's eigen uninstaller "
                        + $"laat zich niet silent aansturen): {string.Join(", ", stillPresent.Select(p => p.DisplayName))}");
                }
            }
        },
        new SettingAction
        {
            Name = "NordVPN verwijderen",
            DefaultSelected = true,
            Execute = async ct =>
            {
                var matches = FindInstalledPrograms(name => name.IndexOf("nordvpn", StringComparison.OrdinalIgnoreCase) >= 0);

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
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
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

        if (fileName.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Some vendors (NordVPN's Advanced Installer package among them) register an
            // UninstallString using /I{guid} (install/repair) instead of /X{guid} (uninstall) —
            // running it as-is just silently repairs/reinstalls the product, which is why it
            // reports success (exit 0) while the program is still there afterwards. /X is the
            // standard uninstall verb every MSI package must support, regardless of what the
            // (buggy) registry string says.
            arguments = Regex.Replace(arguments, @"/I\{", "/X{", RegexOptions.IgnoreCase);

            // MSI-based uninstalls have a reliable silent switch — force it even if the registry
            // string itself wasn't already silent (most QuietUninstallString values are, but
            // plain UninstallString rarely is).
            if (arguments.IndexOf("/qn", StringComparison.OrdinalIgnoreCase) < 0 &&
                arguments.IndexOf("/quiet", StringComparison.OrdinalIgnoreCase) < 0)
            {
                arguments += " /qn /norestart";
            }
        }

        // Inno Setup uninstallers (NordVPN among many others) are named unins###.exe by
        // convention and use /VERYSILENT, not /qn — without it they just show a confirmation
        // dialog, which with no window renders as "ran instantly, exit 0, did nothing".
        if (Regex.IsMatch(Path.GetFileName(fileName), @"^unins\d*\.exe$", RegexOptions.IgnoreCase) &&
            arguments.IndexOf("silent", StringComparison.OrdinalIgnoreCase) < 0)
        {
            arguments += " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
        }

        var psi = new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("kon de uninstaller niet starten.");
        await process.WaitForExitAsync(ct);

        var stillPresent = FindInstalledPrograms(n => string.Equals(n, program.DisplayName, StringComparison.OrdinalIgnoreCase)).Count > 0;
        if (stillPresent)
            throw new InvalidOperationException($"nog steeds aanwezig na uninstall-poging (exitcode {process.ExitCode}, commando: \"{fileName}\" {arguments}).");
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

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup — a locked file here shouldn't fail the session
        }
    }
}
