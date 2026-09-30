using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class NotificationRulesTests
{
    private static readonly TimeOnly Noon = new(12, 0);
    private const string Code = "Your verification code is 482913";
    private const string Plain = "Your package is out for delivery";

    [Fact]
    public void Defaults_match_the_old_behavior()
    {
        var settings = new NotificationSettings();

        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.Favorites, "hi", settings, Noon));
        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.OtherChats, "hi", settings, Noon));
        Assert.Equal(NotifyOutcome.None, NotificationRules.Decide(NotifyGroup.Updates, Plain, settings, Noon));
        Assert.Equal(NotifyOutcome.None, NotificationRules.Decide(NotifyGroup.Screener, Plain, settings, Noon));
    }

    [Theory]
    [InlineData(true, true, NotifyOutcome.Sound)]
    [InlineData(true, false, NotifyOutcome.Silent)]
    [InlineData(false, true, NotifyOutcome.None)]
    [InlineData(false, false, NotifyOutcome.None)]
    public void Each_group_follows_its_own_notify_and_sound_switches(bool notify, bool sound, NotifyOutcome expected)
    {
        foreach (var group in Enum.GetValues<NotifyGroup>())
        {
            var settings = new NotificationSettings();
            settings.Set(group, new GroupNotifySetting(notify, sound));

            Assert.Equal(expected, NotificationRules.Decide(group, "hi", settings, Noon));
        }
    }

    [Fact]
    public void Groups_are_independent_so_any_combination_works()
    {
        var settings = new NotificationSettings
        {
            Favorites = new(true, true),
            OtherChats = new(true, false),
            Updates = new(false, false),
            Screener = new(true, true)
        };

        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.Favorites, "hi", settings, Noon));
        Assert.Equal(NotifyOutcome.Silent, NotificationRules.Decide(NotifyGroup.OtherChats, "hi", settings, Noon));
        Assert.Equal(NotifyOutcome.None, NotificationRules.Decide(NotifyGroup.Updates, Plain, settings, Noon));
        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.Screener, "hi", settings, Noon));
    }

    [Fact]
    public void Master_switch_off_silences_everything_including_codes()
    {
        var settings = NotificationRules.Everything(new NotificationSettings());
        settings.Enabled = false;

        foreach (var group in Enum.GetValues<NotifyGroup>())
        {
            Assert.Equal(NotifyOutcome.None, NotificationRules.Decide(group, Code, settings, Noon));
        }
    }

    [Fact]
    public void Codes_alert_with_sound_even_when_their_group_is_off()
    {
        var settings = new NotificationSettings();

        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.Updates, Code, settings, Noon));
        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.Screener, Code, settings, Noon));
    }

    [Fact]
    public void Codes_follow_their_group_when_the_code_switch_is_off()
    {
        var settings = new NotificationSettings { AlwaysAlertForCodes = false };

        Assert.Equal(NotifyOutcome.None, NotificationRules.Decide(NotifyGroup.Updates, Code, settings, Noon));
    }

    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(2, 30, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(21, 59, false)]
    [InlineData(22, 0, true)]
    public void Quiet_hours_overnight_turn_sound_into_silent(int hour, int minute, bool quiet)
    {
        var settings = new NotificationSettings { QuietHoursEnabled = true, QuietStart = new(22, 0), QuietEnd = new(7, 0) };

        var outcome = NotificationRules.Decide(NotifyGroup.OtherChats, "hi", settings, new TimeOnly(hour, minute));

        Assert.Equal(quiet ? NotifyOutcome.Silent : NotifyOutcome.Sound, outcome);
    }

    [Fact]
    public void Quiet_hours_within_one_day_work_too()
    {
        var settings = new NotificationSettings { QuietHoursEnabled = true, QuietStart = new(13, 0), QuietEnd = new(15, 0) };

        Assert.Equal(NotifyOutcome.Silent, NotificationRules.Decide(NotifyGroup.OtherChats, "hi", settings, new TimeOnly(14, 0)));
        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.OtherChats, "hi", settings, new TimeOnly(15, 0)));
    }

    [Fact]
    public void Quiet_hours_are_ignored_when_turned_off_or_start_equals_end()
    {
        var off = new NotificationSettings { QuietHoursEnabled = false, QuietStart = new(0, 0), QuietEnd = new(23, 59) };
        var empty = new NotificationSettings { QuietHoursEnabled = true, QuietStart = new(9, 0), QuietEnd = new(9, 0) };

        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.OtherChats, "hi", off, Noon));
        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.OtherChats, "hi", empty, new TimeOnly(9, 0)));
    }

    [Fact]
    public void Quiet_hours_never_add_a_notification_that_was_off()
    {
        var settings = new NotificationSettings { QuietHoursEnabled = true };

        Assert.Equal(NotifyOutcome.None, NotificationRules.Decide(NotifyGroup.Updates, Plain, settings, new TimeOnly(23, 0)));
    }

    [Fact]
    public void Favorites_can_ring_during_quiet_hours()
    {
        var settings = new NotificationSettings { QuietHoursEnabled = true, FavoritesRingDuringQuietHours = true };
        var night = new TimeOnly(23, 0);

        Assert.Equal(NotifyOutcome.Sound, NotificationRules.Decide(NotifyGroup.Favorites, "hi", settings, night));
        Assert.Equal(NotifyOutcome.Silent, NotificationRules.Decide(NotifyGroup.OtherChats, "hi", settings, night));
    }

    [Fact]
    public void Codes_are_silent_during_quiet_hours()
    {
        var settings = new NotificationSettings { QuietHoursEnabled = true };

        Assert.Equal(NotifyOutcome.Silent, NotificationRules.Decide(NotifyGroup.Updates, Code, settings, new TimeOnly(23, 0)));
    }

    [Theory]
    [InlineData(ConversationLane.Conversations, true, NotifyGroup.Favorites)]
    [InlineData(ConversationLane.Conversations, false, NotifyGroup.OtherChats)]
    [InlineData(ConversationLane.Updates, false, NotifyGroup.Updates)]
    [InlineData(ConversationLane.Screener, false, NotifyGroup.Screener)]
    public void Group_comes_from_the_lane_and_favorite(ConversationLane lane, bool isFavorite, NotifyGroup expected)
    {
        Assert.Equal(expected, NotificationRules.GroupFor(lane, isFavorite));
    }

    [Fact]
    public void Everything_preset_notifies_every_group_with_sound()
    {
        var preset = NotificationRules.Everything(new NotificationSettings { QuietHoursEnabled = true });

        foreach (var group in Enum.GetValues<NotifyGroup>())
        {
            Assert.Equal(new GroupNotifySetting(true, true), preset.For(group));
        }
        Assert.True(preset.QuietHoursEnabled);
    }

    [Fact]
    public void Favorites_only_preset()
    {
        var preset = NotificationRules.FavoritesOnly(new NotificationSettings());

        Assert.Equal(new GroupNotifySetting(true, true), preset.Favorites);
        Assert.Equal(new GroupNotifySetting(false, false), preset.OtherChats);
        Assert.Equal(new GroupNotifySetting(false, false), preset.Updates);
        Assert.Equal(new GroupNotifySetting(false, false), preset.Screener);
    }

    [Fact]
    public void Everything_silent_preset()
    {
        var preset = NotificationRules.EverythingSilent(new NotificationSettings());

        foreach (var group in Enum.GetValues<NotifyGroup>())
        {
            Assert.Equal(new GroupNotifySetting(true, false), preset.For(group));
        }
    }

    [Fact]
    public void Settings_survive_being_saved_as_json()
    {
        var settings = new NotificationSettings
        {
            Enabled = false,
            OtherChats = new(true, false),
            Screener = new(true, true),
            AlwaysAlertForCodes = false,
            QuietHoursEnabled = true,
            QuietStart = new(21, 30),
            QuietEnd = new(6, 15),
            FavoritesRingDuringQuietHours = true
        };

        var restored = System.Text.Json.JsonSerializer.Deserialize<NotificationSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;

        Assert.False(restored.Enabled);
        Assert.Equal(new GroupNotifySetting(true, false), restored.OtherChats);
        Assert.Equal(new GroupNotifySetting(true, true), restored.Screener);
        Assert.False(restored.AlwaysAlertForCodes);
        Assert.True(restored.QuietHoursEnabled && restored.FavoritesRingDuringQuietHours);
        Assert.Equal(new TimeOnly(21, 30), restored.QuietStart);
        Assert.Equal(new TimeOnly(6, 15), restored.QuietEnd);
    }

    [Fact]
    public void Presets_do_not_change_the_settings_they_start_from()
    {
        var current = new NotificationSettings();

        NotificationRules.Everything(current);

        Assert.Equal(new GroupNotifySetting(false, false), current.Updates);
    }
}
