using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Scans Shortcuts\ on the share for .url/.lnk/.exe files. Same drop-in UX as installers — no JSON.</summary>
public sealed class ShortcutCatalogService(ShareLayout layout)
{
    private static readonly string[] Extensions = [".url", ".lnk", ".exe"];

    // Directory check + enumeration hit the network share — keep them off the calling (UI) thread.
    public Task<List<ShortcutCatalogEntry>> DiscoverAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        if (!Directory.Exists(layout.ShortcutsDir)) return new List<ShortcutCatalogEntry>();

        return Directory.EnumerateFiles(layout.ShortcutsDir)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new ShortcutCatalogEntry
            {
                FileName = Path.GetFileName(f),
                FullPath = f,
                DisplayName = Path.GetFileNameWithoutExtension(f)
            })
            .ToList();
    }, ct);
}
