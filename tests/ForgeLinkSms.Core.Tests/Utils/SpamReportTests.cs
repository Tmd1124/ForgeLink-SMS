using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class SpamReportTests
{
    [Fact]
    public void Forwards_the_spam_text_then_the_senders_number_to_7726()
    {
        Assert.Equal("7726", SpamReport.Number);
        Assert.Equal(new[] { "You WON a $500 gift card! Claim: bit.ly/x", "+1 404-555-0199" },
            SpamReport.MessagesFor("  You WON a $500 gift card! Claim: bit.ly/x \n", "+1 404-555-0199"));
    }

    [Fact]
    public void A_picture_message_with_no_text_still_reports_the_number()
    {
        Assert.Equal(new[] { "(picture message)", "4045550199" }, SpamReport.MessagesFor(null, "4045550199"));
        Assert.Equal(new[] { "(picture message)", "4045550199" }, SpamReport.MessagesFor("   ", "4045550199"));
    }

    [Theory]
    [InlineData("7726", true)]
    [InlineData("+7726", true)]
    [InlineData("77260", false)]
    [InlineData("4045550199", false)]
    public void Recognizes_the_report_conversation(string address, bool expected)
    {
        Assert.Equal(expected, SpamReport.IsReportThread(address));
    }

    [Fact]
    public void Display_defaults_keep_link_previews_on_at_normal_text_size()
    {
        var settings = new DisplaySettings();

        Assert.True(settings.ShowLinkPreviews);
        Assert.Equal(100, settings.TextScale);
        Assert.Equal(new[] { 90, 100, 115, 130 }, DisplayRules.TextScales);
    }

    [Theory]
    [InlineData(115, 115)]
    [InlineData(0, 100)]
    [InlineData(250, 100)]
    public void Unknown_text_sizes_fall_back_to_normal(int stored, int expected)
    {
        Assert.Equal(expected, DisplayRules.TextScaleOrDefault(stored));
    }
}
