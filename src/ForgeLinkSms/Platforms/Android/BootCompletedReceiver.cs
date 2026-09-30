using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

// Android clears every AlarmManager alarm on reboot, so any scheduled sends, snoozes and reminders need to be re-armed
// once the device comes back up, or they'd silently never fire.
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootCompletedReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var pendingResult = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var services = MauiApplication.Current.Services;
                await services.GetRequiredService<IMessageSchedulerService>().RescheduleAllPendingAsync();
                await services.GetRequiredService<ISnoozeService>().RearmAllAsync();
                await services.GetRequiredService<IReminderService>().RearmAllAsync();
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
