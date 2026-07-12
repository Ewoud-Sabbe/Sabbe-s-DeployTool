namespace DeployTool.Core.Models;

/// <summary>An installer file found on the share, merged with its metadata (if configured).</summary>
public sealed class InstallerCatalogEntry
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public bool IsConfigured { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string SilentArgs { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool DefaultSelected { get; init; }
}
