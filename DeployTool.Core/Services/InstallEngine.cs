using System.Diagnostics;
using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>
/// Executes a session: shortcuts and settings run first (fast, no retry), then installers run
/// sequentially — copy to a per-item temp folder, silent install, cleanup, 1x retry on failure.
/// </summary>
public sealed class InstallEngine(ShortcutPlacementService shortcutPlacer, SessionLogger? logger = null)
{
    // MSI success codes: 0 = OK, 3010 = success but reboot required.
    private static readonly HashSet<int> SuccessExitCodes = [0, 3010];

    public async Task RunAsync(IReadOnlyList<SessionItem> items, IProgress<SessionItemProgress>? progress, CancellationToken ct = default)
    {
        var selected = items.Where(i => i.IsSelected).ToList();
        logger?.WriteLine($"Sessie gestart met {selected.Count} geselecteerde item(en) "
            + $"({selected.Count(i => i.Kind == SessionItemKind.Installer)} software, "
            + $"{selected.Count(i => i.Kind == SessionItemKind.Shortcut)} snelkoppeling(en), "
            + $"{selected.Count(i => i.Kind == SessionItemKind.Setting)} instelling(en)).");

        foreach (var item in items.Where(i => i.Kind == SessionItemKind.Shortcut && i.IsSelected))
            await RunShortcutAsync(item, progress, ct);

        foreach (var item in items.Where(i => i.Kind == SessionItemKind.Setting && i.IsSelected))
            await RunSettingAsync(item, progress, ct);

        foreach (var item in items.Where(i => i.Kind == SessionItemKind.Installer && i.IsSelected))
            await RunInstallerWithRetryAsync(item, progress, ct);

        logger?.WriteLine("Sessie voltooid.");
    }

    /// <summary>Re-runs a single failed item, e.g. from the "opnieuw proberen" button.</summary>
    public Task RetryAsync(SessionItem item, IProgress<SessionItemProgress>? progress, CancellationToken ct = default)
    {
        logger?.WriteLine($"[{item.Name}] Handmatige nieuwe poging gestart.");
        return item.Kind switch
        {
            SessionItemKind.Installer => RunInstallerWithRetryAsync(item, progress, ct),
            SessionItemKind.Shortcut => RunShortcutAsync(item, progress, ct),
            SessionItemKind.Setting => RunSettingAsync(item, progress, ct),
            _ => Task.CompletedTask
        };
    }

    private async Task RunInstallerWithRetryAsync(SessionItem item, IProgress<SessionItemProgress>? progress, CancellationToken ct)
    {
        Report(item, ItemStatus.Running, progress);

        var (success, error) = await TryInstallOnceAsync(item, ct);
        if (!success)
        {
            logger?.WriteLine($"[{item.Name}] Eerste poging mislukt ({error}) — automatische nieuwe poging wordt gestart.");
            (success, error) = await TryInstallOnceAsync(item, ct);
        }

        Report(item, success ? ItemStatus.Succeeded : ItemStatus.Failed, progress, error);
    }

    private async Task<(bool Success, string? Error)> TryInstallOnceAsync(SessionItem item, CancellationToken ct)
    {
        var installer = item.Installer ?? throw new InvalidOperationException($"'{item.Name}' heeft geen installer-gegevens.");
        var tempDir = Path.Combine(Path.GetTempPath(), "PCSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var localPath = Path.Combine(tempDir, installer.FileName);

            logger?.WriteLine($"[{item.Name}] Kopiëren gestart: \"{installer.FullPath}\" -> \"{localPath}\"");
            var copyStopwatch = Stopwatch.StartNew();
            await CopyFileAsync(installer.FullPath, localPath, ct);
            copyStopwatch.Stop();
            var sizeMb = new FileInfo(localPath).Length / 1024.0 / 1024.0;
            logger?.WriteLine($"[{item.Name}] Kopiëren voltooid: {sizeMb:N1} MB in {copyStopwatch.Elapsed.TotalSeconds:N1}s.");

            var isMsi = Path.GetExtension(localPath).Equals(".msi", StringComparison.OrdinalIgnoreCase);
            var msiLogPath = isMsi ? Path.Combine(tempDir, "msiexec.log") : null;
            var psi = BuildProcessStartInfo(localPath, installer.SilentArgs, msiLogPath);

            logger?.WriteLine($"[{item.Name}] Installer starten: \"{psi.FileName}\" {psi.Arguments}");
            var runStopwatch = Stopwatch.StartNew();
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Kon installer niet starten.");
            await process.WaitForExitAsync(ct);
            runStopwatch.Stop();

            var success = SuccessExitCodes.Contains(process.ExitCode);
            logger?.WriteLine($"[{item.Name}] Installer afgesloten met exitcode {process.ExitCode} na {runStopwatch.Elapsed.TotalSeconds:N1}s "
                + $"— {(success ? "geslaagd" : "mislukt")}.");

            if (success) return (true, null);

            var error = $"Installer gaf exitcode {process.ExitCode}";
            if (msiLogPath is not null && File.Exists(msiLogPath))
            {
                var savedLogPath = PersistMsiLog(item.Name, msiLogPath);
                if (savedLogPath is not null)
                {
                    logger?.WriteLine($"[{item.Name}] Msiexec-logbestand bewaard: \"{savedLogPath}\"");
                    error += $" (msiexec-log: \"{savedLogPath}\")";
                }
            }

            return (false, error);
        }
        catch (Exception ex)
        {
            logger?.WriteLine($"[{item.Name}] Fout tijdens installatie: {ex.Message}");
            return (false, ex.Message);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
            logger?.WriteLine($"[{item.Name}] Tijdelijke map opgeruimd: \"{tempDir}\"");
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
        catch (Exception ex)
        {
            logger?.WriteLine($"[{item.Name}] Fout bij toepassen instelling: {ex.Message}");
            Report(item, ItemStatus.Failed, progress, ex.Message);
        }
    }

    private void Report(SessionItem item, ItemStatus status, IProgress<SessionItemProgress>? progress, string? error = null)
    {
        item.Status = status;
        item.ErrorMessage = error;
        progress?.Report(new SessionItemProgress(item, status, error));
        logger?.Write(new SessionLogEntry(DateTimeOffset.Now, item.Kind, item.Name, status, error));
    }

    /// <summary>.msi files aren't directly executable — they need to run through msiexec.exe.</summary>
    private static ProcessStartInfo BuildProcessStartInfo(string localPath, string silentArgs, string? msiLogPath)
    {
        if (Path.GetExtension(localPath).Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
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
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await src.CopyToAsync(dst, ct);
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
