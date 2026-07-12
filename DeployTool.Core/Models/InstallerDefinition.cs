namespace DeployTool.Core.Models;

/// <summary>Metadata stored in Config\installers.json on the fileserver, keyed by FileName.</summary>
public sealed class InstallerDefinition
{
    public required string FileName { get; init; }
    public string DisplayName { get; set; } = string.Empty;
    public string SilentArgs { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool DefaultSelected { get; set; }
}
