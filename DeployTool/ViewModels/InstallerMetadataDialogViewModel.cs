using CommunityToolkit.Mvvm.ComponentModel;
using DeployTool.Core.Models;

namespace DeployTool.ViewModels;

public partial class InstallerMetadataDialogViewModel : ObservableObject
{
    public string FileName { get; }

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private string silentArgs = string.Empty;

    [ObservableProperty]
    private string category = string.Empty;

    [ObservableProperty]
    private bool defaultSelected;

    public InstallerMetadataDialogViewModel(string fileName)
    {
        FileName = fileName;
        displayName = fileName;
    }

    public InstallerDefinition ToDefinition() => new()
    {
        FileName = FileName,
        DisplayName = DisplayName.Trim() is { Length: > 0 } n ? n : FileName,
        SilentArgs = SilentArgs.Trim(),
        Category = Category.Trim(),
        DefaultSelected = DefaultSelected
    };
}
