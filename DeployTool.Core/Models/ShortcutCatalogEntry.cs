namespace DeployTool.Core.Models;

/// <summary>A .url or .lnk file found in Shortcuts\ on the share — dropped in, no config needed.</summary>
public sealed class ShortcutCatalogEntry
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required string DisplayName { get; init; }
}
