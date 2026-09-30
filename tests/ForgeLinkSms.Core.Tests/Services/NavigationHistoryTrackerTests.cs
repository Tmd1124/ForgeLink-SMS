using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class NavigationHistoryTrackerTests
{
    [Fact]
    public void TryGoBack_returns_false_when_only_the_root_has_been_recorded()
    {
        var tracker = new NavigationHistoryTracker();
        tracker.RecordNavigation("conversations");

        Assert.False(tracker.TryGoBack());
    }

    [Fact]
    public void TryGoBack_navigates_to_the_previous_screen()
    {
        var tracker = new NavigationHistoryTracker();
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=18");
        string? navigatedTo = null;
        tracker.NavigateAction = path => navigatedTo = path;

        var result = tracker.TryGoBack();

        Assert.True(result);
        Assert.Equal("conversations", navigatedTo);
    }

    [Fact]
    public void TryGoBack_pops_one_screen_at_a_time()
    {
        var tracker = new NavigationHistoryTracker();
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=18");
        tracker.RecordNavigation("compose");
        var visited = new List<string>();
        tracker.NavigateAction = path => visited.Add(path);

        tracker.TryGoBack();
        tracker.TryGoBack();

        Assert.Equal(["conversations/thread?id=18", "conversations"], visited);
    }

    [Fact]
    public void RecordNavigation_resets_the_stack_when_returning_to_the_root()
    {
        var tracker = new NavigationHistoryTracker();
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=18");

        tracker.RecordNavigation("conversations");

        Assert.False(tracker.TryGoBack());
    }

    [Fact]
    public void RecordNavigation_ignores_the_splash_route_before_the_root_is_reached()
    {
        var tracker = new NavigationHistoryTracker();

        tracker.RecordNavigation("");

        Assert.False(tracker.TryGoBack());
    }

    [Fact]
    public void RecordNavigation_ignores_the_splash_route_even_after_the_root_was_already_recorded()
    {
        // Regression: a MAUI Activity can be recreated (e.g. after the app was backgrounded and the
        // process was kept alive) while this singleton's stack survives, so the fresh Blazor circuit's
        // initial "/" render must not corrupt an already-populated stack.
        var tracker = new NavigationHistoryTracker();
        tracker.RecordNavigation("conversations");

        tracker.RecordNavigation("");

        Assert.False(tracker.TryGoBack());
    }

    [Fact]
    public void TryConsumeLocalBack_returns_false_when_no_handler_is_set()
    {
        var tracker = new NavigationHistoryTracker();

        Assert.False(tracker.TryConsumeLocalBack());
    }

    [Fact]
    public void TryConsumeLocalBack_returns_the_handlers_result()
    {
        var tracker = new NavigationHistoryTracker();
        tracker.LocalBackHandler = () => true;

        Assert.True(tracker.TryConsumeLocalBack());
    }

    [Fact]
    public void RecordNavigation_does_not_duplicate_when_navigating_to_the_current_screen_again()
    {
        var tracker = new NavigationHistoryTracker();
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=18");
        tracker.RecordNavigation("conversations/thread?id=18");
        var visited = new List<string>();
        tracker.NavigateAction = path => visited.Add(path);

        var firstBack = tracker.TryGoBack();
        var secondBack = tracker.TryGoBack();

        Assert.True(firstBack);
        Assert.False(secondBack);
        Assert.Equal(["conversations"], visited);
    }

    [Fact]
    public void Back_asks_the_side_pane_only_after_the_page_had_nothing_to_close()
    {
        var tracker = new NavigationHistoryTracker();
        var paneAsked = 0;
        tracker.PaneBackHandler = () => { paneAsked++; return true; };

        tracker.LocalBackHandler = () => true;
        Assert.True(tracker.TryConsumeLocalBack());
        Assert.Equal(0, paneAsked);

        tracker.LocalBackHandler = () => false;
        Assert.True(tracker.TryConsumeLocalBack());
        Assert.Equal(1, paneAsked);

        tracker.LocalBackHandler = null;
        tracker.PaneBackHandler = () => false;
        Assert.False(tracker.TryConsumeLocalBack());
    }

    [Fact]
    public void Chat_switches_replace_each_other_when_collapsing_is_on()
    {
        var tracker = new NavigationHistoryTracker { CollapseChatSwitches = true };
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=1");
        tracker.RecordNavigation("conversations/thread?id=2");
        string? navigatedTo = null;
        tracker.NavigateAction = path => navigatedTo = path;

        Assert.True(tracker.TryGoBack());
        Assert.Equal("conversations", navigatedTo);
    }

    [Fact]
    public void Chat_switches_stack_up_when_collapsing_is_off()
    {
        var tracker = new NavigationHistoryTracker();
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=1");
        tracker.RecordNavigation("conversations/thread?id=2");

        Assert.Equal(3, tracker.Snapshot().Count);
    }

    [Fact]
    public void Collapsing_leaves_other_pages_on_the_stack()
    {
        var tracker = new NavigationHistoryTracker { CollapseChatSwitches = true };
        tracker.RecordNavigation("conversations");
        tracker.RecordNavigation("conversations/thread?id=1");
        tracker.RecordNavigation("conversations/media?id=1");
        tracker.RecordNavigation("conversations/thread?id=1");

        Assert.Equal(4, tracker.Snapshot().Count);
    }
}
