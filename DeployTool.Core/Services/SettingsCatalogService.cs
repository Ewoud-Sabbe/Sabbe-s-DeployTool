using DeployTool.Core.Models;
using Microsoft.Win32;

namespace DeployTool.Core.Services;

/// <summary>Hardcoded, extensible list of Windows-setting actions. Add new entries here.</summary>
public sealed class SettingsCatalogService
{
    public List<SettingAction> GetAll() =>
    [
        new SettingAction
        {
            Name = "Standaardprinter-popup uitschakelen",
            DefaultSelected = true,
            Execute = (_) =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows");
                key.SetValue("LegacyDefaultPrinterMode", 1, RegistryValueKind.DWord);
                return Task.CompletedTask;
            }
        },
        new SettingAction
        {
            Name = "Bestandsextensies tonen in Verkenner",
            DefaultSelected = true,
            Execute = (_) =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                key.SetValue("HideFileExt", 0, RegistryValueKind.DWord);
                return Task.CompletedTask;
            }
        },
        new SettingAction
        {
            Name = "\"Deze pc\" tonen op bureaublad",
            DefaultSelected = true,
            Execute = (_) =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
                key.SetValue("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0, RegistryValueKind.DWord);
                return Task.CompletedTask;
            }
        },
    ];
}
