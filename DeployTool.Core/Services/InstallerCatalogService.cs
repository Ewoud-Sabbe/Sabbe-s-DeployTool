using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Scans Installers\ on the share and merges each file with its metadata from installers.json.</summary>
public sealed class InstallerCatalogService(ShareLayout layout, InstallerMetadataStore metadataStore)
{
    private static readonly string[] Extensions = [".exe", ".msi"];

    public async Task<List<InstallerCatalogEntry>> DiscoverAsync(CancellationToken ct = default)
    {
        var definitions = await metadataStore.LoadAsync(ct);
        var byFileName = definitions.ToDictionary(d => d.FileName, StringComparer.OrdinalIgnoreCase);

        // Directory check + enumeration hit the network share — keep them off the calling (UI) thread.
        var files = await Task.Run(() => !Directory.Exists(layout.InstallersDir)
            ? new List<string>()
            : Directory.EnumerateFiles(layout.InstallersDir)
                .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList(), ct);

        var result = new List<InstallerCatalogEntry>();
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (byFileName.TryGetValue(fileName, out var def))
            {
                result.Add(new InstallerCatalogEntry
                {
                    FileName = fileName,
                    FullPath = file,
                    IsConfigured = true,
                    DisplayName = def.DisplayName,
                    SilentArgs = def.SilentArgs,
                    Category = def.Category,
                    DefaultSelected = def.DefaultSelected,
                });
            }
            else
            {
                result.Add(new InstallerCatalogEntry
                {
                    FileName = fileName,
                    FullPath = file,
                    IsConfigured = false,
                    DisplayName = fileName,
                    DefaultSelected = false,
                });
            }
        }

        return result;
    }
}
