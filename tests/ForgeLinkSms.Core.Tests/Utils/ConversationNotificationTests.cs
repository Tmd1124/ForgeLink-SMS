using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class ConversationNotificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);
    private static readonly NotificationLine Fallback = new("4045550199", "Kim", "fallback", Start);
    private static long _id;

    private static SmsMessage Msg(string body, int minute, bool outgoing = false, string address = "+14045550199", params AttachmentKind[] kinds) => new()
    {
        Id = ++_id,
        ThreadId = 1,
        Address = address,
        Body = body,
        Timestamp = Start.AddMinutes(minute),
        IsOutgoing = outgoing,
        Status = SmsMessageStatus.Delivered,
        Attachments = kinds.Select((k, i) => new MessageAttachment { FileName = "f", Kind = k, PartId = i }).ToList()
    };

    // Like the real contact lookup, matches numbers however they are formatted.
    private static string Name(string address) => PhoneNumberFormatter.ToComparableDigits(address) == PhoneNumberFormatter.ToComparableDigits("+17705550101") ? "Sam" : "Kim";

    private static ConversationNotificationModel Build(IReadOnlyList<SmsMessage> unread, bool group = false) =>
        ConversationNotification.Build(unread, group ? "Family" : "Kim", group, Name, Fallback);

    // The caller passes exactly the unread incoming messages, so nothing already read can slip in
    // (a count-based window once pulled in a read message when a reaction was among the unread).
    [Fact]
    public void Shows_the_unread_messages_given_oldest_first_and_never_the_users_own()
    {
        var unread = new[] { Msg("two", 4), Msg("my reply", 2, outgoing: true), Msg("one", 3) };

        var model = Build(unread);

        Assert.Equal(new[] { "one", "two" }, model.Lines.Select(l => l.Text));
        Assert.Equal(("Kim", false), (model.Title, model.IsGroup));
    }

    [Fact]
    public void A_sender_without_a_phone_number_keeps_its_own_key()
    {
        Assert.Equal("CarelonRx", ConversationNotification.SenderKey("CarelonRx"));
        Assert.Equal(ConversationNotification.SenderKey("+1 (404) 555-0199"), ConversationNotification.SenderKey("+14045550199"));
    }

    [Fact]
    public void Caps_at_five_lines()
    {
        var messages = Enumerable.Range(1, 8).Select(i => Msg($"m{i}", i)).ToArray();

        var model = Build(messages);

        Assert.Equal(new[] { "m4", "m5", "m6", "m7", "m8" }, model.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Group_lines_name_their_sender_and_share_a_key_per_person()
    {
        var messages = new[] { Msg("hi", 1, address: "+17705550101"), Msg("hey", 2), Msg("again", 3, address: "+1 (770) 555-0101") };

        var lines = Build(messages, group: true).Lines;

        Assert.Equal(new[] { "Sam", "Kim", "Sam" }, lines.Select(l => l.SenderName));
        Assert.Equal(lines[0].SenderKey, lines[2].SenderKey);
        Assert.NotEqual(lines[0].SenderKey, lines[1].SenderKey);
    }

    [Fact]
    public void Attachments_without_text_get_a_label_and_reactions_their_short_form()
    {
        var messages = new[]
        {
            Msg("", 1, kinds: AttachmentKind.Image),
            Msg("", 2, kinds: AttachmentKind.Video),
            Msg("", 3, kinds: AttachmentKind.Audio),
            Msg("", 4, kinds: AttachmentKind.File),
            Msg("Loved “Sounds good”", 5)
        };

        var texts = Build(messages).Lines.Select(l => l.Text);

        Assert.Equal(new[] { "📷 Photo", "🎥 Video", "🎤 Voice message", "📎 Attachment", "❤️ to “Sounds good”" }, texts);
    }

    [Fact]
    public void Falls_back_to_the_just_received_text_when_nothing_matches()
    {
        Assert.Equal(new[] { Fallback }, Build(Array.Empty<SmsMessage>()).Lines);
        Assert.Equal(new[] { Fallback }, Build(new[] { Msg("mine", 1, outgoing: true) }).Lines);
    }

    [Theory]
    [InlineData("Kim Donnelly", "KD")]
    [InlineData("Kim, Ever & Dare", "KE")]
    [InlineData("CarelonRx", "C")]
    [InlineData("(404) 555-0199", "#")]
    [InlineData("+14045550199", "#")]
    [InlineData("", "?")]
    public void Avatars_without_a_photo_show_initials_or_a_number_sign(string name, string expected) =>
        Assert.Equal(expected, ConversationNotification.InitialsFor(name));
}
