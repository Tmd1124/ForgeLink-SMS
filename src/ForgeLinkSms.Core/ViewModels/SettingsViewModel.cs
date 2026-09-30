using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDefaultAppRoleService _roleService;

    [ObservableProperty]
    private bool _isDefaultSmsApp;

    public SettingsViewModel(IDefaultAppRoleService roleService)
    {
        _roleService = roleService;
    }

    [RelayCommand]
    private Task Refresh()
    {
        IsDefaultSmsApp = _roleService.IsDefaultSmsApp();
        return Task.CompletedTask;
    }
}
