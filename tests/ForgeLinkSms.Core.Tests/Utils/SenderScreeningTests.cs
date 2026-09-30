using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class SenderScreeningTests
{
    private static readonly IReadOnlySet<string> NoneAllowed = new HashSet<string>();

    private static SmsThread MakeThread(string? name = null, bool hasOutgoing = false, bool isFavorite = false, string address = "(312) 555-0147") => new()
    {
        Id = 1,
        Address = address,
        DisplayName = name,
        LastMessageBody = "hi",
        LastMessageTimestamp = DateTimeOffset.UtcNow,
        UnreadCount = 0,
        HasOutgoing = hasOutgoing,
        IsFavorite = isFavorite
    };

    [Theory]
    [InlineData("72975")]
    [InlineData("262966")]
    [InlineData("AMAZON")]
    [InlineData("(855) 201-4477")]
    [InlineData("+1 800-555-0199")]
    public void IsAutomatedSender_recognizes_business_senders(string address)
    {
        Assert.True(SenderScreening.IsAutomatedSender(address));
    }

    [Theory]
    [InlineData("(312) 555-0147")]
    [InlineData("+1 312-555-0147")]
    [InlineData("kevin_r@yahoo.com")]
    public void IsAutomatedSender_is_false_for_personal_numbers(string address)
    {
        Assert.False(SenderScreening.IsAutomatedSender(address));
    }

    [Fact]
    public void LaneFor_an_unknown_personal_number_is_the_screener()
    {
        Assert.Equal(ConversationLane.Screener, SenderScreening.LaneFor(MakeThread(), NoneAllowed));
    }

    [Fact]
    public void LaneFor_a_personal_number_you_have_texted_is_conversations()
    {
        Assert.Equal(ConversationLane.Conversations, SenderScreening.LaneFor(MakeThread(hasOutgoing: true), NoneAllowed));
    }

    [Fact]
    public void LaneFor_a_short_code_is_updates_even_after_replying_to_it()
    {
        Assert.Equal(ConversationLane.Updates, SenderScreening.LaneFor(MakeThread(address: "72975", hasOutgoing: true), NoneAllowed));
    }

    [Theory]
    [InlineData("Mom", false, "72975")]
    [InlineData(null, true, "72975")]
    public void LaneFor_contacts_and_favorites_are_conversations(string? name, bool isFavorite, string address)
    {
        Assert.Equal(ConversationLane.Conversations, SenderScreening.LaneFor(MakeThread(name: name, isFavorite: isFavorite, address: address), NoneAllowed));
    }

    [Fact]
    public void LaneFor_an_allowed_number_in_any_format_is_conversations()
    {
        Assert.Equal(ConversationLane.Conversations, SenderScreening.LaneFor(MakeThread(address: "+1 855-201-4477"), new HashSet<string> { "8552014477" }));
    }
}
