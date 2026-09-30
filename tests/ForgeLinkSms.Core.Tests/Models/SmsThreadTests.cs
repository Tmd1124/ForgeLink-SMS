using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Models;

public class SmsThreadTests
{
    [Fact]
    public void PreviewText_truncates_long_bodies_to_60_chars_with_ellipsis()
    {
        var longBody = new string('a', 100);
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = longBody,
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal(new string('a', 60) + "…", thread.PreviewText);
    }

    [Fact]
    public void PreviewText_shows_a_reaction_in_its_short_form()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = "Loved “Sounds good”",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("❤️ to “Sounds good”", thread.PreviewText);
    }

    [Fact]
    public void PreviewText_leaves_short_bodies_unchanged()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = "short message",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("short message", thread.PreviewText);
    }

    [Fact]
    public void DisplayNameOrAddress_falls_back_to_address_when_no_contact_match()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("5550142231", thread.DisplayNameOrAddress);
    }

    [Fact]
    public void DisplayNameOrAddress_prefers_contact_name()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = "Alice Smith",
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("Alice Smith", thread.DisplayNameOrAddress);
    }

    [Fact]
    public void Initials_uses_first_letter_of_first_two_words_for_a_full_name()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = "Alice Smith",
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("AS", thread.Initials);
    }

    [Fact]
    public void Initials_uses_first_letter_only_for_a_single_word_name()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = "Brownstone",
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("B", thread.Initials);
    }

    [Fact]
    public void Initials_uses_first_character_of_the_raw_address_when_no_contact_name()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("5", thread.Initials);
    }

    [Fact]
    public void Initials_skip_a_leading_plus_sign_on_a_raw_address()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "+16023466737",
            DisplayName = null,
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("1", thread.Initials);
    }

    [Fact]
    public void Initials_are_uppercased()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = "alice smith",
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("AS", thread.Initials);
    }
}
