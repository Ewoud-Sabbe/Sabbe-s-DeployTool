using System.Windows;
using DeployTool.ViewModels;

namespace DeployTool;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(string shareRoot)
    {
        InitializeComponent();
        _viewModel = new MainViewModel(shareRoot);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }
}
