using System.Text.Json;
using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Reads Shortcuts\shortcuts.json.</summary>
public sealed class ShortcutStore(ShareLayout layout)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<ShortcutDefinition>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(layout.ShortcutsJsonPath)) return [];
        await using var stream = File.OpenRead(layout.ShortcutsJsonPath);
        var items = await JsonSerializer.DeserializeAsync<List<ShortcutDefinition>>(stream, Options, ct);
        return items ?? [];
    }
}
