using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class ReactionAttacherTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddHours(-1);
    private static int _id;

    private static SmsMessage Message(string body, int minute, bool outgoing = false, bool withImage = false) => new()
    {
        Id = ++_id,
        ThreadId = 1,
        Address = "555",
        Body = body,
        Timestamp = Start.AddMinutes(minute),
        IsOutgoing = outgoing,
        Status = SmsMessageStatus.Delivered,
        Attachments = withImage
            ? new[] { new MessageAttachment { FileName = "p.jpg", Kind = AttachmentKind.Image, PartId = _id } }
            : Array.Empty<MessageAttachment>()
    };

    [Fact]
    public void A_reaction_becomes_a_badge_on_the_quoted_message_and_is_hidden()
    {
        var original = Message("Sounds good", 1, outgoing: true);
        var reaction = Message("Loved “Sounds good”", 2);
        var messages = new List<SmsMessage> { original, reaction };

        ReactionAttacher.Apply(messages);

        Assert.Equal(new[] { "❤️" }, original.Reactions);
        Assert.True(reaction.IsHiddenReaction);
    }

    private static SmsMessage WithAttachment(int minute, AttachmentKind kind, bool outgoing = true) => new()
    {
        Id = ++_id,
        ThreadId = 1,
        Address = "555",
        Body = string.Empty,
        Timestamp = Start.AddMinutes(minute),
        IsOutgoing = outgoing,
        Status = SmsMessageStatus.Delivered,
        Attachments = new[] { new MessageAttachment { FileName = "f", Kind = kind, PartId = _id } }
    };

    [Fact]
    public void A_reaction_to_a_movie_lands_on_the_latest_video_not_a_later_photo()
    {
        var video = WithAttachment(1, AttachmentKind.Video);
        var photo = WithAttachment(2, AttachmentKind.Image);
        var reaction = Message("Loved a movie", 3);

        ReactionAttacher.Apply(new List<SmsMessage> { video, photo, reaction });

        Assert.Equal(new[] { "❤️" }, video.Reactions);
        Assert.Empty(photo.Reactions);
        Assert.True(reaction.IsHiddenReaction);
    }

    [Fact]
    public void A_reaction_to_an_attachment_lands_on_the_latest_message_with_any_attachment()
    {
        var file = WithAttachment(1, AttachmentKind.File);
        var reaction = Message("Emphasized an attachment", 2);

        ReactionAttacher.Apply(new List<SmsMessage> { file, reaction });

        Assert.Equal(new[] { "‼️" }, file.Reactions);
    }

    [Fact]
    public void A_reaction_to_a_movie_with_no_video_loaded_stays_as_text()
    {
        var photo = WithAttachment(1, AttachmentKind.Image);
        var reaction = Message("Loved a movie", 2);

        ReactionAttacher.Apply(new List<SmsMessage> { photo, reaction });

        Assert.Empty(photo.Reactions);
        Assert.False(reaction.IsHiddenReaction);
    }

    [Fact]
    public void A_shortened_quote_matches_the_start_of_a_long_message()
    {
        var original = Message("Can you pick up milk and eggs on the way home tonight please", 1, outgoing: true);
        var reaction = Message("Liked “Can you pick up milk and eggs on the…”", 2);

        ReactionAttacher.Apply(new List<SmsMessage> { original, reaction });

        Assert.Equal(new[] { "👍" }, original.Reactions);
    }

    [Fact]
    public void It_attaches_to_the_most_recent_matching_message()
    {
        var older = Message("ok", 1, outgoing: true);
        var newer = Message("ok", 3, outgoing: true);
        var reaction = Message("Liked “ok”", 4);

        ReactionAttacher.Apply(new List<SmsMessage> { older, newer, reaction });

        Assert.Empty(older.Reactions);
        Assert.Equal(new[] { "👍" }, newer.Reactions);
    }

    [Fact]
    public void A_photo_reaction_attaches_to_the_latest_photo()
    {
        var photo = Message("", 1, outgoing: true, withImage: true);
        var text = Message("that's the one", 2, outgoing: true);
        var reaction = Message("Loved an image", 3);

        ReactionAttacher.Apply(new List<SmsMessage> { photo, text, reaction });

        Assert.Equal(new[] { "❤️" }, photo.Reactions);
        Assert.True(reaction.IsHiddenReaction);
    }

    [Fact]
    public void A_removed_reaction_takes_the_badge_away()
    {
        var original = Message("Sounds good", 1, outgoing: true);
        var reaction = Message("Loved “Sounds good”", 2);
        var removal = Message("Removed a heart from “Sounds good”", 3);

        ReactionAttacher.Apply(new List<SmsMessage> { original, reaction, removal });

        Assert.Empty(original.Reactions);
        Assert.True(reaction.IsHiddenReaction);
        Assert.True(removal.IsHiddenReaction);
    }

    [Fact]
    public void A_reaction_whose_message_is_not_loaded_stays_visible()
    {
        var reaction = Message("Loved “something from last year”", 2);

        ReactionAttacher.Apply(new List<SmsMessage> { Message("hi", 1), reaction });

        Assert.False(reaction.IsHiddenReaction);
    }

    [Fact]
    public void Applying_again_after_more_history_loads_does_not_double_count()
    {
        var original = Message("Sounds good", 1, outgoing: true);
        var messages = new List<SmsMessage> { original, Message("Loved “Sounds good”", 2) };

        ReactionAttacher.Apply(messages);
        ReactionAttacher.Apply(messages);

        Assert.Equal(new[] { "❤️" }, original.Reactions);
    }
}
