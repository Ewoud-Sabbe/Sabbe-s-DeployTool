using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Scans Shortcuts\ on the share for .url/.lnk/.exe files. Same drop-in UX as installers — no JSON.</summary>
public sealed class ShortcutCatalogService(ShareLayout layout)
{
    private static readonly string[] Extensions = [".url", ".lnk", ".exe"];

    public Task<List<ShortcutCatalogEntry>> DiscoverAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(layout.ShortcutsDir)) return Task.FromResult(new List<ShortcutCatalogEntry>());

        var entries = Directory.EnumerateFiles(layout.ShortcutsDir)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new ShortcutCatalogEntry
            {
                FileName = Path.GetFileName(f),
                FullPath = f,
                DisplayName = Path.GetFileNameWithoutExtension(f)
            })
            .ToList();

        return Task.FromResult(entries);
    }
}
