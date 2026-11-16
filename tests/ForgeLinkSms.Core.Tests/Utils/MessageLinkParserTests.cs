using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class MessageLinkParserTests
{
    [Fact]
    public void Parse_returns_single_plain_text_segment_when_no_entities_present()
    {
        var segments = MessageLinkParser.Parse("just a normal message with no links");

        var segment = Assert.Single(segments);
        Assert.Equal(MessageLinkKind.PlainText, segment.Kind);
        Assert.Equal("just a normal message with no links", segment.Text);
    }

    [Theory]
    [InlineData("check out https://example.com/page for info", "https://example.com/page")]
    [InlineData("see www.example.com today", "www.example.com")]
    public void Parse_detects_urls(string text, string expectedUrl)
    {
        var segments = MessageLinkParser.Parse(text);

        var urlSegment = Assert.Single(segments, s => s.Kind == MessageLinkKind.Url);
        Assert.Equal(expectedUrl, urlSegment.Text);
    }

    [Theory]
    [InlineData("call me at 555-014-2231 later")]
    [InlineData("call me at (555) 014-2231 later")]
    [InlineData("call me at +1 555-014-2231 later")]
    public void Parse_detects_phone_numbers(string text)
    {
        var segments = MessageLinkParser.Parse(text);

        var phoneSegment = Assert.Single(segments, s => s.Kind == MessageLinkKind.PhoneNumber);
        Assert.Equal("5550142231", Assert.IsType<string>(phoneSegment.Data));
    }

    [Fact]
    public void Parse_detects_street_addresses()
    {
        var segments = MessageLinkParser.Parse("meet me at 123 Main St, Springfield, IL for lunch");

        var addressSegment = Assert.Single(segments, s => s.Kind == MessageLinkKind.Address);
        Assert.Contains("123 Main St", addressSegment.Text);
    }

    [Fact]
    public void Parse_does_not_treat_plain_sentences_as_addresses()
    {
        var segments = MessageLinkParser.Parse("I will see you later today");

        Assert.DoesNotContain(segments, s => s.Kind == MessageLinkKind.Address);
    }

    [Theory]
    [InlineData("let's meet tomorrow at 3pm", 1, 15, 0)]
    [InlineData("call tonight at 7", 0, 19, 0)]
    [InlineData("free Friday at 2:30 PM?", null, 14, 30)]
    public void Parse_detects_relative_day_and_weekday_appointments(string text, int? dayOffset, int expectedHour, int expectedMinute)
    {
        var now = new DateTime(2026, 9, 21, 9, 0, 0); // a Monday
        var segments = MessageLinkParser.Parse(text, now);

        var appointment = Assert.Single(segments, s => s.Kind == MessageLinkKind.Appointment);
        var parsed = Assert.IsType<DateTime>(appointment.Data);
        Assert.Equal(expectedHour, parsed.Hour);
        Assert.Equal(expectedMinute, parsed.Minute);
        if (dayOffset.HasValue)
        {
            Assert.Equal(now.Date.AddDays(dayOffset.Value), parsed.Date);
        }
    }

    [Fact]
    public void Parse_detects_numeric_date_with_time()
    {
        var now = new DateTime(2026, 9, 21, 9, 0, 0);
        var segments = MessageLinkParser.Parse("appointment on 9/25 at 10am", now);

        var appointment = Assert.Single(segments, s => s.Kind == MessageLinkKind.Appointment);
        var parsed = Assert.IsType<DateTime>(appointment.Data);
        Assert.Equal(new DateTime(2026, 9, 25, 10, 0, 0), parsed);
    }

    [Fact]
    public void Parse_does_not_detect_a_date_without_an_accompanying_time()
    {
        var segments = MessageLinkParser.Parse("let's meet on the 25th");

        Assert.DoesNotContain(segments, s => s.Kind == MessageLinkKind.Appointment);
    }

    [Fact]
    public void Parse_preserves_plain_text_around_and_between_detected_entities()
    {
        var segments = MessageLinkParser.Parse("Call 555-014-2231 or visit https://example.com now");

        Assert.Equal(MessageLinkKind.PlainText, segments[0].Kind);
        Assert.Equal("Call ", segments[0].Text);
        Assert.Equal(MessageLinkKind.PhoneNumber, segments[1].Kind);
        Assert.Equal(MessageLinkKind.PlainText, segments[2].Kind);
        Assert.Equal(" or visit ", segments[2].Text);
        Assert.Equal(MessageLinkKind.Url, segments[3].Kind);
        Assert.Equal(MessageLinkKind.PlainText, segments[4].Kind);
        Assert.Equal(" now", segments[4].Text);
    }

    [Theory]
    [InlineData("go to example.com/menu now", "example.com/menu")]
    [InlineData("see example.com.", "example.com")]
    [InlineData("(https://example.com/a?b=1)", "https://example.com/a?b=1")]
    public void Parse_detects_bare_domains_and_trims_trailing_punctuation(string text, string expected)
    {
        var url = Assert.Single(MessageLinkParser.Parse(text), s => s.Kind == MessageLinkKind.Url);
        Assert.Equal(expected, url.Text);
    }

    [Fact]
    public void Parse_does_not_treat_email_addresses_as_urls()
    {
        Assert.DoesNotContain(MessageLinkParser.Parse("mail bob@example.com please"), s => s.Kind == MessageLinkKind.Url);
    }

    [Theory]
    [InlineData("dinner Nov 20 at 7pm", 2026, 11, 20, 19, 0)]
    [InlineData("dinner 20 November 6:30pm", 2026, 11, 20, 18, 30)]
    [InlineData("2026-12-01 14:30", 2026, 12, 1, 14, 30)]
    [InlineData("3pm tomorrow", 2026, 9, 22, 15, 0)]
    [InlineData("see you at noon on Friday", 2026, 9, 25, 12, 0)]
    [InlineData("meet 9/25/26 @ 8:15 a.m.", 2026, 9, 25, 8, 15)]
    [InlineData("party on January 5 at 9pm", 2027, 1, 5, 21, 0)]
    public void Parse_detects_varied_date_time_formats(string text, int y, int mo, int d, int h, int mi)
    {
        var now = new DateTime(2026, 9, 21, 9, 0, 0);
        var appt = Assert.Single(MessageLinkParser.Parse(text, now), s => s.Kind == MessageLinkKind.Appointment);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), Assert.IsType<DateTime>(appt.Data));
    }

    [Fact]
    public void Parse_treats_a_date_without_a_time_as_an_all_day_event()
    {
        var appt = Assert.Single(MessageLinkParser.Parse("flight on Dec 3", new DateTime(2026, 9, 21)), s => s.Kind == MessageLinkKind.Appointment);
        Assert.Equal(new DateOnly(2026, 12, 3), Assert.IsType<DateOnly>(appt.Data));
    }

    [Theory]
    [InlineData("I sat down today")]
    [InlineData("see you Friday")]
    [InlineData("it may rain")]
    public void Parse_leaves_ordinary_words_alone(string text)
    {
        Assert.DoesNotContain(MessageLinkParser.Parse(text, new DateTime(2026, 9, 21)), s => s.Kind != MessageLinkKind.PlainText);
    }

    [Fact]
    public void Parse_address_beats_time_after_at()
    {
        var segs = MessageLinkParser.Parse("meet at 5 Oak St tomorrow");
        Assert.Contains(segs, s => s.Kind == MessageLinkKind.Address);
    }
}
