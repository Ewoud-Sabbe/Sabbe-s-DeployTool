namespace DeployTool.Core.Models;

/// <summary>Runtime unit the InstallEngine executes: one selected installer, shortcut, or setting.</summary>
public sealed class SessionItem
{
    public required SessionItemKind Kind { get; init; }
    public required string Name { get; init; }
    public bool IsSelected { get; set; }

    public InstallerCatalogEntry? Installer { get; init; }
    public ShortcutCatalogEntry? Shortcut { get; init; }
    public SettingAction? Setting { get; init; }

    public ItemStatus Status { get; internal set; } = ItemStatus.Pending;
    public string? ErrorMessage { get; internal set; }
}
