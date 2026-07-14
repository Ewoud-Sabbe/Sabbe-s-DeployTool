using System.Text.Json;

namespace DeployTool.Core.Services;

/// <summary>
/// Reads/writes Config\item-defaults.json — shared "standaard geselecteerd" overrides for
/// shortcuts and settings, which (unlike installers) have no other metadata file of their own.
/// Keyed by "shortcut:{FileName}" / "setting:{Name}".
/// </summary>
public sealed class ItemDefaultsStore(ShareLayout layout)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<Dictionary<string, bool>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(layout.ItemDefaultsJsonPath)) return [];
        using var stream = File.OpenRead(layout.ItemDefaultsJsonPath);
        var defaults = await JsonSerializer.DeserializeAsync<Dictionary<string, bool>>(stream, Options, ct);
        return defaults ?? [];
    }

    public async Task SetAsync(string key, bool isDefault, CancellationToken ct = default)
    {
        var defaults = await LoadAsync(ct);
        defaults[key] = isDefault;

        Directory.CreateDirectory(layout.ConfigDir);
        using var stream = File.Create(layout.ItemDefaultsJsonPath);
        await JsonSerializer.SerializeAsync(stream, defaults, Options, ct);
    }
}
