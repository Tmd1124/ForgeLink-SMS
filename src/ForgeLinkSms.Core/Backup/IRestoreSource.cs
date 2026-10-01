namespace ForgeLinkSms.Core.Backup;

// A backup file being restored: ForgeLink's own .flbackup, or another app's SMS Backup & Restore XML.
public interface IRestoreSource : IDisposable
{
    BackupManifest Manifest { get; }
    AppData AppData { get; }
    // includeMedia false skips unpacking photos/videos (Check backup only needs counts).
    IEnumerable<BackupMessage> ReadMessages(bool includeMedia = true);
    Stream OpenMedia(string name);

    /// Attachments left out because they were too large to import.
    int SkippedAttachments { get; }
}
