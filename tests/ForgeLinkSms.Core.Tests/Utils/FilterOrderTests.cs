using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class FilterOrderTests
{
    // All filters are 1..5; only 1, 3 and 5 have chats, so only they show as tabs.
    private static readonly long[] All = { 1, 2, 3, 4, 5 };
    private static readonly long[] Tabs = { 1, 3, 5 };

    [Fact]
    public void Moving_a_tab_to_the_front_puts_it_before_the_first_tab()
    {
        Assert.Equal(new long[] { 5, 1, 2, 3, 4 }, FilterOrder.MoveTab(All, Tabs, movedId: 5, toTabIndex: 0));
    }

    [Fact]
    public void Moving_a_tab_to_the_end_puts_it_after_the_last_tab()
    {
        Assert.Equal(new long[] { 2, 3, 4, 5, 1 }, FilterOrder.MoveTab(All, Tabs, movedId: 1, toTabIndex: 2));
    }

    [Fact]
    public void Moving_a_tab_to_the_middle_puts_it_just_before_the_tab_it_now_precedes()
    {
        Assert.Equal(new long[] { 1, 2, 5, 3, 4 }, FilterOrder.MoveTab(All, Tabs, movedId: 5, toTabIndex: 1));
    }

    [Fact]
    public void Moving_a_tab_onto_its_own_place_changes_nothing()
    {
        Assert.Equal(All, FilterOrder.MoveTab(All, Tabs, movedId: 3, toTabIndex: 1));
    }

    [Fact]
    public void Out_of_range_targets_are_clamped()
    {
        Assert.Equal(new long[] { 2, 3, 4, 5, 1 }, FilterOrder.MoveTab(All, Tabs, movedId: 1, toTabIndex: 99));
    }
}
