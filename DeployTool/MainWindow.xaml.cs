using System.Collections.Specialized;
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
        _viewModel.LogLines.CollectionChanged += LogLines_CollectionChanged;
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    private void LogLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (LogListBox.Items.Count > 0)
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
    }
}
