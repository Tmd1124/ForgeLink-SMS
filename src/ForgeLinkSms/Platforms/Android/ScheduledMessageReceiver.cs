using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class ScheduledMessageReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var id = intent?.GetIntExtra(MessageSchedulerService.ScheduledMessageIdExtra, -1) ?? -1;
        if (id < 0)
        {
            return;
        }

        // The system only keeps this receiver's process alive for a few seconds after OnReceive
        // returns, so async work needs GoAsync's PendingResult to signal when it's actually done.
        var pendingResult = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var services = MauiApplication.Current.Services;
                var repository = services.GetRequiredService<IScheduledMessageRepository>();
                var smsService = services.GetRequiredService<ISmsService>();
                var scheduler = services.GetRequiredService<IMessageSchedulerService>();

                var message = await repository.GetAsync(id);
                if (message is not null)
                {
                    if (message.IsGroup)
                    {
                        await smsService.SendGroupAsync(message.ThreadId, message.Recipients, message.Body, null);
                    }
                    else
                    {
                        await smsService.SendAsync(message.Address, message.Body);
                    }
                    await scheduler.CompleteAsync(id);
                }
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
