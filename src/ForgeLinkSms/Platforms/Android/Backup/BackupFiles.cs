using Android.Content;
using Android.Provider;
using ForgeLinkSms.Core.Backup;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

internal static class BackupFiles
{
    public const string WeeklyFolderKey = "backup_weekly_folder";
    public const string StatusKey = "backup_last_status";
    public const string PasswordKey = "backup_password";
    public const string MimeType = "application/octet-stream";
    public const string WeeklyWorkName = "forgelink-weekly-backup";
    public static string RestoreWorkDirectory => Path.Combine(FileSystem.CacheDirectory, "restore");

    public static bool HasFolderAccess(Context context, string folder) =>
        context.ContentResolver!.PersistedUriPermissions.Any(p => p.Uri?.ToString() == folder && p.IsWritePermission);

    // The folder's access can be revoked (folder deleted, app data cleared, Drive signed out); keep
    // retrying would fail every week, so the schedule stops and Settings asks for a new folder.
    public static void PauseWeekly(Context context)
    {
        AndroidX.Work.WorkManager.GetInstance(context).CancelUniqueWork(WeeklyWorkName);
        Preferences.Remove(WeeklyFolderKey);
        BackupWorker.SaveStatus(false, "Weekly backups are paused: ForgeLink can no longer open the backup folder. Turn weekly backups on again and choose a folder.");
    }

    public static void EnsureDefaultSmsApp(Context context)
    {
        if (global::Android.Provider.Telephony.Sms.GetDefaultSmsPackage(context) != context.PackageName)
        {
            throw new BackupException("Restoring needs ForgeLink to be your default SMS app. Set it as default, then try again.");
        }
    }

    // Leftovers from an interrupted restore can be as large as the backup itself.
    public static void PrepareRestoreWorkDirectory(Context context, AndroidUri backup)
    {
        var directory = RestoreWorkDirectory;
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
            }
        }
        Directory.CreateDirectory(directory);
        long size = -1;
        using (var cursor = context.ContentResolver!.Query(backup, new[] { IOpenableColumns.Size }, null, null, null))
        {
            if (cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0))
            {
                size = cursor.GetLong(0);
            }
        }
        var free = new global::Android.OS.StatFs(directory).AvailableBytes;
        if (size > 0 && free < size + 50L * 1024 * 1024)
        {
            throw new BackupException($"Not enough free space to read this backup: it needs about {size / 1048576.0 / 1024:0.0} GB free on the phone.");
        }
    }

    public static AndroidUri CreateInFolder(Context context, AndroidUri tree, string fileName)
    {
        var parent = DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!;
        return DocumentsContract.CreateDocument(context.ContentResolver!, parent, MimeType, fileName)
            ?? throw new IOException("Could not create the backup file in the chosen folder.");
    }

    public static void DeleteOldWeekly(Context context, AndroidUri tree)
    {
        var treeId = DocumentsContract.GetTreeDocumentId(tree);
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, treeId)!;
        var byName = new Dictionary<string, string>();
        using (var cursor = context.ContentResolver!.Query(children, new[] { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName }, null, null, null))
        {
            while (cursor is not null && cursor.MoveToNext())
            {
                byName[cursor.GetString(1) ?? string.Empty] = cursor.GetString(0)!;
            }
        }
        foreach (var name in BackupRetention.FilesToDelete(byName.Keys))
        {
            DocumentsContract.DeleteDocument(context.ContentResolver!, DocumentsContract.BuildDocumentUriUsingTree(tree, byName[name])!);
        }
    }

    public static void TryDelete(Context context, AndroidUri uri)
    {
        try
        {
            DocumentsContract.DeleteDocument(context.ContentResolver!, uri);
        }
        catch (Exception)
        {
        }
    }

    public static string DisplayName(Context context, AndroidUri uri)
    {
        using var cursor = context.ContentResolver!.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) ?? "backup" : "backup";
    }
}
