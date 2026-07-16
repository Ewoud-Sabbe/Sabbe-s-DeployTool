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
}
