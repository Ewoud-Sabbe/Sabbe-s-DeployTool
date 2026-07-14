using System.Web.Script.Serialization;
using DeployTool.Core.Polyfills;

namespace DeployTool.Core.Services;

/// <summary>
/// Reads/writes Config\item-defaults.json — shared "standaard geselecteerd" overrides for
/// shortcuts and settings, which (unlike installers) have no other metadata file of their own.
/// Keyed by "shortcut:{FileName}" / "setting:{Name}".
/// </summary>
public sealed class ItemDefaultsStore(ShareLayout layout)
{
    private static readonly JavaScriptSerializer Serializer = new();

    public async Task<Dictionary<string, bool>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(layout.ItemDefaultsJsonPath)) return [];
        var json = await Task.Run(() => File.ReadAllText(layout.ItemDefaultsJsonPath), ct);
        return Serializer.Deserialize<Dictionary<string, bool>>(json) ?? [];
    }

    public async Task SetAsync(string key, bool isDefault, CancellationToken ct = default)
    {
        var defaults = await LoadAsync(ct);
        defaults[key] = isDefault;

        Directory.CreateDirectory(layout.ConfigDir);
        var json = JsonIndenter.Indent(Serializer.Serialize(defaults));
        await Task.Run(() => File.WriteAllText(layout.ItemDefaultsJsonPath, json), ct);
    }
}
