using Android.Content;
using AndroidX.Work;
using ForgeLinkSms.Core.Backup;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

public sealed class RestoreWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    public const string PendingPasswordKey = "restore_pending_password";
    private bool _foregroundRefused;

    public override ForegroundInfo ForegroundInfo => BackupNotifier.Progress(ApplicationContext, "Restoring messages", 0, 0, Id);

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
        var uri = AndroidUri.Parse(InputData.GetString("uri"))!;
        var includeSettings = InputData.GetBoolean("settings", false);
        var includeScheduled = InputData.GetBoolean("scheduled", false);
        var isSmsBackupXml = InputData.GetBoolean("xml", false);
        try
        {
            TryForeground(BackupNotifier.Progress(context, "Restoring messages", 0, 0, Id));
            var password = SecureStorage.GetAsync(PendingPasswordKey).GetAwaiter().GetResult();
            SecureStorage.Remove(PendingPasswordKey);
            BackupFiles.EnsureDefaultSmsApp(context);
            BackupFiles.PrepareRestoreWorkDirectory(context, uri, needsFullCopy: !isSmsBackupXml);
            using var reader = BackupService.OpenRestoreSource(context, uri, isSmsBackupXml, password);
            var target = new AndroidRestoreTarget(context, MauiApplication.Current.Services);
            var throttle = new ProgressThrottle(TimeSpan.FromSeconds(1));
            var progress = new InlineProgress(p =>
            {
                if (IsStopped)
                {
                    throw new OperationCanceledException();
                }
                if (throttle.ShouldReport(DateTime.UtcNow, p.Done, p.Total))
                {
                    TryForeground(BackupNotifier.Progress(context, "Restoring messages", p.Done, p.Total, Id));
                }
            });
            var result = RestoreRunner.RunAsync(reader, target, includeSettings, includeScheduled, DateTimeOffset.UtcNow, progress, CancellationToken.None).GetAwaiter().GetResult();
            var summary = $"Added {result.Added:N0} messages. {result.Skipped:N0} were already on this phone."
                + (result.MediaTooLarge > 0 ? $" {result.MediaTooLarge:N0} attachment{(result.MediaTooLarge == 1 ? " was" : "s were")} too large to import." : "");
            BackupWorker.SaveStatus(true, "Restore: " + summary);
            BackupNotifier.Result(context, "Restore complete", summary);
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            SecureStorage.Remove(PendingPasswordKey);
            var reason = e is BackupException ? e.Message : IsStopped ? "Restore cancelled. Running it again is safe." : $"Restore failed: {e.Message}";
            BackupWorker.SaveStatus(false, reason);
            BackupNotifier.Result(context, "Restore didn't finish", reason);
            return Result.InvokeFailure();
        }
    }
}
