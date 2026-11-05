using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class ReviewPromptRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 18, 0, 0, TimeSpan.Zero);

    private static ReviewPromptState Ready() => new()
    {
        FirstUseUtc = Now.AddDays(-10),
        SentCount = 25,
        LastAskedUtc = null
    };

    [Fact]
    public void Asks_after_a_week_and_twenty_sent_messages_when_never_asked()
    {
        Assert.True(ReviewPromptRules.ShouldAsk(Ready(), Now));
    }

    [Fact]
    public void Waits_until_the_app_has_been_used_for_seven_days()
    {
        Assert.False(ReviewPromptRules.ShouldAsk(Ready() with { FirstUseUtc = Now.AddDays(-6) }, Now));
        Assert.True(ReviewPromptRules.ShouldAsk(Ready() with { FirstUseUtc = Now.AddDays(-7) }, Now));
    }

    [Fact]
    public void Waits_for_twenty_sent_messages()
    {
        Assert.False(ReviewPromptRules.ShouldAsk(Ready() with { SentCount = 19 }, Now));
        Assert.True(ReviewPromptRules.ShouldAsk(Ready() with { SentCount = 20 }, Now));
    }

    [Fact]
    public void Asks_again_only_after_120_days()
    {
        Assert.False(ReviewPromptRules.ShouldAsk(Ready() with { LastAskedUtc = Now.AddDays(-119) }, Now));
        Assert.True(ReviewPromptRules.ShouldAsk(Ready() with { LastAskedUtc = Now.AddDays(-120) }, Now));
    }

    [Fact]
    public void Never_asks_without_a_first_use_date()
    {
        Assert.False(ReviewPromptRules.ShouldAsk(Ready() with { FirstUseUtc = null }, Now));
    }

    [Fact]
    public void Never_asks_Google_Play_once_the_person_has_rated_in_the_app()
    {
        Assert.False(ReviewPromptRules.ShouldAsk(Ready() with { Rated = true }, Now));
    }

    [Fact]
    public void Shows_the_in_app_card_to_installs_not_from_Google_Play_when_the_same_rules_are_met()
    {
        Assert.True(ReviewPromptRules.ShouldShowCard(Ready(), Now));
        Assert.False(ReviewPromptRules.ShouldShowCard(Ready() with { FirstUseUtc = Now.AddDays(-6) }, Now));
        Assert.False(ReviewPromptRules.ShouldShowCard(Ready() with { SentCount = 19 }, Now));
        Assert.False(ReviewPromptRules.ShouldShowCard(Ready() with { FirstUseUtc = null }, Now));
    }

    [Fact]
    public void Leaves_Google_Play_installs_to_the_Google_card()
    {
        Assert.False(ReviewPromptRules.ShouldShowCard(Ready() with { InstalledFromPlay = true }, Now));
    }

    [Fact]
    public void Stops_showing_the_card_once_rated()
    {
        Assert.False(ReviewPromptRules.ShouldShowCard(Ready() with { Rated = true }, Now));
    }

    [Fact]
    public void Hides_the_card_for_120_days_after_Not_now()
    {
        Assert.False(ReviewPromptRules.ShouldShowCard(Ready() with { CardDismissedUtc = Now.AddDays(-119) }, Now));
        Assert.True(ReviewPromptRules.ShouldShowCard(Ready() with { CardDismissedUtc = Now.AddDays(-120) }, Now));
    }
}
