using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using AndroidTelephony = global::Android.Provider.Telephony;

namespace ForgeLinkSms.Platforms.Android;

// Everything that happens after a received SMS or MMS has been written to Android's message
// store, shared so both kinds of message get identical archive/trash/snooze/lane/notification
// behavior.
internal static class IncomingMessagePipeline
{
    public static void OnStored(Context context, long threadId, string senderAddress, string notificationBody, string? groupLabel = null)
    {
        var services = MauiApplication.Current.Services;

        if (threadId != 0L)
        {
            // A reply to an archived/trashed conversation means the user is actively back in
            // touch with it — surface it in the main list again rather than leaving a new,
            // unread message hidden in Archive/Trash where it's easy to miss.
            var archiveRepository = services.GetRequiredService<IArchiveRepository>();
            if (archiveRepository.IsArchivedAsync(threadId).GetAwaiter().GetResult())
            {
                archiveRepository.UnarchiveThreadAsync(threadId).GetAwaiter().GetResult();
            }

            var trashRepository = services.GetRequiredService<ITrashRepository>();
            if (trashRepository.IsTrashedAsync(threadId).GetAwaiter().GetResult())
            {
                trashRepository.RestoreThreadAsync(threadId).GetAwaiter().GetResult();
            }

            // Same idea as email snooze: a new message means the conversation needs attention now.
            services.GetRequiredService<ISnoozeService>().UnsnoozeAsync(threadId).GetAwaiter().GetResult();

            services.GetRequiredService<IIncomingMessageNotifier>().NotifyMessageReceived(threadId);
        }

        if (threadId != 0 && services.GetRequiredService<IMuteRepository>().IsMutedAsync(threadId, DateTimeOffset.UtcNow).GetAwaiter().GetResult())
        {
            return;
        }

        var contact = services.GetRequiredService<IContactService>().LookupAsync(senderAddress).GetAwaiter().GetResult();
        var normalizedAddress = PhoneNumberFormatter.ToComparableDigits(senderAddress);
        var isAllowed = services.GetRequiredService<IAllowedSenderRepository>().IsAllowedAsync(normalizedAddress).GetAwaiter().GetResult();
        var isFavorite = threadId != 0 && services.GetRequiredService<IFavoriteRepository>().IsFavoriteAsync(threadId).GetAwaiter().GetResult();
        var lane = SenderScreening.LaneFor(contact is not null, isFavorite, isAllowed, ThreadHasOutgoing(context, threadId), senderAddress);
        var outcome = NotificationRules.Decide(
            NotificationRules.GroupFor(lane, isFavorite),
            notificationBody,
            services.GetRequiredService<INotificationSettingsStore>().Get(),
            TimeOnly.FromDateTime(DateTime.Now));
        if (outcome == NotifyOutcome.None)
        {
            return;
        }

        var senderName = contact?.DisplayName ?? senderAddress;
        var title = groupLabel is null ? senderName : $"{senderName} · {groupLabel}";
        services.GetRequiredService<INotificationService>().NotifyIncomingMessage(title, ReactionParser.Describe(notificationBody) ?? notificationBody, threadId, senderAddress, outcome == NotifyOutcome.Sound);
    }

    public static bool ThreadHasOutgoing(Context context, long threadId)
    {
        if (threadId == 0L)
        {
            return false;
        }

        using var cursor = context.ContentResolver!.Query(
            AndroidTelephony.Sms.ContentUri!, new[] { "_id" }, "thread_id = ? AND type != 1", new[] { threadId.ToString() }, null);
        if (cursor is not null && cursor.Count > 0)
        {
            return true;
        }

        return MmsReader.QueryAll(context, threadId).Any(m => m.IsOutgoing);
    }
}
