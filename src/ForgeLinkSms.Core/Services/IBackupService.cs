namespace ForgeLinkSms.Core.Services;

// LastRunUtc/LastMessage describe the latest backup, export or restore; LastBackupUtc is the latest one that
// actually saved messages (a backup or export), including backups found in the weekly folder.
public sealed record BackupStatus(DateTimeOffset? LastRunUtc, bool? LastSucceeded, string? LastMessage, bool WeeklyEnabled, string? WeeklyFolderName, bool HasPassword, bool IsRunning, DateTimeOffset? LastBackupUtc = null);

public sealed record RestoreFile(string Uri, string FileName, bool IsEncrypted, bool IsSmsBackupXml = false);

public interface IBackupService
{
    BackupStatus GetStatus();

    /// True when it's time to suggest turning on weekly backups (see BackupHealth).
    bool ShouldNudge();
    void DismissNudge();

    /// Checks the weekly folder for the newest backup, and notifies when weekly backups have quietly stopped.
    /// Reads the backup folder, so call it off the UI thread.
    void CheckBackupHealth();

    // False when the user closes the system "Save as" / folder picker without choosing.
    Task<bool> BackUpNowAsync();
    Task<bool> EnableWeeklyAsync();
    void DisableWeekly();

    Task SetPasswordAsync(string? password);

    Task<RestoreFile?> PickRestoreFileAsync();

    /// Saves all texts and media as an SMS Backup & Restore XML file (Save-as picker, then a background job).
    Task<bool> ExportForOtherAppsAsync();

    // Reads the whole backup and counts what a restore would add, writing nothing. Throws BackupException
    // (wrong password, damaged file, newer format) with a message meant for the user.
    Task<ForgeLinkSms.Core.Backup.RestorePreview> PreviewRestoreAsync(RestoreFile file, string? password);
    // includeScheduled re-adds the backup's still-future scheduled texts, which will really be sent.
    Task StartRestoreAsync(RestoreFile file, string? password, bool includeSettings, bool includeScheduled);
}
