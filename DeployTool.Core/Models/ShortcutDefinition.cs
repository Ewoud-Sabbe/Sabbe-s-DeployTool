namespace DeployTool.Core.Models;

/// <summary>Entry in Shortcuts\shortcuts.json. Target starting with http(s):// becomes a .url shortcut, otherwise a .lnk to a path.</summary>
public sealed class ShortcutDefinition
{
    public required string Name { get; init; }
    public required string Target { get; init; }
    public bool DefaultSelected { get; init; }
}
