using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupModelsTests
{
    [Fact]
    public void Conversation_key_ignores_order_formatting_and_country_code()
    {
        Assert.Equal(ConversationKey.From(new[] { "+1 (404) 555-0199", "770.555.0101" }), ConversationKey.From(new[] { "7705550101", "4045550199" }));
        Assert.Equal("4045550199", ConversationKey.From(new[] { "+14045550199" }));
    }

    [Fact]
    public void Alphanumeric_senders_keep_a_stable_non_empty_key()
    {
        Assert.Equal("carelonrx pharmacy", ConversationKey.From(new[] { " CarelonRx Pharmacy " }));
        Assert.NotEqual(ConversationKey.From(new[] { "AMAZON" }), ConversationKey.From(new[] { "CHASE" }));
    }

    [Fact]
    public void App_data_round_trips_through_json_including_settings()
    {
        var data = new AppData
        {
            Favorites = new[] { "4045550199" },
            Filters = new[] { new BackupFilter("Softball", "#16a34a", new[] { "4045550199", "7705550101" }) },
            QuickReplies = new[] { "On my way" },
            Scheduled = new[] { new BackupScheduled("4045550199", "4045550199", "Happy birthday!", new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero), "") },
            Muted = new[] { new BackupTimed("7705550101", null) },
            Blocked = new[] { "8005550000" },
            Drafts = new[] { new BackupDraft("4045550199", "half-written") },
            Settings = new BackupSettings(new DisplaySettings { TextScale = 115 }, new NotificationSettings { QuietHoursEnabled = true }, "Dark", "#16a34a")
        };

        var back = BackupJson.Deserialize<AppData>(BackupJson.Serialize(data));

        Assert.Equal(data.Favorites, back.Favorites);
        Assert.Equal("Softball", back.Filters[0].Name);
        Assert.Equal(data.Filters[0].Members, back.Filters[0].Members);
        Assert.Equal(data.Scheduled[0], back.Scheduled[0]);
        Assert.Null(back.Muted[0].UntilUtc);
        Assert.Equal(115, back.Settings!.Display.TextScale);
        Assert.True(back.Settings.Notifications.QuietHoursEnabled);
    }

    [Fact]
    public void Message_round_trips_with_sender_and_attachments()
    {
        var m = new BackupMessage(true, new[] { "4045550199", "7705550101" }, "4045550199", 1_790_000_000_000, 1_790_000_000_000, false, true, 0, "Look!", null,
            new[] { new BackupAttachment("media/1.jpg", "image/jpeg", "IMG_1.jpg") });

        var back = BackupJson.Deserialize<BackupMessage>(BackupJson.Serialize(m));

        Assert.Equal(m.Addresses, back.Addresses);
        Assert.Equal("4045550199", back.From);
        Assert.Equal("media/1.jpg", back.Attachments[0].Media);
    }

    [Fact]
    public void Bad_json_is_reported_as_a_damaged_backup()
    {
        Assert.Throws<BackupDamagedException>(() => BackupJson.Deserialize<AppData>("{not json"));
        Assert.Throws<BackupDamagedException>(() => BackupJson.Deserialize<AppData>("null"));
    }

    [Fact]
    public void Group_names_profile_and_swipe_settings_survive_a_backup()
    {
        var data = new AppData
        {
            GroupNames = new[] { new BackupGroupName("4045550199,7705550101", "Softball Parents") },
            Profile = new BackupProfile("Travis", "7708654177", "t@example.com", "12 Oak St", "Ball Ground", "GA", "30107"),
            Settings = new BackupSettings(new DisplaySettings { SwipeRight = SwipeAction.Archive, SwipeLeft = SwipeAction.Nothing },
                new NotificationSettings(), "System", "#16a34a")
        };

        var back = BackupJson.Deserialize<AppData>(BackupJson.Serialize(data));

        Assert.Equal("Softball Parents", back.GroupNames.Single().Name);
        Assert.Equal("Ball Ground", back.Profile!.City);
        Assert.Equal(SwipeAction.Archive, back.Settings!.Display.SwipeRight);
        Assert.Equal(SwipeAction.Nothing, back.Settings.Display.SwipeLeft);
    }

    [Fact]
    public void Older_backups_without_group_names_or_a_profile_still_read()
    {
        var back = BackupJson.Deserialize<AppData>("""{"Favorites":["4045550199"]}""");

        Assert.Empty(back.GroupNames);
        Assert.Null(back.Profile);
    }
}
