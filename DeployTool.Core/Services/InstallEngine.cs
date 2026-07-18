using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using DeployTool.Core.Models;
using DeployTool.Core.Polyfills;

namespace DeployTool.Core.Services;

/// <summary>
/// Executes a session: shortcuts and settings run first (fast, no retry), then installers run
/// in order — copy to a per-item temp folder, silent install, cleanup, 1x retry on failure.
/// While one installer runs (local, no network use), the next one's copy is already prefetched
/// in the background so the network link isn't sitting idle during a multi-minute install.
/// </summary>
public sealed class InstallEngine(ShortcutPlacementService shortcutPlacer, SessionLogger? logger = null)
{
    // MSI success codes: 0 = OK, 3010 = success but reboot required,
    // 1641 = success and a reboot was initiated (some .exe installers return this too).
    private static readonly HashSet<int> SuccessExitCodes = [0, 3010, 1641];

    // INSTALLSTATE values (msi.h) that mean the product is present on the machine.
    private static readonly HashSet<int> InstalledProductStates = [3, 4, 5]; // LOCAL, SOURCE, DEFAULT

    public async Task RunAsync(IReadOnlyList<SessionItem> items, IProgress<SessionItemProgress>? progress, CancellationToken ct = default)
    {
        var selected = items.Where(i => i.IsSelected).ToList();
        logger?.WriteLine($"Sessie gestart met {selected.Count} geselecteerde item(en) "
            + $"({selected.Count(i => i.Kind == SessionItemKind.Installer)} software, "
            + $"{selected.Count(i => i.Kind == SessionItemKind.Shortcut)} snelkoppeling(en), "
            + $"{selected.Count(i => i.Kind == SessionItemKind.Setting)} instelling(en), "
            + $"{selected.Count(i => i.Kind == SessionItemKind.Bloatware)} bloatware-item(en)).");

        var installerItems = items.Where(i => i.Kind == SessionItemKind.Installer && i.IsSelected).ToList();

        // Start the first installer's network copy right away: the settings/bloatware phase can
        // run for minutes (McAfee's interactive fallback alone is bounded at 5) while the network
        // link would otherwise sit completely idle the whole time.
        var firstPrepare = installerItems.Count > 0 ? PrepareAsync(installerItems[0], ct) : null;

        try
        {
            foreach (var item in items.Where(i => i.Kind == SessionItemKind.Shortcut && i.IsSelected))
            {
                ct.ThrowIfCancellationRequested();
                await RunShortcutAsync(item, progress, ct);
            }

            foreach (var item in items.Where(i => i.Kind is SessionItemKind.Setting or SessionItemKind.Bloatware && i.IsSelected))
            {
                ct.ThrowIfCancellationRequested();
                await RunSettingAsync(item, progress, ct);
            }

            await RunInstallersPipelinedAsync(installerItems, firstPrepare, progress, ct);

            logger?.WriteLine("Sessie voltooid.");
        }
        catch (OperationCanceledException)
        {
            // If cancellation hit before the installer phase consumed the prefetch, drain it here
            // so its temp folder doesn't leak. (When it was consumed, this is a no-op.)
            if (firstPrepare is not null)
            {
                try { FileSystemHelpers.TryDeleteDirectory((await firstPrepare).TempDir); }
                catch (OperationCanceledException) { /* its temp dir was already cleaned up */ }
            }

            // Whatever was mid-flight gets marked; untouched items simply stay Pending.
            foreach (var item in selected.Where(i => i.Status == ItemStatus.Running))
                Report(item, ItemStatus.Failed, progress, "Geannuleerd");
            logger?.WriteLine("Sessie geannuleerd — een al gestart(e) installer/uninstaller draait mogelijk nog door.");
            throw;
        }
    }

    /// <summary>Re-runs a single failed item, e.g. from the "opnieuw proberen" button.</summary>
    public async Task RetryAsync(SessionItem item, IProgress<SessionItemProgress>? progress, CancellationToken ct = default)
    {
        logger?.WriteLine($"[{item.Name}] Handmatige nieuwe poging gestart.");
        try
        {
            await (item.Kind switch
            {
                SessionItemKind.Installer => RunInstallerWithRetryAsync(item, progress, ct),
                SessionItemKind.Shortcut => RunShortcutAsync(item, progress, ct),
                SessionItemKind.Setting or SessionItemKind.Bloatware => RunSettingAsync(item, progress, ct),
                _ => Task.CompletedTask
            });
        }
        catch (OperationCanceledException)
        {
            if (item.Status == ItemStatus.Running)
                Report(item, ItemStatus.Failed, progress, "Geannuleerd");
            logger?.WriteLine($"[{item.Name}] Nieuwe poging geannuleerd.");
            throw;
        }
    }

    /// <summary>
    /// Runs installers in order, but starts copying installer N+1 as soon as installer N's own
    /// copy finishes — so that copy happens in the background while N is actually installing
    /// (a purely local, network-idle phase that can easily run for minutes on a large installer).
    /// </summary>
    private async Task RunInstallersPipelinedAsync(IReadOnlyList<SessionItem> installerItems, Task<PrepareOutcome>? initialPrepare, IProgress<SessionItemProgress>? progress, CancellationToken ct)
    {
        if (installerItems.Count == 0) return;

        var nextPrepare = initialPrepare ?? PrepareAsync(installerItems[0], ct);

        try
        {
            for (var i = 0; i < installerItems.Count; i++)
            {
                var item = installerItems[i];
                Report(item, ItemStatus.Running, progress);

                var prepared = await nextPrepare!;

                // This item's copy just finished — kick off the next one now, so it overlaps with
                // this item's install instead of only starting once the install is done.
                nextPrepare = i + 1 < installerItems.Count ? PrepareAsync(installerItems[i + 1], ct) : null;

                var (status, detail) = await InstallWithRetryAsync(item, prepared, ct);
                Report(item, status, progress, detail);
            }
        }
        finally
        {
            // A cancelled run can leave the next item's prefetch in flight — wait for it and
            // clean up its temp folder so cancellation doesn't leak a half-copied installer.
            if (nextPrepare is not null)
            {
                try { FileSystemHelpers.TryDeleteDirectory((await nextPrepare).TempDir); }
                catch (OperationCanceledException) { /* its temp dir was already cleaned up */ }
            }
        }
    }

    private async Task RunInstallerWithRetryAsync(SessionItem item, IProgress<SessionItemProgress>? progress, CancellationToken ct)
    {
        Report(item, ItemStatus.Running, progress);
        var prepared = await PrepareAsync(item, ct);
        var (status, detail) = await InstallWithRetryAsync(item, prepared, ct);
        Report(item, status, progress, detail);
    }

    private async Task<(ItemStatus Status, string? Detail)> InstallWithRetryAsync(SessionItem item, PrepareOutcome prepared, CancellationToken ct)
    {
        var (status, detail) = await ExecutePreparedAsync(item, prepared, ct);
        if (status == ItemStatus.Failed)
        {
            logger?.WriteLine($"[{item.Name}] Eerste poging mislukt ({detail}) — automatische nieuwe poging wordt gestart.");
            var retryPrepared = await PrepareAsync(item, ct);
            (status, detail) = await ExecutePreparedAsync(item, retryPrepared, ct);
        }

        return (status, detail);
    }

    /// <summary>Result of the network-bound half of an install: either a short-circuit outcome
    /// (already installed / copy failed) or a locally-copied installer ready to run.</summary>
    private sealed class PrepareOutcome
    {
        public required string TempDir { get; init; }
        public (ItemStatus Status, string? Detail)? Completed { get; init; }
        public string? LocalPath { get; init; }
        public string? MsiLogPath { get; init; }
    }

    /// <summary>Already-installed check + copy to a local temp folder. Network-bound, safe to run
    /// concurrently with another item's (local) install.</summary>
    private async Task<PrepareOutcome> PrepareAsync(SessionItem item, CancellationToken ct)
    {
        var installer = item.Installer ?? throw new InvalidOperationException($"'{item.Name}' heeft geen installer-gegevens.");
        var tempDir = Path.Combine(Path.GetTempPath(), "PCSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var isMsi = Path.GetExtension(installer.FileName).Equals(".msi", StringComparison.OrdinalIgnoreCase);

            // Check before copying: no point pulling a multi-GB installer over the network
            // just to find out it's already installed. On a background thread: the ProductCode
            // check opens the .msi in place on the share (MsiOpenPackageW), which would otherwise
            // block the calling (UI) thread for seconds on a slow network.
            var alreadyInstalled = await Task.Run(() => TryGetAlreadyInstalledMessage(installer.FullPath, isMsi, installer.DisplayName), ct);
            if (alreadyInstalled is { } alreadyInstalledMessage)
            {
                logger?.WriteLine($"[{item.Name}] {alreadyInstalledMessage}");
                return new PrepareOutcome { TempDir = tempDir, Completed = (ItemStatus.AlreadyInstalled, alreadyInstalledMessage) };
            }

            var localPath = Path.Combine(tempDir, installer.FileName);

            logger?.WriteLine($"[{item.Name}] Kopiëren gestart: \"{installer.FullPath}\" -> \"{localPath}\"");
            var copyStopwatch = Stopwatch.StartNew();
            await CopyFileAsync(installer.FullPath, localPath, ct);
            copyStopwatch.Stop();
            var sizeMb = new FileInfo(localPath).Length / 1024.0 / 1024.0;
            logger?.WriteLine($"[{item.Name}] Kopiëren voltooid: {sizeMb:N1} MB in {copyStopwatch.Elapsed.TotalSeconds:N1}s.");

            var msiLogPath = isMsi ? Path.Combine(tempDir, "msiexec.log") : null;
            return new PrepareOutcome { TempDir = tempDir, LocalPath = localPath, MsiLogPath = msiLogPath };
        }
        catch (OperationCanceledException)
        {
            FileSystemHelpers.TryDeleteDirectory(tempDir);
            throw;
        }
        catch (Exception ex)
        {
            logger?.WriteLine($"[{item.Name}] Fout tijdens kopiëren: {ex.Message}");
            return new PrepareOutcome { TempDir = tempDir, Completed = (ItemStatus.Failed, ex.Message) };
        }
    }

    /// <summary>Runs the already-copied installer (or resolves a short-circuit outcome from Prepare) and cleans up its temp folder.</summary>
    private async Task<(ItemStatus Status, string? Detail)> ExecutePreparedAsync(SessionItem item, PrepareOutcome prepared, CancellationToken ct)
    {
        if (prepared.Completed is { } completed)
        {
            FileSystemHelpers.TryDeleteDirectory(prepared.TempDir);
            return completed;
        }

        try
        {
            var psi = BuildProcessStartInfo(prepared.LocalPath!, item.Installer!.SilentArgs, prepared.MsiLogPath);

            logger?.WriteLine($"[{item.Name}] Installer starten: \"{psi.FileName}\" {psi.Arguments}");
            var runStopwatch = Stopwatch.StartNew();
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Kon installer niet starten.");
            await process.WaitForExitAsync(ct);
            runStopwatch.Stop();

            var success = SuccessExitCodes.Contains(process.ExitCode);
            logger?.WriteLine($"[{item.Name}] Installer afgesloten met exitcode {process.ExitCode} na {runStopwatch.Elapsed.TotalSeconds:N1}s "
                + $"— {(success ? "geslaagd" : "mislukt")}.");

            if (success) return (ItemStatus.Succeeded, null);

            var error = $"Installer gaf exitcode {process.ExitCode}";
            if (prepared.MsiLogPath is not null && File.Exists(prepared.MsiLogPath))
            {
                var savedLogPath = PersistMsiLog(item.Name, prepared.MsiLogPath);
                if (savedLogPath is not null)
                {
                    logger?.WriteLine($"[{item.Name}] Msiexec-logbestand bewaard: \"{savedLogPath}\"");
                    error += $" (msiexec-log: \"{savedLogPath}\")";
                }
            }

            return (ItemStatus.Failed, error);
        }
        catch (OperationCanceledException)
        {
            logger?.WriteLine($"[{item.Name}] Installatie geannuleerd — het installerproces draait mogelijk nog door.");
            throw;
        }
        catch (Exception ex)
        {
            logger?.WriteLine($"[{item.Name}] Fout tijdens installatie: {ex.Message}");
            return (ItemStatus.Failed, ex.Message);
        }
        finally
        {
            FileSystemHelpers.TryDeleteDirectory(prepared.TempDir);
            logger?.WriteLine($"[{item.Name}] Tijdelijke map opgeruimd: \"{prepared.TempDir}\"");
        }
    }

    private Task RunShortcutAsync(SessionItem item, IProgress<SessionItemProgress>? progress, CancellationToken ct)
    {
        Report(item, ItemStatus.Running, progress);
        try
        {
            var shortcut = item.Shortcut ?? throw new InvalidOperationException($"'{item.Name}' heeft geen snelkoppeling-gegevens.");
            logger?.WriteLine($"[{item.Name}] Snelkoppeling plaatsen: \"{shortcut.FullPath}\" -> bureaublad");
            shortcutPlacer.Place(shortcut);
            logger?.WriteLine($"[{item.Name}] Snelkoppeling geplaatst.");
            Report(item, ItemStatus.Succeeded, progress);
        }
        catch (Exception ex)
        {
            logger?.WriteLine($"[{item.Name}] Fout bij plaatsen snelkoppeling: {ex.Message}");
            Report(item, ItemStatus.Failed, progress, ex.Message);
        }
        return Task.CompletedTask;
    }

    private async Task RunSettingAsync(SessionItem item, IProgress<SessionItemProgress>? progress, CancellationToken ct)
    {
        Report(item, ItemStatus.Running, progress);
        try
        {
            var setting = item.Setting ?? throw new InvalidOperationException($"'{item.Name}' heeft geen instelling-actie.");
            logger?.WriteLine($"[{item.Name}] Instelling toepassen...");
            var stopwatch = Stopwatch.StartNew();
            await setting.Execute(ct);
            stopwatch.Stop();
            logger?.WriteLine($"[{item.Name}] Instelling toegepast in {stopwatch.Elapsed.TotalMilliseconds:N0}ms.");
            Report(item, ItemStatus.Succeeded, progress);
        }
        catch (OperationCanceledException)
        {
            Report(item, ItemStatus.Failed, progress, "Geannuleerd");
            throw;
        }
        catch (Exception ex)
        {
            logger?.WriteLine($"[{item.Name}] Fout bij toepassen instelling: {ex.Message}");
            Report(item, ItemStatus.Failed, progress, ex.Message);
        }
    }

    private void Report(SessionItem item, ItemStatus status, IProgress<SessionItemProgress>? progress, string? detail = null)
    {
        item.Status = status;
        item.ErrorMessage = detail;
        progress?.Report(new SessionItemProgress(item, status, detail));
        logger?.Write(new SessionLogEntry(DateTimeOffset.Now, item.Kind, item.Name, status, detail));
    }

    /// <summary>
    /// For .msi: asks Windows Installer directly via the package's own ProductCode (authoritative).
    /// For both .msi and .exe: falls back to matching the configured display name against
    /// "Programma's en onderdelen" (Uninstall registry), since most .exe installers report
    /// success even when there was nothing to do — 0 either way.
    /// </summary>
    private static string? TryGetAlreadyInstalledMessage(string localPath, bool isMsi, string displayName)
    {
        if (isMsi)
        {
            var productCode = TryGetMsiProductCode(localPath);
            if (productCode is not null && InstalledProductStates.Contains(MsiQueryProductStateW(productCode)))
                return $"Al geïnstalleerd (ProductCode {productCode}) — installatie overgeslagen.";
        }

        if (TryFindInstalledDisplayName(displayName) is { } installedName)
            return $"Al geïnstalleerd (gevonden in \"Programma's en onderdelen\" als \"{installedName}\") — installatie overgeslagen.";

        return null;
    }

    /// <summary>Matches a configured display name against installed programs (both registry bitness views + HKCU),
    /// comparing whole words so "Office" doesn't match "LibreOffice".</summary>
    private static string? TryFindInstalledDisplayName(string displayName)
    {
        var hint = NormalizeProgramName(displayName);
        if (hint.Length == 0) return null;

        return UninstallRegistry.FindInstalledPrograms(installedName =>
        {
            var normalizedInstalled = NormalizeProgramName(installedName);
            return normalizedInstalled.Length > 0 && ProgramNamesMatch(hint, normalizedInstalled);
        }).FirstOrDefault()?.DisplayName;
    }

    /// <summary>
    /// True when the shorter name's words all appear, in order, in the longer name's words.
    /// Substring matching was too loose here: "office" is contained in "libreoffice", which made
    /// a configured "Office" report LibreOffice machines as already installed. Word-level matching
    /// still links "Teams" to "Microsoft Teams" and "Adobe Reader" to "Adobe Acrobat Reader DC".
    /// </summary>
    private static bool ProgramNamesMatch(string normalizedA, string normalizedB)
    {
        var a = normalizedA.Split(' ');
        var b = normalizedB.Split(' ');
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);

        var pos = 0;
        foreach (var word in shorter)
        {
            pos = Array.IndexOf(longer, word, pos);
            if (pos < 0) return false;
            pos++;
        }

        return true;
    }

    /// <summary>Lowercases and strips version numbers / "(64-bit)"-style suffixes so names compare sensibly.</summary>
    private static string NormalizeProgramName(string name)
    {
        var normalized = name.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\(.*?\)", "");
        normalized = Regex.Replace(normalized, @"[\d.]+", "");
        normalized = Regex.Replace(normalized, @"[^a-z]+", " ");
        return normalized.Trim();
    }

    private static string? TryGetMsiProductCode(string msiPath)
    {
        var handle = IntPtr.Zero;
        try
        {
            if (MsiOpenPackageW(msiPath, out handle) != 0) return null;

            var buffer = new StringBuilder(64);
            var size = buffer.Capacity;
            return MsiGetPropertyW(handle, "ProductCode", buffer, ref size) == 0 ? buffer.ToString() : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero) MsiCloseHandle(handle);
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenPackageW(string szPackagePath, out IntPtr hProduct);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiGetPropertyW(IntPtr hInstall, string szName, StringBuilder szValueBuf, ref int pcchValueBuf);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern int MsiCloseHandle(IntPtr hAny);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MsiQueryProductStateW(string szProduct);

    /// <summary>.msi files aren't directly executable — they need to run through msiexec.exe.</summary>
    private static ProcessStartInfo BuildProcessStartInfo(string localPath, string silentArgs, string? msiLogPath)
    {
        if (Path.GetExtension(localPath).Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            // Under /qn, msiexec reboots the machine *automatically* when a package asks for it —
            // mid-session, unattended. /norestart suppresses that; the install then returns 3010
            // ("success, reboot required"), which is already treated as success above.
            if (silentArgs.IndexOf("/norestart", StringComparison.OrdinalIgnoreCase) < 0)
                silentArgs = $"{silentArgs} /norestart".TrimStart();

            var args = msiLogPath is null
                ? $"/i \"{localPath}\" {silentArgs}".TrimEnd()
                : $"/i \"{localPath}\" {silentArgs} /l*v \"{msiLogPath}\"".TrimEnd();

            return new ProcessStartInfo("msiexec.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        return new ProcessStartInfo(localPath, silentArgs)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    /// <summary>Copies the msiexec verbose log out of the (about to be deleted) temp folder for later troubleshooting.</summary>
    private string? PersistMsiLog(string itemName, string msiLogPath)
    {
        if (logger is null) return null;
        try
        {
            var logsDir = Path.GetDirectoryName(logger.LogPath)!;
            var fileName = $"{SanitizeFileName(itemName)}_msiexec_{DateTime.Now:HHmmssfff}.log";
            var destination = Path.Combine(logsDir, fileName);
            File.Copy(msiLogPath, destination, overwrite: true);
            return destination;
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await src.CopyToAsync(dst, 81920, ct);
    }
}
