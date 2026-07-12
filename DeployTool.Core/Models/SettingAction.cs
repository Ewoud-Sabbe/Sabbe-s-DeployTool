namespace DeployTool.Core.Models;

/// <summary>A hardcoded Windows-setting action. Add new entries in SettingsCatalogService — no JSON involved.</summary>
public sealed class SettingAction
{
    public required string Name { get; init; }
    public bool DefaultSelected { get; init; }
    public required Func<CancellationToken, Task> Execute { get; init; }
}
