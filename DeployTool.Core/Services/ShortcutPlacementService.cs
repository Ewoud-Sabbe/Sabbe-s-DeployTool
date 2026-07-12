using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Copies a .url/.lnk shortcut file as-is onto the current user's desktop.</summary>
public sealed class ShortcutPlacementService
{
    public void Place(ShortcutCatalogEntry entry)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var destination = Path.Combine(desktop, entry.FileName);
        File.Copy(entry.FullPath, destination, overwrite: true);
    }
}
