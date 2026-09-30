using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var reminderId = intent?.GetIntExtra(ReminderAlarmScheduler.ReminderIdExtra, -1) ?? -1;
        if (reminderId < 0)
        {
            return;
        }

        var pendingResult = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var services = MauiApplication.Current.Services;
                if (await services.GetRequiredService<IReminderService>().FireAsync(reminderId) is not { } reminder)
                {
                    return;
                }

                var threads = await services.GetRequiredService<IThreadService>().GetThreadsAsync();
                var name = threads.FirstOrDefault(t => t.Id == reminder.ThreadId)?.DisplayNameOrAddress ?? reminder.Address;
                services.GetRequiredService<INotificationService>().NotifyReminder(name, reminder);
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
