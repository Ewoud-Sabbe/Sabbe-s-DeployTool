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

            var psi = new ProcessStartInfo(localPath, installer.SilentArgs)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var displayArgs = string.IsNullOrWhiteSpace(installer.SilentArgs) ? "(geen argumenten)" : installer.SilentArgs;
            logger?.WriteLine($"[{item.Name}] Installer starten: \"{localPath}\" {displayArgs}");
            var runStopwatch = Stopwatch.StartNew();
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Kon installer niet starten.");
            await process.WaitForExitAsync(ct);
            runStopwatch.Stop();

            var success = SuccessExitCodes.Contains(process.ExitCode);
            logger?.WriteLine($"[{item.Name}] Installer afgesloten met exitcode {process.ExitCode} na {runStopwatch.Elapsed.TotalSeconds:N1}s "
                + $"— {(success ? "geslaagd" : "mislukt")}.");

            return success ? (true, null) : (false, $"Installer gaf exitcode {process.ExitCode}");
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
