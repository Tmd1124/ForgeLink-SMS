using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class ConversationListRefresherTests
{
    [Fact]
    public void Requesting_a_refresh_notifies_every_listener()
    {
        var refresher = new ConversationListRefresher();
        var calls = 0;
        refresher.RefreshRequested += () => calls++;
        refresher.RefreshRequested += () => calls++;

        refresher.RequestRefresh();

        Assert.Equal(2, calls);
    }
}
