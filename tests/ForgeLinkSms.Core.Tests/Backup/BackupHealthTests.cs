using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nudges_when_there_has_never_been_a_backup_and_weekly_is_off() =>
        Assert.True(BackupHealth.ShouldNudge(lastBackup: null, weeklyEnabled: false, dismissedAt: null, Now));

    [Fact]
    public void Does_not_nudge_while_weekly_backups_are_on() =>
        Assert.False(BackupHealth.ShouldNudge(lastBackup: null, weeklyEnabled: true, dismissedAt: null, Now));

    [Fact]
    public void Does_not_nudge_within_two_weeks_of_a_backup()
    {
        Assert.False(BackupHealth.ShouldNudge(Now.AddDays(-13), weeklyEnabled: false, dismissedAt: null, Now));
        Assert.True(BackupHealth.ShouldNudge(Now.AddDays(-14), weeklyEnabled: false, dismissedAt: null, Now));
    }

    [Fact]
    public void A_dismissed_nudge_comes_back_after_thirty_days()
    {
        Assert.False(BackupHealth.ShouldNudge(null, weeklyEnabled: false, dismissedAt: Now.AddDays(-29), Now));
        Assert.True(BackupHealth.ShouldNudge(null, weeklyEnabled: false, dismissedAt: Now.AddDays(-30), Now));
    }

    [Fact]
    public void Warns_when_weekly_backups_have_not_succeeded_for_three_weeks()
    {
        Assert.False(BackupHealth.ShouldWarn(weeklyEnabled: true, lastBackup: Now.AddDays(-20), weeklySince: Now.AddDays(-60), lastWarnedAt: null, Now));
        Assert.True(BackupHealth.ShouldWarn(weeklyEnabled: true, lastBackup: Now.AddDays(-21), weeklySince: Now.AddDays(-60), lastWarnedAt: null, Now));
    }

    [Fact]
    public void Counts_from_when_weekly_was_turned_on_if_nothing_has_run_yet()
    {
        Assert.False(BackupHealth.ShouldWarn(weeklyEnabled: true, lastBackup: null, weeklySince: Now.AddDays(-10), lastWarnedAt: null, Now));
        Assert.True(BackupHealth.ShouldWarn(weeklyEnabled: true, lastBackup: null, weeklySince: Now.AddDays(-22), lastWarnedAt: null, Now));
    }

    [Fact]
    public void Warns_at_most_once_a_week_and_never_with_weekly_off()
    {
        Assert.False(BackupHealth.ShouldWarn(weeklyEnabled: true, lastBackup: Now.AddDays(-40), weeklySince: Now.AddDays(-60), lastWarnedAt: Now.AddDays(-3), Now));
        Assert.True(BackupHealth.ShouldWarn(weeklyEnabled: true, lastBackup: Now.AddDays(-40), weeklySince: Now.AddDays(-60), lastWarnedAt: Now.AddDays(-7), Now));
        Assert.False(BackupHealth.ShouldWarn(weeklyEnabled: false, lastBackup: Now.AddDays(-40), weeklySince: null, lastWarnedAt: null, Now));
    }

    [Fact]
    public void The_last_backup_is_the_newer_of_the_recorded_one_and_the_newest_file_in_the_folder()
    {
        Assert.Equal(Now.AddDays(-1), BackupHealth.LastBackup(Now.AddDays(-5), Now.AddDays(-1)));
        Assert.Equal(Now.AddDays(-5), BackupHealth.LastBackup(Now.AddDays(-5), null));
        Assert.Null(BackupHealth.LastBackup(null, null));
    }

    [Fact]
    public void Reads_the_date_from_the_newest_backup_file_name_and_ignores_other_files()
    {
        var newest = BackupRetention.NewestBackupTime(new[]
        {
            "ForgeLink-backup-2026-09-20-0300.flbackup",
            "ForgeLink-backup-2026-09-27-0310.flbackup",
            "holiday.jpg",
            "ForgeLink-backup-2026-13-40-9999.flbackup"
        });

        Assert.Equal(new DateTime(2026, 9, 27, 3, 10, 0), newest);
        Assert.Null(BackupRetention.NewestBackupTime(new[] { "notes.txt" }));
    }
}
