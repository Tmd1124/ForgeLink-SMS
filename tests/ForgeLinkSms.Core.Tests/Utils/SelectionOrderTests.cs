using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class SelectionOrderTests
{
    private static SmsThread[] Threads(params long[] ids) => ids.Select(id => new SmsThread
    {
        Id = id,
        Address = $"555{id}",
        DisplayName = $"Person {id}",
        LastMessageBody = "hi",
        UnreadCount = 0,
        LastMessageTimestamp = DateTimeOffset.UtcNow
    }).ToArray();

    [Fact]
    public void Long_pressed_chat_moves_to_the_top_and_the_rest_keep_their_order()
    {
        var ordered = SelectionOrder.PinFirst(Threads(1, 2, 3, 4), pinnedId: 3);

        Assert.Equal(new long[] { 3, 1, 2, 4 }, ordered.Select(t => t.Id));
    }

    [Fact]
    public void Already_first_stays_first()
    {
        Assert.Equal(new long[] { 1, 2, 3 }, SelectionOrder.PinFirst(Threads(1, 2, 3), pinnedId: 1).Select(t => t.Id));
    }

    [Fact]
    public void No_pinned_chat_or_one_not_in_the_list_leaves_the_order_alone()
    {
        Assert.Equal(new long[] { 1, 2, 3 }, SelectionOrder.PinFirst(Threads(1, 2, 3), pinnedId: null).Select(t => t.Id));
        Assert.Equal(new long[] { 1, 2, 3 }, SelectionOrder.PinFirst(Threads(1, 2, 3), pinnedId: 9).Select(t => t.Id));
    }
}
