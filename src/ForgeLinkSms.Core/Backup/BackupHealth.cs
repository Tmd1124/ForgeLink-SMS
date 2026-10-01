namespace ForgeLinkSms.Core.Backup;

// When to remind the person that their messages aren't being backed up.
public static class BackupHealth
{
    public static readonly TimeSpan NudgeAfter = TimeSpan.FromDays(14);
    public static readonly TimeSpan NudgeSnooze = TimeSpan.FromDays(30);
    public static readonly TimeSpan WarnAfter = TimeSpan.FromDays(21);
    public static readonly TimeSpan WarnRepeat = TimeSpan.FromDays(7);

    public static bool ShouldNudge(DateTimeOffset? lastBackup, bool weeklyEnabled, DateTimeOffset? dismissedAt, DateTimeOffset now) =>
        !weeklyEnabled
        && (lastBackup is null || now - lastBackup >= NudgeAfter)
        && (dismissedAt is null || now - dismissedAt >= NudgeSnooze);

    // Weekly backups only run while charging and can lose their folder; three silent weeks means something is wrong.
    public static bool ShouldWarn(bool weeklyEnabled, DateTimeOffset? lastBackup, DateTimeOffset? weeklySince, DateTimeOffset? lastWarnedAt, DateTimeOffset now) =>
        weeklyEnabled
        && (lastBackup ?? weeklySince) is { } reference
        && now - reference >= WarnAfter
        && (lastWarnedAt is null || now - lastWarnedAt >= WarnRepeat);

    public static DateTimeOffset? LastBackup(DateTimeOffset? recorded, DateTimeOffset? newestInFolder) =>
        recorded is null ? newestInFolder : newestInFolder is null ? recorded : recorded > newestInFolder ? recorded : newestInFolder;
}
