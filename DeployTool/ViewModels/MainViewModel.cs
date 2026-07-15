using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeployTool.Core.Models;
using DeployTool.Core.Polyfills;
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
    private readonly SettingsCatalogService _settingsCatalog;
    private readonly ShortcutPlacementService _shortcutPlacer = new();
    private readonly ItemDefaultsStore _itemDefaultsStore;

    private SessionLogger? _logger;
    private InstallEngine? _engine;

    public ObservableCollection<SessionItemViewModel> Installers { get; } = [];
    public ObservableCollection<SessionItemViewModel> Shortcuts { get; } = [];
    public ObservableCollection<SessionItemViewModel> Settings { get; } = [];

    /// <summary>Mirrors the session log file, line for line, as it's written.</summary>
    public ObservableCollection<string> LogLines { get; } = [];

    public string MachineName => Environment.MachineName;

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
        _settingsCatalog = new SettingsCatalogService(_layout);
        _itemDefaultsStore = new ItemDefaultsStore(_layout);
    }

    private IEnumerable<SessionItemViewModel> AllItems => Installers.Concat(Shortcuts).Concat(Settings);

    public async Task LoadAsync()
    {
        IsLoading = true;
        var previousSelections = AllItems.ToDictionary(GetStableKey, i => i.IsSelected);
        try
        {
            StatusMessage = "Verbinden met fileserver...";
            await Task.Run(() => _fileServerConnector.Connect(_layout.Root));

            StatusMessage = "Software, snelkoppelingen en instellingen laden...";
            var installerEntries = await _installerCatalog.DiscoverAsync();
            var shortcutEntries = await _shortcutCatalog.DiscoverAsync();
            var settingActions = _settingsCatalog.GetAll();
            var itemDefaults = await _itemDefaultsStore.LoadAsync();

            Installers.Clear();
            foreach (var entry in installerEntries)
            {
                var key = $"installer:{entry.FileName}";
                var isSelected = previousSelections.TryGetValue(key, out var wasSelected)
                    ? wasSelected
                    : entry.IsConfigured && entry.DefaultSelected;

                var item = new SessionItem
                {
                    Kind = SessionItemKind.Installer,
                    Name = entry.DisplayName,
                    IsSelected = isSelected,
                    Installer = entry
                };
                Installers.Add(new SessionItemViewModel(item, entry.IsConfigured, entry.DefaultSelected));
            }

            Shortcuts.Clear();
            foreach (var entry in shortcutEntries)
            {
                var key = $"shortcut:{entry.FileName}";
                var isDefault = itemDefaults.GetValueOrDefault(key, false);
                var isSelected = previousSelections.TryGetValue(key, out var wasSelected) ? wasSelected : isDefault;

                var item = new SessionItem
                {
                    Kind = SessionItemKind.Shortcut,
                    Name = entry.DisplayName,
                    IsSelected = isSelected,
                    Shortcut = entry
                };
                Shortcuts.Add(new SessionItemViewModel(item, isDefault: isDefault));
            }

            Settings.Clear();
            foreach (var action in settingActions)
            {
                var key = $"setting:{action.Name}";
                var isDefault = itemDefaults.GetValueOrDefault(key, action.DefaultSelected);
                var isSelected = previousSelections.TryGetValue(key, out var wasSelected) ? wasSelected : isDefault;

                var item = new SessionItem
                {
                    Kind = SessionItemKind.Setting,
                    Name = action.Name,
                    IsSelected = isSelected,
                    Setting = action
                };
                Settings.Add(new SessionItemViewModel(item, isDefault: isDefault));
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

    private static string GetStableKey(SessionItemViewModel vm) => vm.Kind switch
    {
        SessionItemKind.Installer => $"installer:{vm.Model.Installer!.FileName}",
        SessionItemKind.Shortcut => $"shortcut:{vm.Model.Shortcut!.FileName}",
        SessionItemKind.Setting => $"setting:{vm.Name}",
        _ => vm.Name
    };

    private SessionLogger EnsureLogger()
    {
        if (_logger is null)
        {
            _logger = new SessionLogger(_layout);
            _logger.LineWritten += line => LogLines.Add(line);
        }
        return _logger;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        StatusMessage = "Bezig...";
        try
        {
            var logger = EnsureLogger();
            _engine ??= new InstallEngine(_shortcutPlacer, logger);

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

        var logger = EnsureLogger();
        _engine ??= new InstallEngine(_shortcutPlacer, logger);

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

    /// <summary>Flips whether a shortcut/setting is pre-checked on future loads. Takes effect next time, not retroactively.</summary>
    [RelayCommand]
    private async Task ToggleDefaultAsync(SessionItemViewModel? item)
    {
        if (item is null || !item.CanToggleDefault) return;

        item.IsDefault = !item.IsDefault;
        await _itemDefaultsStore.SetAsync(item.DefaultsKey, item.IsDefault);
    }

    private void OnProgress(SessionItemProgress progress)
    {
        var vm = AllItems.FirstOrDefault(i => i.Model == progress.Item);
        vm?.ApplyProgress(progress.Status, progress.ErrorMessage);
    }
}
