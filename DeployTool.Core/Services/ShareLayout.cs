namespace DeployTool.Core.Services;

/// <summary>Folder layout on the fileserver share (\\fileserver\PCSetup or a local test root).</summary>
public sealed class ShareLayout
{
    public string Root { get; }

    public ShareLayout(string root) => Root = root;

    public string AppDir => Path.Combine(Root, "App");
    public string InstallersDir => Path.Combine(Root, "Installers");
    public string ShortcutsDir => Path.Combine(Root, "Shortcuts");
    public string ConfigDir => Path.Combine(Root, "Config");
    public string LogsDir => Path.Combine(Root, "Logs");

    public string InstallersJsonPath => Path.Combine(ConfigDir, "installers.json");
    public string ShortcutsJsonPath => Path.Combine(ShortcutsDir, "shortcuts.json");
}
