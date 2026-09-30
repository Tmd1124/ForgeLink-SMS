using System.Text.Json;
using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Services;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

public sealed class BackupService : IBackupService
{
    private const string ManualWorkName = "forgelink-backup";
    private const string RestoreWorkName = "forgelink-restore";
    private static Context Context => global::Android.App.Application.Context;

    public BackupStatus GetStatus()
    {
        BackupWorker.StoredStatus? last = null;
        try
        {
            var json = Preferences.Get(BackupFiles.StatusKey, string.Empty);
            last = json.Length > 0 ? JsonSerializer.Deserialize<BackupWorker.StoredStatus>(json) : null;
        }
        catch (JsonException)
        {
        }
        var folder = Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty);
        var hasPassword = !string.IsNullOrEmpty(SecureStorage.GetAsync(BackupFiles.PasswordKey).GetAwaiter().GetResult());
        return new BackupStatus(last?.When, last?.Ok, last?.Message, folder.Length > 0,
            folder.Length > 0 ? BackupFiles.DisplayName(Context, DocumentsTreeRoot(folder)) : null, hasPassword, IsRunning());
    }

    public async Task<bool> BackUpNowAsync()
    {
        var folder = Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty);
        if (folder.Length > 0 && !BackupFiles.HasFolderAccess(Context, folder))
        {
            BackupFiles.PauseWeekly(Context);
            folder = string.Empty;
        }
        var data = new Data.Builder();
        if (folder.Length == 0)
        {
            var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType(BackupFiles.MimeType);
            intent.PutExtra(Intent.ExtraTitle, BackupRetention.FileNameFor(DateTime.Now));
            var uri = await ActivityResultBridge.StartAsync(intent);
            if (uri is null)
            {
                return false;
            }
            data.PutString("uri", uri.ToString());
        }
        Enqueue<BackupWorker>(ManualWorkName, data.Build());
        return true;
    }

    public async Task<bool> EnableWeeklyAsync()
    {
        var uri = await ActivityResultBridge.StartAsync(new Intent(Intent.ActionOpenDocumentTree));
        if (uri is null)
        {
            return false;
        }
        Context.ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        Preferences.Set(BackupFiles.WeeklyFolderKey, uri.ToString());
        var constraints = new Constraints.Builder().SetRequiresCharging(true).SetRequiresBatteryNotLow(true).Build();
        var request = (PeriodicWorkRequest)new PeriodicWorkRequest.Builder(Java.Lang.Class.FromType(typeof(BackupWorker)), 7, Java.Util.Concurrent.TimeUnit.Days!)
            .SetConstraints(constraints)
            .Build();
        WorkManager.GetInstance(Context).EnqueueUniquePeriodicWork(BackupFiles.WeeklyWorkName, ExistingPeriodicWorkPolicy.Update!, request);
        return true;
    }

    public void DisableWeekly()
    {
        WorkManager.GetInstance(Context).CancelUniqueWork(BackupFiles.WeeklyWorkName);
        var folder = Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty);
        if (folder.Length > 0)
        {
            try
            {
                Context.ContentResolver!.ReleasePersistableUriPermission(AndroidUri.Parse(folder)!, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            }
            catch (Java.Lang.SecurityException)
            {
            }
        }
        Preferences.Remove(BackupFiles.WeeklyFolderKey);
    }

    public async Task SetPasswordAsync(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            SecureStorage.Remove(BackupFiles.PasswordKey);
        }
        else
        {
            await SecureStorage.SetAsync(BackupFiles.PasswordKey, password);
        }
    }

    public async Task<RestoreFile?> PickRestoreFileAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        var uri = await ActivityResultBridge.StartAsync(intent);
        if (uri is null)
        {
            return null;
        }
        var header = new byte[4];
        using (var input = Context.ContentResolver!.OpenInputStream(uri))
        {
            var read = input?.Read(header, 0, 4) ?? 0;
            header = header[..Math.Max(read, 0)];
        }
        return new RestoreFile(uri.ToString()!, BackupFiles.DisplayName(Context, uri), BackupCrypto.IsEncrypted(header));
    }

    public Task<RestorePreview> PreviewRestoreAsync(RestoreFile file, string? password) => Task.Run(async () =>
    {
        var uri = AndroidUri.Parse(file.Uri)!;
        BackupFiles.EnsureDefaultSmsApp(Context);
        BackupFiles.PrepareRestoreWorkDirectory(Context, uri);
        using var input = Context.ContentResolver!.OpenInputStream(uri) ?? throw new BackupDamagedException();
        using var reader = BackupReader.Open(input, string.IsNullOrEmpty(password) ? null : password, BackupFiles.RestoreWorkDirectory);
        return await RestoreRunner.PreviewAsync(reader, new AndroidRestoreTarget(Context, MauiApplication.Current.Services), CancellationToken.None);
    });

    public async Task StartRestoreAsync(RestoreFile file, string? password, bool includeSettings, bool includeScheduled)
    {
        SecureStorage.Remove(RestoreWorker.PendingPasswordKey);
        if (!string.IsNullOrEmpty(password))
        {
            await SecureStorage.SetAsync(RestoreWorker.PendingPasswordKey, password);
        }
        var data = new Data.Builder().PutString("uri", file.Uri).PutBoolean("settings", includeSettings).PutBoolean("scheduled", includeScheduled).Build();
        Enqueue<RestoreWorker>(RestoreWorkName, data);
    }

    private static void Enqueue<TWorker>(string name, Data data) where TWorker : Worker
    {
        var request = (OneTimeWorkRequest)new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(TWorker)))
            .SetInputData(data)
            .SetExpedited(OutOfQuotaPolicy.RunAsNonExpeditedWorkRequest!)
            .Build();
        WorkManager.GetInstance(Context).EnqueueUniqueWork(name, ExistingWorkPolicy.Keep!, request);
    }

    // The future's result is a java.util.List surfaced as a plain Java object, so it's read through the Java list API.
    private static bool IsRunning()
    {
        foreach (var name in new[] { ManualWorkName, RestoreWorkName })
        {
            var infos = WorkManager.GetInstance(Context).GetWorkInfosForUniqueWork(name).Get()!.JavaCast<Java.Util.IList>()!;
            for (var i = 0; i < infos.Size(); i++)
            {
                var state = infos.Get(i)!.JavaCast<WorkInfo>()!.GetState();
                if (state == WorkInfo.State.Running || state == WorkInfo.State.Enqueued)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static AndroidUri DocumentsTreeRoot(string tree) =>
        global::Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(AndroidUri.Parse(tree)!, global::Android.Provider.DocumentsContract.GetTreeDocumentId(AndroidUri.Parse(tree)!))!;
}
