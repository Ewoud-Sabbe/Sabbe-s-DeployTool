using System.Windows;
using DeployTool.ViewModels;

namespace DeployTool.Views;

public partial class InstallerMetadataDialog : Window
{
    public InstallerMetadataDialogViewModel ViewModel { get; }

    public InstallerMetadataDialog(InstallerMetadataDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
