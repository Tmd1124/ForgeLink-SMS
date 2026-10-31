using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class FilterNameTests
{
    [Fact]
    public void A_picture_on_an_empty_name_leads_it_and_otherwise_follows_the_words()
    {
        Assert.Equal("🖼 ", FilterName.WithMarker(""));
        Assert.Equal("Golf 🖼", FilterName.WithMarker("Golf"));
    }

    [Fact]
    public void A_second_picture_does_not_add_another_marker()
    {
        Assert.Equal("Golf 🖼", FilterName.WithMarker("Golf 🖼"));
    }

    [Fact]
    public void Removing_the_picture_takes_the_marker_and_extra_spaces_out()
    {
        Assert.Equal("Golf", FilterName.WithoutMarker("Golf 🖼"));
        Assert.Equal("Golf Club", FilterName.WithoutMarker("Golf 🖼 Club"));
    }

    [Fact]
    public void Split_puts_the_picture_where_the_marker_is_and_first_when_there_is_none()
    {
        Assert.Equal(("Golf ", " Club"), FilterName.Split("Golf 🖼 Club"));
        Assert.Equal(("", "Golf"), FilterName.Split("Golf"));
    }

    [Fact]
    public void Plain_reads_as_words_and_calls_a_picture_only_name_picture()
    {
        Assert.Equal("Golf", FilterName.Plain("Golf 🖼"));
        Assert.Equal("picture", FilterName.Plain("🖼 "));
    }
}
