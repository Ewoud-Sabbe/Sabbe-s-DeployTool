using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeployTool.Core.Models;
using DeployTool.Core.Services;
using DeployTool.Views;

namespace DeployTool.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ShareLayout _layout;
    private readonly FileServerConnector _fileServerConnector = new();
    private readonly InstallerMetadataStore _metadataStore;
    private readonly InstallerCatalogService _installerCatalog;
    private readonly ShortcutCatalogService _shortcutCatalog;
    private readonly SettingsCatalogService _settingsCatalog = new();
    private readonly ShortcutPlacementService _shortcutPlacer = new();

    private SessionLogger? _logger;
    private InstallEngine? _engine;

    public ObservableCollection<SessionItemViewModel> Installers { get; } = [];
    public ObservableCollection<SessionItemViewModel> Shortcuts { get; } = [];
    public ObservableCollection<SessionItemViewModel> Settings { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool isLoading = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool isRunning;

    [ObservableProperty]
    private string statusMessage = "Verbinden met fileserver...";

    public MainViewModel(string shareRoot)
    {
        _layout = new ShareLayout(shareRoot);
        _metadataStore = new InstallerMetadataStore(_layout);
        _installerCatalog = new InstallerCatalogService(_layout, _metadataStore);
        _shortcutCatalog = new ShortcutCatalogService(_layout);
    }

    private IEnumerable<SessionItemViewModel> AllItems => Installers.Concat(Shortcuts).Concat(Settings);

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            StatusMessage = "Verbinden met fileserver...";
            await Task.Run(() => _fileServerConnector.Connect(_layout.Root));

            StatusMessage = "Software, snelkoppelingen en instellingen laden...";
            var installerEntries = await _installerCatalog.DiscoverAsync();
            var shortcutEntries = await _shortcutCatalog.DiscoverAsync();
            var settingActions = _settingsCatalog.GetAll();

            Installers.Clear();
            foreach (var entry in installerEntries)
            {
                var item = new SessionItem
                {
                    Kind = SessionItemKind.Installer,
                    Name = entry.DisplayName,
                    IsSelected = entry.IsConfigured && entry.DefaultSelected,
                    Installer = entry
                };
                Installers.Add(new SessionItemViewModel(item, entry.IsConfigured));
            }

            Shortcuts.Clear();
            foreach (var entry in shortcutEntries)
            {
                var item = new SessionItem
                {
                    Kind = SessionItemKind.Shortcut,
                    Name = entry.DisplayName,
                    IsSelected = false,
                    Shortcut = entry
                };
                Shortcuts.Add(new SessionItemViewModel(item));
            }

            Settings.Clear();
            foreach (var action in settingActions)
            {
                var item = new SessionItem
                {
                    Kind = SessionItemKind.Setting,
                    Name = action.Name,
                    IsSelected = action.DefaultSelected,
                    Setting = action
                };
                Settings.Add(new SessionItemViewModel(item));
            }

            StatusMessage = "Klaar om te starten.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fout bij laden: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        StatusMessage = "Bezig...";
        try
        {
            _logger ??= new SessionLogger(_layout);
            _engine ??= new InstallEngine(_shortcutPlacer, _logger);

            var selected = AllItems.Where(i => i.IsSelected && i.IsConfigured).Select(vm => vm.Model).ToList();
            var progress = new Progress<SessionItemProgress>(OnProgress);

            await _engine.RunAsync(selected, progress);
            StatusMessage = "Sessie voltooid.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fout tijdens sessie: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanStart() => !IsLoading && !IsRunning;

    [RelayCommand]
    private async Task RetryAsync(SessionItemViewModel? item)
    {
        if (item is null) return;

        _logger ??= new SessionLogger(_layout);
        _engine ??= new InstallEngine(_shortcutPlacer, _logger);

        var progress = new Progress<SessionItemProgress>(OnProgress);
        await _engine.RetryAsync(item.Model, progress);
    }

    [RelayCommand]
    private async Task ConfigureAsync(SessionItemViewModel? item)
    {
        if (item?.Model.Installer is not { } installer) return;

        var dialogViewModel = new InstallerMetadataDialogViewModel(installer.FileName)
        {
            DisplayName = installer.IsConfigured ? installer.DisplayName : installer.FileName,
            SilentArgs = installer.SilentArgs,
            Category = installer.Category,
            DefaultSelected = installer.DefaultSelected
        };

        var dialog = new InstallerMetadataDialog(dialogViewModel) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        await _metadataStore.UpsertAsync(dialogViewModel.ToDefinition());
        await LoadAsync();
    }

    private void OnProgress(SessionItemProgress progress)
    {
        var vm = AllItems.FirstOrDefault(i => i.Model == progress.Item);
        vm?.ApplyProgress(progress.Status, progress.ErrorMessage);
    }
}
