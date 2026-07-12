using CommunityToolkit.Mvvm.ComponentModel;
using DeployTool.Core.Models;

namespace DeployTool.ViewModels;

public partial class SessionItemViewModel : ObservableObject
{
    public SessionItem Model { get; }

    /// <summary>False for installers found on the share that have no metadata yet — shown but not selectable.</summary>
    public bool IsConfigured { get; }

    public SessionItemKind Kind => Model.Kind;
    public string Name => Model.Name;
    public bool IsInstaller => Kind == SessionItemKind.Installer;
    public string ConfigureButtonLabel => IsConfigured ? "Bewerken..." : "Configureren...";

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private ItemStatus status;

    [ObservableProperty]
    private string? errorMessage;

    public SessionItemViewModel(SessionItem model, bool isConfigured = true)
    {
        Model = model;
        IsConfigured = isConfigured;
        isSelected = model.IsSelected;
        status = model.Status;
    }

    partial void OnIsSelectedChanged(bool value) => Model.IsSelected = value;

    public void ApplyProgress(ItemStatus newStatus, string? error)
    {
        Status = newStatus;
        ErrorMessage = error;
    }
}
