using System.Windows;

namespace DeployTool;

public partial class App : Application
{
    // Production default. Override for local testing with DEPLOYTOOL_SHARE_ROOT
    // (or a first command-line argument), e.g. "S:\deploy map".
    private const string DefaultShareRoot = @"\\fileserver\PCSetup";

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
