using DeployTool.Core.Models;
using DeployTool.Core.Services;

var root = args.Length > 0 ? args[0] : @"S:\deploy map";
Console.WriteLine($"Fileserver root: {root}");

var layout = new ShareLayout(root);
new FileServerConnector().Connect(root);
Console.WriteLine("Fileserver connectie OK.");

var metadataStore = new InstallerMetadataStore(layout);
var installerCatalog = new InstallerCatalogService(layout, metadataStore);
var shortcutCatalog = new ShortcutCatalogService(layout);
var settingsCatalog = new SettingsCatalogService(layout);

var installers = await installerCatalog.DiscoverAsync();
Console.WriteLine($"\nGevonden installers ({installers.Count}):");
foreach (var i in installers)
    Console.WriteLine($"  [{(i.IsConfigured ? "OK" : "NOG TE CONFIGUREREN")}] {i.FileName} -> \"{i.DisplayName}\" args=\"{i.SilentArgs}\" default={i.DefaultSelected}");

var shortcuts = await shortcutCatalog.DiscoverAsync();
Console.WriteLine($"\nSnelkoppelingen ({shortcuts.Count}):");
foreach (var s in shortcuts)
    Console.WriteLine($"  {s.FileName} -> \"{s.DisplayName}\"");

var settings = settingsCatalog.GetAll();
Console.WriteLine($"\nInstellingen ({settings.Count}):");
foreach (var s in settings)
    Console.WriteLine($"  {s.Name} default={s.DefaultSelected}");

// Build a session: only configured installers + all shortcuts + all settings, all selected.
var sessionItems = new List<SessionItem>();
sessionItems.AddRange(installers.Where(i => i.IsConfigured).Select(i => new SessionItem
{
    Kind = SessionItemKind.Installer,
    Name = i.DisplayName,
    IsSelected = true,
    Installer = i
}));
sessionItems.AddRange(shortcuts.Select(s => new SessionItem
{
    Kind = SessionItemKind.Shortcut,
    Name = s.DisplayName,
    IsSelected = true,
    Shortcut = s
}));
sessionItems.AddRange(settings.Select(s => new SessionItem
{
    Kind = SessionItemKind.Setting,
    Name = s.Name,
    IsSelected = true,
    Setting = s
}));

Console.WriteLine($"\nSessie starten met {sessionItems.Count} items...\n");

using var logger = new SessionLogger(layout);
var engine = new InstallEngine(new ShortcutPlacementService(), logger);

var progress = new Progress<SessionItemProgress>(p =>
    Console.WriteLine($"  [{p.Status,-9}] {p.Item.Kind,-9} {p.Item.Name}" + (p.ErrorMessage is null ? "" : $" -> {p.ErrorMessage}")));

await engine.RunAsync(sessionItems, progress);

Console.WriteLine("\nResultaat:");
foreach (var item in sessionItems)
    Console.WriteLine($"  {item.Status,-9} {item.Kind,-9} {item.Name}" + (item.ErrorMessage is null ? "" : $" ({item.ErrorMessage})"));

Console.WriteLine($"\nLog geschreven naar: {logger.LogPath}");

var failed = sessionItems.FirstOrDefault(i => i.Status == ItemStatus.Failed && i.Kind == SessionItemKind.Installer);
if (failed is not null)
{
    Console.WriteLine($"\nRetry-test voor mislukt item '{failed.Name}'...");
    await engine.RetryAsync(failed, progress);
    Console.WriteLine($"  Na retry: {failed.Status}" + (failed.ErrorMessage is null ? "" : $" ({failed.ErrorMessage})"));
}

Console.WriteLine("\nKlaar.");
