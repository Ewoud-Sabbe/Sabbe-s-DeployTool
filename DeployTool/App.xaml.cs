using System.Windows;

namespace DeployTool;

public partial class App : Application
{
    // Production default. Override with DEPLOYTOOL_SHARE_ROOT (or a first command-line argument)
    // for local testing, e.g. "S:\deploy map".
    private const string DefaultShareRoot = @"\\jdstore\Installatie\# VOORINSTALLATIE\deploy map";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var shareRoot = Environment.GetEnvironmentVariable("DEPLOYTOOL_SHARE_ROOT")
                         ?? e.Args.FirstOrDefault()
                         ?? DefaultShareRoot;

        var window = new MainWindow(shareRoot);
        window.Show();
    }
}
