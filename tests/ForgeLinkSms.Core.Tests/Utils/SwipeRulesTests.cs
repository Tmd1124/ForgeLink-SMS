using System.Text.Json;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class SwipeRulesTests
{
    [Fact]
    public void By_default_right_trashes_and_left_archives_as_before()
    {
        var settings = new DisplaySettings();

        Assert.Equal(SwipeAction.Trash, SwipeRules.ActionFor(120, settings));
        Assert.Equal(SwipeAction.Archive, SwipeRules.ActionFor(-120, settings));
    }

    [Fact]
    public void Short_drags_are_taps_not_swipes()
    {
        var settings = new DisplaySettings();

        Assert.Equal(SwipeAction.Nothing, SwipeRules.ActionFor(SwipeRules.Threshold - 1, settings));
        Assert.Equal(SwipeAction.Nothing, SwipeRules.ActionFor(-(SwipeRules.Threshold - 1), settings));
        Assert.Equal(SwipeAction.Trash, SwipeRules.ActionFor(SwipeRules.Threshold, settings));
    }

    [Fact]
    public void Each_direction_follows_its_own_setting()
    {
        var settings = new DisplaySettings { SwipeRight = SwipeAction.Archive, SwipeLeft = SwipeAction.Nothing };

        Assert.Equal(SwipeAction.Archive, SwipeRules.ActionFor(150, settings));
        Assert.Equal(SwipeAction.Nothing, SwipeRules.ActionFor(-150, settings));
    }

    [Fact]
    public void Settings_saved_before_swipe_choices_existed_keep_the_old_behavior()
    {
        var old = JsonSerializer.Deserialize<DisplaySettings>("""{"ShowFilterTabs":true,"TextScale":115}""")!;

        Assert.Equal(SwipeAction.Trash, old.SwipeRight);
        Assert.Equal(SwipeAction.Archive, old.SwipeLeft);
    }

    [Fact]
    public void Summarizes_both_directions_for_the_menu()
    {
        Assert.Equal("Right: Trash · Left: Archive", SwipeRules.Summary(new DisplaySettings()));
        Assert.Equal("Right: Archive · Left: Nothing",
            SwipeRules.Summary(new DisplaySettings { SwipeRight = SwipeAction.Archive, SwipeLeft = SwipeAction.Nothing }));
    }
}
