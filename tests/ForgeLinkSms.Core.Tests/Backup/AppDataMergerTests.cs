using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Backup;

public class AppDataMergerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlySet<string> Restored = new HashSet<string> { "a", "b" };

    private static MergePlan Plan(AppData current, AppData backup, bool settings = false, bool scheduled = true) =>
        AppDataMerger.Plan(current, backup, Restored, settings, scheduled, Now);

    [Fact]
    public void Adds_only_missing_favorites_blocks_allowed_and_quick_replies()
    {
        var plan = Plan(
            new AppData { Favorites = new[] { "a" }, Blocked = new[] { "900" }, Allowed = new[] { "700" }, QuickReplies = new[] { "On my way" } },
            new AppData { Favorites = new[] { "a", "b" }, Blocked = new[] { "900", "901" }, Allowed = new[] { "700", "701" }, QuickReplies = new[] { "On my way", "Call you soon" } });

        Assert.Equal(new[] { "b" }, plan.FavoritesToAdd);
        Assert.Equal(new[] { "901" }, plan.BlockedToAdd);
        Assert.Equal(new[] { "701" }, plan.AllowedToAdd);
        Assert.Equal(new[] { "Call you soon" }, plan.QuickRepliesToAdd);
    }

    [Fact]
    public void Filters_join_by_name_ignoring_case_and_add_only_new_members()
    {
        var plan = Plan(
            new AppData { Filters = new[] { new BackupFilter("Softball", "#16a34a", new[] { "a" }) } },
            new AppData { Filters = new[] { new BackupFilter("softball", "#e11d48", new[] { "a", "b" }), new BackupFilter("Work", "#0ea5e9", new[] { "b" }) } });

        Assert.Equal(new[] { new FilterToCreate("Work", "#0ea5e9") }, plan.FiltersToCreate);
        Assert.Equal(new[] { new FilterMember("Softball", "b"), new FilterMember("Work", "b") }, plan.MembersToAdd);
    }

    [Fact]
    public void Scheduled_texts_are_added_once_and_only_if_still_in_the_future()
    {
        var future = new BackupScheduled("a", "4045550199", "Happy birthday", Now.AddDays(3), "");
        var past = future with { SendAtUtc = Now.AddDays(-1), Body = "Too late" };

        Assert.Equal(new[] { future }, Plan(new AppData(), new AppData { Scheduled = new[] { future, past } }).ScheduledToAdd);
        Assert.Empty(Plan(new AppData { Scheduled = new[] { future } }, new AppData { Scheduled = new[] { future } }).ScheduledToAdd);
    }

    [Fact]
    public void Conversation_states_apply_only_to_restored_conversations()
    {
        var plan = Plan(new AppData { Archived = new[] { "b" } }, new AppData
        {
            Archived = new[] { "a", "b", "z" },
            Trashed = new[] { "z" },
            Snoozed = new[] { new BackupTimed("a", Now.AddHours(2)), new BackupTimed("b", Now.AddHours(-2)) },
            Muted = new[] { new BackupTimed("a", null), new BackupTimed("z", null) },
            Drafts = new[] { new BackupDraft("a", "hello"), new BackupDraft("z", "nope") }
        });

        Assert.Equal(new[] { "a" }, plan.ArchiveToApply);
        Assert.Empty(plan.TrashToApply);
        Assert.Equal(new[] { new BackupTimed("a", Now.AddHours(2)) }, plan.SnoozesToApply);
        Assert.Equal(new[] { new BackupTimed("a", null) }, plan.MutesToApply);
        Assert.Equal(new[] { new BackupDraft("a", "hello") }, plan.DraftsToAdd);
    }

    [Fact]
    public void A_repeating_scheduled_text_is_kept_even_when_its_last_time_has_passed()
    {
        var weekly = new BackupScheduled("a", "4045550199", "Trash day", Now.AddDays(-2), "", ScheduleRepeat.Weekly, Now.AddDays(-30));

        Assert.Equal(new[] { weekly }, Plan(new AppData(), new AppData { Scheduled = new[] { weekly } }).ScheduledToAdd);
    }

    [Fact]
    public void A_repeating_scheduled_text_already_on_the_phone_is_not_added_again_after_it_moved_on()
    {
        var backedUp = new BackupScheduled("a", "4045550199", "Trash day", Now.AddDays(-2), "", ScheduleRepeat.Weekly, Now.AddDays(-30));
        var onPhone = backedUp with { SendAtUtc = Now.AddDays(5) };

        Assert.Empty(Plan(new AppData { Scheduled = new[] { onPhone } }, new AppData { Scheduled = new[] { backedUp } }).ScheduledToAdd);
    }

    [Fact]
    public void Pins_come_back_only_for_restored_chats_and_are_not_repeated()
    {
        var plan = Plan(new AppData { Pinned = new[] { "b" } }, new AppData { Pinned = new[] { "a", "b", "z" } });

        Assert.Equal(new[] { "a" }, plan.PinsToApply);
    }

    [Fact]
    public void Scheduled_texts_are_re_added_only_when_the_user_opts_in()
    {
        var future = new BackupScheduled("a", "4045550199", "Happy birthday", Now.AddDays(3), "");

        Assert.Empty(Plan(new AppData(), new AppData { Scheduled = new[] { future } }, scheduled: false).ScheduledToAdd);
    }

    [Fact]
    public void Two_filters_whose_names_differ_only_by_case_do_not_break_the_merge()
    {
        var plan = Plan(
            new AppData { Filters = new[] { new BackupFilter("Work", "#0ea5e9", new[] { "a" }), new BackupFilter("work", "#16a34a", Array.Empty<string>()) } },
            new AppData { Filters = new[] { new BackupFilter("WORK", "#e11d48", new[] { "b" }) } });

        Assert.Empty(plan.FiltersToCreate);
        Assert.Equal(new[] { new FilterMember("Work", "b") }, plan.MembersToAdd);
    }

    [Fact]
    public void Existing_drafts_are_never_overwritten()
    {
        var plan = Plan(new AppData { Drafts = new[] { new BackupDraft("a", "mine") } }, new AppData { Drafts = new[] { new BackupDraft("a", "old") } });

        Assert.Empty(plan.DraftsToAdd);
    }

    [Fact]
    public void Settings_are_restored_only_when_asked()
    {
        var backup = new AppData { Settings = new BackupSettings(new DisplaySettings { TextScale = 130 }, new NotificationSettings(), "Dark", "#16a34a") };

        Assert.Null(Plan(new AppData(), backup).Settings);
        Assert.Equal(130, Plan(new AppData(), backup, settings: true).Settings!.Display.TextScale);
    }

    [Fact]
    public void Merging_the_same_backup_twice_changes_nothing_the_second_time()
    {
        var backup = new AppData { Favorites = new[] { "a" }, Filters = new[] { new BackupFilter("Work", "#0ea5e9", new[] { "a" }) }, QuickReplies = new[] { "Ok" } };

        var plan = Plan(backup, backup);

        Assert.Empty(plan.FavoritesToAdd);
        Assert.Empty(plan.FiltersToCreate);
        Assert.Empty(plan.MembersToAdd);
        Assert.Empty(plan.QuickRepliesToAdd);
    }
}
