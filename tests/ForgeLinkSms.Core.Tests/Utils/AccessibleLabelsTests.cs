using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class AccessibleLabelsTests
{
    private static readonly DateTime Today = new(2026, 9, 30);

    private static DateTimeOffset Local(int month, int day, int hour, int minute) =>
        new(new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Local));

    private static SmsThread Thread(string? name, string body, int unread = 0, bool favorite = false, bool muted = false, string? draft = null, int day = 30) => new()
    {
        Id = 1,
        Address = "4045550199",
        DisplayName = name,
        LastMessageBody = body,
        LastMessageTimestamp = Local(9, day, 16, 12),
        UnreadCount = unread,
        IsFavorite = favorite,
        IsMuted = muted,
        DraftText = draft
    };

    private static SmsMessage Message(string body, bool outgoing, params AttachmentKind[] kinds) => new()
    {
        Id = 1,
        ThreadId = 1,
        Address = "4045550199",
        Body = body,
        Timestamp = Local(9, 30, 21, 28),
        IsOutgoing = outgoing,
        Status = outgoing ? SmsMessageStatus.Sent : SmsMessageStatus.Delivered,
        Attachments = kinds.Select((k, i) => new MessageAttachment { FileName = $"f{i}", Kind = k, PartId = i }).ToList()
    };

    [Fact]
    public void A_chat_row_reads_name_state_last_message_and_time()
    {
        Assert.Equal("Kim Donnelly, 2 unread, favorite, muted. Last message: Got it. 4:12 PM",
            AccessibleLabels.ForThread(Thread("Kim Donnelly", "Got it", unread: 2, favorite: true, muted: true), Today));
    }

    [Fact]
    public void A_quiet_chat_row_skips_empty_states_and_older_days_read_as_a_date()
    {
        Assert.Equal("4045550199. Last message: hi. Sep 27",
            AccessibleLabels.ForThread(Thread(null, "hi", day: 27), Today));
    }

    [Fact]
    public void A_draft_is_read_instead_of_the_last_message()
    {
        Assert.Equal("Kim. Draft: see you soon. 4:12 PM",
            AccessibleLabels.ForThread(Thread("Kim", "Got it", draft: "see you soon"), Today));
    }

    [Fact]
    public void A_reaction_as_the_last_message_is_read_in_its_short_form()
    {
        Assert.Equal("Kim. Last message: 😂 to an image. 4:12 PM",
            AccessibleLabels.ForThread(Thread("Kim", "Laughed at an image"), Today));
    }

    [Fact]
    public void A_pinned_chat_says_so()
    {
        var thread = Thread("Kim", "Got it");
        thread.IsPinned = true;

        Assert.Equal("Kim, pinned. Last message: Got it. 4:12 PM", AccessibleLabels.ForThread(thread, Today));
    }

    [Fact]
    public void A_bubble_reads_the_name_and_unread_count()
    {
        Assert.Equal("Kim, 3 unread", AccessibleLabels.ForBubble(Thread("Kim", "x", unread: 3)));
        Assert.Equal("Kim", AccessibleLabels.ForBubble(Thread("Kim", "x")));
    }

    [Fact]
    public void An_outgoing_message_is_introduced_as_you_with_its_status()
    {
        Assert.Equal("You, 9:28 PM. Sent",
            AccessibleLabels.MessageLeadIn(Message("See you at 6", outgoing: true), "Kim"));
    }

    [Fact]
    public void An_incoming_message_is_introduced_by_its_sender_and_attachment_count()
    {
        Assert.Equal("Kim, 9:28 PM. 2 photos, 1 video",
            AccessibleLabels.MessageLeadIn(Message("Look", outgoing: false, AttachmentKind.Image, AttachmentKind.Gif, AttachmentKind.Video), "Kim"));
        Assert.Equal("Kim, 9:28 PM", AccessibleLabels.MessageLeadIn(Message("Hi", outgoing: false), "Kim"));
    }

    [Fact]
    public void Reactions_and_a_reminder_are_part_of_the_lead_in()
    {
        var message = Message("Got it", outgoing: false);
        message.Reactions.Add("❤️");

        Assert.Equal("Kim, 9:28 PM. Reactions: ❤️. Reminder tomorrow 8:00 AM",
            AccessibleLabels.MessageLeadIn(message, "Kim", "tomorrow 8:00 AM"));
    }
}
