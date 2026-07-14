using System.Web.Script.Serialization;
using DeployTool.Core.Models;
using DeployTool.Core.Polyfills;

namespace DeployTool.Core.Services;

/// <summary>Reads/writes Config\installers.json — the shared metadata for all installers, used by all PCs.</summary>
public sealed class InstallerMetadataStore(ShareLayout layout)
{
    private static readonly JavaScriptSerializer Serializer = new();

    public async Task<List<InstallerDefinition>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(layout.InstallersJsonPath)) return [];
        var json = await Task.Run(() => File.ReadAllText(layout.InstallersJsonPath), ct);
        return Serializer.Deserialize<List<InstallerDefinition>>(json) ?? [];
    }

    public async Task SaveAsync(List<InstallerDefinition> definitions, CancellationToken ct = default)
    {
        Directory.CreateDirectory(layout.ConfigDir);
        var json = JsonIndenter.Indent(Serializer.Serialize(definitions));
        await Task.Run(() => File.WriteAllText(layout.InstallersJsonPath, json), ct);
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
