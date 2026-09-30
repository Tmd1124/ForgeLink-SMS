using Moq;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public async Task RefreshCommand_reads_current_default_app_status()
    {
        var role = new Mock<IDefaultAppRoleService>();
        role.Setup(r => r.IsDefaultSmsApp()).Returns(true);
        var viewModel = new SettingsViewModel(role.Object);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsDefaultSmsApp);
    }
}
