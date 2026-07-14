using System.Text.Json;
using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Reads/writes Config\installers.json — the shared metadata for all installers, used by all PCs.</summary>
public sealed class InstallerMetadataStore(ShareLayout layout)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public async Task<List<InstallerDefinition>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(layout.InstallersJsonPath)) return [];
        using var stream = File.OpenRead(layout.InstallersJsonPath);
        var items = await JsonSerializer.DeserializeAsync<List<InstallerDefinition>>(stream, Options, ct);
        return items ?? [];
    }

    public async Task SaveAsync(List<InstallerDefinition> definitions, CancellationToken ct = default)
    {
        Directory.CreateDirectory(layout.ConfigDir);
        using var stream = File.Create(layout.InstallersJsonPath);
        await JsonSerializer.SerializeAsync(stream, definitions, Options, ct);
    }

    /// <summary>Adds or replaces one definition by FileName and persists the full list.</summary>
    public async Task UpsertAsync(InstallerDefinition definition, CancellationToken ct = default)
    {
        var items = await LoadAsync(ct);
        var idx = items.FindIndex(i => string.Equals(i.FileName, definition.FileName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) items[idx] = definition; else items.Add(definition);
        await SaveAsync(items, ct);
    }
}
