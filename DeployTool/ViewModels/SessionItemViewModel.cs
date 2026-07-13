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
    public bool CanToggleDefault => Kind is SessionItemKind.Shortcut or SessionItemKind.Setting;
    public string ConfigureButtonLabel => IsConfigured ? "Bewerken..." : "Configureren...";

    /// <summary>Stable key into ItemDefaultsStore, e.g. "shortcut:foo.url" / "setting:Bestandsextensies tonen".</summary>
    public string DefaultsKey => Kind switch
    {
        SessionItemKind.Shortcut => $"shortcut:{Model.Shortcut!.FileName}",
        SessionItemKind.Setting => $"setting:{Name}",
        _ => $"installer:{Model.Installer?.FileName}"
    };

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isDefault;

    [ObservableProperty]
    private ItemStatus status;

    [ObservableProperty]
    private string? errorMessage;

    public SessionItemViewModel(SessionItem model, bool isConfigured = true, bool isDefault = false)
    {
        Model = model;
        IsConfigured = isConfigured;
        isSelected = model.IsSelected;
        this.isDefault = isDefault;
        status = model.Status;
    }

    partial void OnIsSelectedChanged(bool value) => Model.IsSelected = value;

    public void ApplyProgress(ItemStatus newStatus, string? error)
    {
        Status = newStatus;
        ErrorMessage = error;
    }
}
