namespace ForgeLinkSms.Core.Services;

public sealed record BackupStatus(DateTimeOffset? LastRunUtc, bool? LastSucceeded, string? LastMessage, bool WeeklyEnabled, string? WeeklyFolderName, bool HasPassword, bool IsRunning);

public sealed record RestoreFile(string Uri, string FileName, bool IsEncrypted, bool IsSmsBackupXml = false);

public interface IBackupService
{
    BackupStatus GetStatus();

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
