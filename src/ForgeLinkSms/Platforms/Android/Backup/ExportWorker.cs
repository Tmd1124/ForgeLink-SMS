using Android.Content;
using AndroidX.Work;
using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// Writes an SMS Backup & Restore XML file to the "uri" chosen in the Save-as picker.
public sealed class ExportWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    private const string Title = "Exporting messages";
    private bool _foregroundRefused;

    public override ForegroundInfo ForegroundInfo => BackupNotifier.Progress(ApplicationContext, Title, 0, 0, Id);

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
        var file = AndroidUri.Parse(InputData.GetString("uri"))!;
        try
        {
            TryForeground(BackupNotifier.Progress(context, Title, 0, 0, Id));
            var services = MauiApplication.Current.Services;
            var source = new AndroidBackupSource(context, services);
            var names = new ContactNames(services.GetRequiredService<IContactService>());
            var throttle = new ProgressThrottle(TimeSpan.FromSeconds(1));
            var progress = new InlineProgress(p =>
            {
                if (IsStopped)
                {
                    throw new OperationCanceledException();
                }
                if (throttle.ShouldReport(DateTime.UtcNow, p.Done, p.Total))
                {
                    TryForeground(BackupNotifier.Progress(context, Title, p.Done, p.Total, Id));
                }
            });
            ExportResult result;
            using (var output = context.ContentResolver!.OpenOutputStream(file, "wt") ?? throw new IOException("Could not open the export file."))
            {
                result = SmsBackupXmlWriter.RunAsync(source, output, DateTimeOffset.UtcNow, names.For, progress, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            var summary = $"Exported {result.Sms + result.Mms:N0} messages and {result.MediaFiles:N0} photos/videos for other apps.";
            BackupWorker.SaveStatus(true, summary);
            BackupNotifier.Result(context, "Export complete", summary);
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            BackupFiles.TryDelete(context, file);
            var reason = IsStopped ? "Export cancelled." : e is BackupException ? e.Message : $"Export failed: {e.Message}";
            BackupWorker.SaveStatus(false, reason);
            if (!IsStopped)
            {
                BackupNotifier.Result(context, "Export didn't finish", reason);
            }
            return Result.InvokeFailure();
        }
    }

    // SMS Backup & Restore shows a contact name per message; looking each address up once keeps a
    // large export from querying contacts thousands of times.
    private sealed class ContactNames(IContactService contacts)
    {
        private readonly Dictionary<string, string?> _cache = new();

        public string? For(IReadOnlyList<string> addresses)
        {
            var names = addresses.Select(Name).Where(n => n is not null).ToList();
            return names.Count == 0 ? null : string.Join(", ", names);
        }

        private string? Name(string address)
        {
            if (!_cache.TryGetValue(address, out var name))
            {
                name = contacts.LookupAsync(address).GetAwaiter().GetResult()?.DisplayName;
                _cache[address] = name;
            }
            return name;
        }
    }
}
