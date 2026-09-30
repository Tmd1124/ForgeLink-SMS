using System.Text.Json;
using Android.Content;
using AndroidX.Work;
using ForgeLinkSms.Core.Backup;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// Input "uri" = a file from the Save-as picker; without it, writes a new file into the weekly folder.
public sealed class BackupWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    private bool _foregroundRefused;

    // Expedited work calls this below Android 12 to show its notification before DoWork runs.
    public override ForegroundInfo ForegroundInfo => BackupNotifier.Progress(ApplicationContext, "Backing up messages", 0, 0, Id);

    // Android 12+ refuses to start a foreground service from the background (the weekly job usually is);
    // the backup still runs, just without the progress notification.
    private void TryForeground(ForegroundInfo info)
    {
        if (_foregroundRefused)
        {
            return;
        }
        try
        {
            SetForegroundAsync(info).Get();
        }
        catch (Exception)
        {
            _foregroundRefused = true;
        }
    }

    public override Result DoWork()
    {
        var context = ApplicationContext;
        var targetUri = InputData.GetString("uri");
        var weekly = string.IsNullOrEmpty(targetUri);
        AndroidUri? file = null;
        try
        {
            TryForeground(BackupNotifier.Progress(context, "Backing up messages", 0, 0, Id));
            if (weekly)
            {
                var folder = Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty);
                if (string.IsNullOrEmpty(folder))
                {
                    return Result.InvokeSuccess();
                }
                if (!BackupFiles.HasFolderAccess(context, folder))
                {
                    BackupFiles.PauseWeekly(context);
                    BackupNotifier.Result(context, "Weekly backups paused", "ForgeLink can no longer open the backup folder. Choose it again in Settings.");
                    return Result.InvokeSuccess();
                }
                file = BackupFiles.CreateInFolder(context, AndroidUri.Parse(folder)!, BackupRetention.FileNameFor(DateTime.Now));
            }
            else
            {
                file = AndroidUri.Parse(targetUri)!;
            }

            var password = SecureStorage.GetAsync(BackupFiles.PasswordKey).GetAwaiter().GetResult();
            var source = new AndroidBackupSource(context, MauiApplication.Current.Services);
            var throttle = new ProgressThrottle(TimeSpan.FromSeconds(1));
            var progress = new InlineProgress(p =>
            {
                if (IsStopped)
                {
                    throw new OperationCanceledException();
                }
                if (throttle.ShouldReport(DateTime.UtcNow, p.Done, p.Total))
                {
                    TryForeground(BackupNotifier.Progress(context, "Backing up messages", p.Done, p.Total, Id));
                }
            });
            BackupManifest manifest;
            using (var output = context.ContentResolver!.OpenOutputStream(file, "wt") ?? throw new IOException("Could not open the backup file."))
            {
                manifest = BackupRunner.RunAsync(source, output, password, AppInfo.Current.VersionString, DateTimeOffset.UtcNow,
                    progress, CancellationToken.None).GetAwaiter().GetResult();
            }
            // From here on the new backup is complete and must never be deleted by the failure handling below.
            file = null;
            if (weekly)
            {
                try
                {
                    BackupFiles.DeleteOldWeekly(context, AndroidUri.Parse(Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty))!);
                }
                catch (Exception)
                {
                }
            }
            var summary = $"{manifest.SmsCount + manifest.MmsCount:N0} messages and {manifest.MediaFiles:N0} photos/videos saved.";
            SaveStatus(true, summary);
            BackupNotifier.Result(context, "Backup complete", summary);
            return Result.InvokeSuccess();
        }
        catch (Exception) when (IsStopped)
        {
            CleanUp(context, file, weekly, "Backup cancelled.");
            return Result.InvokeFailure();
        }
        catch (Exception e)
        {
            var reason = e is BackupException ? e.Message : $"Backup failed: {e.Message}";
            CleanUp(context, file, weekly, reason);
            BackupNotifier.Result(context, "Backup didn't finish", reason);
            return Result.InvokeFailure();
        }
    }

    private static void CleanUp(Context context, AndroidUri? file, bool weekly, string reason)
    {
        if (file is not null)
        {
            BackupFiles.TryDelete(context, file);
        }
        SaveStatus(false, reason);
    }

    internal static void SaveStatus(bool ok, string message) =>
        Preferences.Set(BackupFiles.StatusKey, JsonSerializer.Serialize(new StoredStatus(DateTimeOffset.UtcNow, ok, message)));

    internal sealed record StoredStatus(DateTimeOffset When, bool Ok, string Message);
}

// Progress<T> posts reports to another thread, where a cancel exception would crash the app; this
// reports on the job's own thread so throwing OperationCanceledException stops the runner cleanly.
internal sealed class InlineProgress(Action<BackupProgress> report) : IProgress<BackupProgress>
{
    public void Report(BackupProgress value) => report(value);
}
