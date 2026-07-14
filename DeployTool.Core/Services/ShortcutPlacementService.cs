using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Copies a .url/.lnk/.exe shortcut file as-is onto the current user's desktop.</summary>
public sealed class ShortcutPlacementService
{
    public void Place(ShortcutCatalogEntry entry)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var destination = Path.Combine(desktop, entry.FileName);

        if (Path.GetExtension(entry.FileName).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            PlaceUrlWithLocalIcon(entry.FullPath, destination);
            return;
        }

        File.Copy(entry.FullPath, destination, overwrite: true);
    }

    /// <summary>
    /// The app runs elevated (UAC), so its authenticated connection to the fileserver share is
    /// only visible within that elevated logon session. Explorer — which draws desktop icons —
    /// runs unelevated and can't see it, so a .url pointing IconFile at a share path silently
    /// renders as a blank/default icon. Copying the icon locally once and rewriting IconFile to
    /// point there sidesteps that entirely.
    /// </summary>
    private static void PlaceUrlWithLocalIcon(string sourcePath, string destination)
    {
        var lines = File.ReadAllLines(sourcePath);
        var iconLineIndex = Array.FindIndex(lines, l => l.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase));

        if (iconLineIndex < 0)
        {
            File.Copy(sourcePath, destination, overwrite: true);
            return;
        }

        var iconSourcePath = lines[iconLineIndex]["IconFile=".Length..].Trim();
        if (File.Exists(iconSourcePath))
        {
            var localIconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeployTool", "Icons");
            Directory.CreateDirectory(localIconDir);
            var localIconPath = Path.Combine(localIconDir, Path.GetFileName(iconSourcePath));
            File.Copy(iconSourcePath, localIconPath, overwrite: true);
            lines[iconLineIndex] = "IconFile=" + localIconPath;
        }

        File.WriteAllLines(destination, lines);
    }
}
