using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class SearchFiltersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static SmsMessage Msg(long id, string body, int daysAgo = 0, long threadId = 1, AttachmentKind? attachment = null) => new()
    {
        Id = id,
        ThreadId = threadId,
        Address = "555",
        Body = body,
        Timestamp = Now.AddDays(-daysAgo),
        IsOutgoing = false,
        Status = SmsMessageStatus.Delivered,
        Attachments = attachment is { } kind ? new[] { new MessageAttachment { FileName = "f", Kind = kind, PartId = 1 } } : Array.Empty<MessageAttachment>()
    };

    private static long[] Ids(IEnumerable<SmsMessage> messages) => messages.Select(m => m.Id).ToArray();

    [Fact]
    public void Photos_keeps_messages_with_a_picture_or_video()
    {
        var messages = new[] { Msg(1, "text"), Msg(2, "", attachment: AttachmentKind.Image), Msg(3, "", attachment: AttachmentKind.Video),
            Msg(4, "", attachment: AttachmentKind.Gif), Msg(5, "", attachment: AttachmentKind.Audio) };

        Assert.Equal(new long[] { 2, 3, 4 }, Ids(SearchFilters.Apply(messages, SearchKind.Photos, SearchPeriod.AnyTime, null, Now)));
    }

    [Fact]
    public void Links_keeps_messages_with_a_web_link()
    {
        var messages = new[] { Msg(1, "see https://example.com/a"), Msg(2, "call me at 7"), Msg(3, "www.example.org works too") };

        Assert.Equal(new long[] { 1, 3 }, Ids(SearchFilters.Apply(messages, SearchKind.Links, SearchPeriod.AnyTime, null, Now)));
    }

    [Theory]
    [InlineData(SearchPeriod.PastWeek, new long[] { 1 })]
    [InlineData(SearchPeriod.PastMonth, new long[] { 1, 2 })]
    [InlineData(SearchPeriod.PastYear, new long[] { 1, 2, 3 })]
    [InlineData(SearchPeriod.AnyTime, new long[] { 1, 2, 3, 4 })]
    public void A_period_keeps_messages_from_that_far_back(SearchPeriod period, long[] expected)
    {
        var messages = new[] { Msg(1, "a", daysAgo: 6), Msg(2, "b", daysAgo: 29), Msg(3, "c", daysAgo: 300), Msg(4, "d", daysAgo: 400) };

        Assert.Equal(expected, Ids(SearchFilters.Apply(messages, SearchKind.All, period, null, Now)));
    }

    [Fact]
    public void A_chat_keeps_only_that_conversation()
    {
        var messages = new[] { Msg(1, "a", threadId: 7), Msg(2, "b", threadId: 8) };

        Assert.Equal(new long[] { 2 }, Ids(SearchFilters.Apply(messages, SearchKind.All, SearchPeriod.AnyTime, 8, Now)));
    }

    [Fact]
    public void Labels_name_each_choice()
    {
        Assert.Equal("Past month", SearchFilters.Label(SearchPeriod.PastMonth));
        Assert.Equal("📷 Photos", SearchFilters.Label(SearchKind.Photos));
    }
}
