using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class BackSwipeTests
{
    [Fact]
    public void A_quick_swipe_to_the_right_goes_back() =>
        Assert.True(BackSwipe.IsBackSwipe(dx: 140, dy: 12, elapsedMs: 250));

    [Fact]
    public void A_swipe_to_the_left_does_not_go_back() =>
        Assert.False(BackSwipe.IsBackSwipe(dx: -140, dy: 0, elapsedMs: 250));

    [Fact]
    public void A_short_nudge_does_not_go_back() =>
        Assert.False(BackSwipe.IsBackSwipe(dx: 40, dy: 0, elapsedMs: 150));

    [Fact]
    public void A_mostly_vertical_scroll_does_not_go_back() =>
        Assert.False(BackSwipe.IsBackSwipe(dx: 100, dy: 90, elapsedMs: 250));

    [Fact]
    public void A_slow_drag_does_not_go_back() =>
        Assert.False(BackSwipe.IsBackSwipe(dx: 200, dy: 5, elapsedMs: 1500));
}
