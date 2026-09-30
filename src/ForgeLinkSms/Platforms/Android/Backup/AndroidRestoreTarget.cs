using Android.Content;
using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using AndroidTelephony = Android.Provider.Telephony;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// Writes to the SMS/MMS provider, which only the default SMS app may do.
internal sealed class AndroidRestoreTarget(Context context, IServiceProvider services) : IRestoreTarget
{
    private const int PduFrom = 137;
    private const int PduTo = 151;
    private const int MessageTypeSendReq = 128;
    private const int MessageTypeRetrieveConf = 132;

    public Task<IEnumerable<ExistingMessage>> ReadExistingMessagesAsync()
    {
        var source = new AndroidBackupSource(context, services);
        source.ReadAppDataAsync().GetAwaiter().GetResult();
        var existing = source.ReadMessages()
            .Select(m => new ExistingMessage(ConversationKey.From(m.Message.Addresses), m.Message.TimestampMs, m.Message.Outgoing, m.Message.Body, m.Attachments.Count));
        return Task.FromResult(existing);
    }

    public async Task<AppData> ReadAppDataAsync() => (await AppDataSnapshot.ReadAsync(services)).Data;

    public Task InsertSmsAsync(BackupMessage message)
    {
        var values = new ContentValues();
        values.Put("address", message.From ?? message.Addresses.FirstOrDefault() ?? string.Empty);
        if (message.Addresses.Count > 1)
        {
            // Without an explicit thread the provider files the row under a 1-to-1 thread with its address.
            values.Put("thread_id", AndroidTelephony.Threads.GetOrCreateThreadId(context, message.Addresses.ToHashSet()));
        }
        values.Put("body", message.Body ?? string.Empty);
        values.Put("date", message.TimestampMs);
        values.Put("date_sent", message.DateSentMs);
        values.Put("type", message.Outgoing ? 2 : 1);
        values.Put("read", message.Read ? 1 : 0);
        values.Put("seen", 1);
        values.Put("status", message.Status);
        context.ContentResolver!.Insert(AndroidTelephony.Sms.ContentUri!, values);
        return Task.CompletedTask;
    }

    public Task InsertMmsAsync(BackupMessage message, Func<string, Stream> openMedia)
    {
        var resolver = context.ContentResolver!;
        var threadId = AndroidTelephony.Threads.GetOrCreateThreadId(context, message.Addresses.ToHashSet());
        var values = new ContentValues();
        values.Put("thread_id", threadId);
        values.Put("date", message.TimestampMs / 1000);
        values.Put("date_sent", message.DateSentMs / 1000);
        values.Put("msg_box", message.Outgoing ? 2 : 1);
        values.Put("read", message.Read ? 1 : 0);
        values.Put("seen", 1);
        values.Put("m_type", message.Outgoing ? MessageTypeSendReq : MessageTypeRetrieveConf);
        values.Put("ct_t", "application/vnd.wap.multipart.related");
        if (!string.IsNullOrEmpty(message.Subject))
        {
            values.Put("sub", message.Subject);
        }
        var mmsUri = resolver.Insert(AndroidUri.Parse("content://mms")!, values) ?? throw new IOException("The message store refused a picture message.");
        var mmsId = mmsUri.LastPathSegment!;

        void AddAddress(string address, int type)
        {
            var addr = new ContentValues();
            addr.Put("address", address);
            addr.Put("type", type);
            addr.Put("charset", 106);
            resolver.Insert(AndroidUri.Parse($"content://mms/{mmsId}/addr")!, addr);
        }
        if (message.Outgoing)
        {
            AddAddress("insert-address-token", PduFrom);
            foreach (var to in message.Addresses)
            {
                AddAddress(to, PduTo);
            }
        }
        else
        {
            var from = message.From ?? message.Addresses.FirstOrDefault() ?? string.Empty;
            AddAddress(from, PduFrom);
            foreach (var to in message.Addresses.Where(a => a != from))
            {
                AddAddress(to, PduTo);
            }
        }

        var partsUri = AndroidUri.Parse($"content://mms/{mmsId}/part")!;
        if (!string.IsNullOrEmpty(message.Body))
        {
            var text = new ContentValues();
            text.Put("mid", mmsId);
            text.Put("ct", "text/plain");
            text.Put("chset", 106);
            text.Put("cl", "text.txt");
            text.Put("text", message.Body);
            resolver.Insert(partsUri, text);
        }
        foreach (var attachment in message.Attachments)
        {
            var part = new ContentValues();
            part.Put("mid", mmsId);
            part.Put("ct", attachment.ContentType);
            var name = attachment.FileName ?? Path.GetFileName(attachment.Media);
            part.Put("cl", name);
            part.Put("name", name);
            var partUri = resolver.Insert(partsUri, part) ?? throw new IOException("The message store refused an attachment.");
            using var output = resolver.OpenOutputStream(partUri) ?? throw new IOException("Could not write an attachment.");
            using var input = openMedia(attachment.Media);
            input.CopyTo(output);
        }
        return Task.CompletedTask;
    }

    public async Task ApplyAsync(MergePlan plan)
    {
        var (_, _, threadOf) = await AppDataSnapshot.ReadAsync(services);
        long ThreadFor(string conversation) => threadOf.TryGetValue(conversation, out var id)
            ? id
            : AndroidTelephony.Threads.GetOrCreateThreadId(context, conversation.Split(',').ToHashSet());

        foreach (var conversation in plan.FavoritesToAdd)
        {
            await services.GetRequiredService<IFavoriteRepository>().FavoriteThreadAsync(ThreadFor(conversation));
        }

        var filterRepo = services.GetRequiredService<IFilterRepository>();
        foreach (var filter in plan.FiltersToCreate)
        {
            await filterRepo.CreateFilterAsync(filter.Name, filter.ColorHex);
        }
        var filtersByName = (await filterRepo.GetAllFiltersAsync()).GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        foreach (var member in plan.MembersToAdd)
        {
            if (filtersByName.TryGetValue(member.FilterName, out var filterId))
            {
                await filterRepo.AssignFilterAsync(ThreadFor(member.Conversation), filterId);
            }
        }

        var quickReplies = services.GetRequiredService<IQuickReplyRepository>();
        foreach (var text in plan.QuickRepliesToAdd)
        {
            await quickReplies.AddAsync(text);
        }

        var scheduler = services.GetRequiredService<IMessageSchedulerService>();
        foreach (var s in plan.ScheduledToAdd)
        {
            var from = s.RepeatFromUtc ?? s.SendAtUtc;
            // A repeating series whose last time has passed restarts at its next future time instead of sending now.
            var sendAt = s.SendAtUtc > DateTimeOffset.UtcNow ? s.SendAtUtc
                : ScheduleRepeatRules.Next(from, s.Repeat, DateTimeOffset.UtcNow, TimeZoneInfo.Local) ?? s.SendAtUtc;
            int id;
            if (string.IsNullOrEmpty(s.GroupAddresses))
            {
                id = await scheduler.ScheduleAsync(s.Address, s.Body, sendAt, s.Repeat);
            }
            else
            {
                id = await scheduler.ScheduleGroupAsync(ThreadFor(s.Conversation), s.GroupAddresses.Split(','), s.Body, sendAt, s.Repeat);
            }
            if (s.Repeat != ScheduleRepeat.None && sendAt != from)
            {
                var repository = services.GetRequiredService<IScheduledMessageRepository>();
                if (await repository.GetAsync(id) is { } row)
                {
                    row.RepeatFromUtc = from;
                    await repository.UpdateAsync(row);
                }
            }
        }

        foreach (var conversation in plan.PinsToApply)
        {
            await services.GetRequiredService<IPinnedRepository>().PinThreadAsync(ThreadFor(conversation));
        }
        foreach (var conversation in plan.ArchiveToApply)
        {
            await services.GetRequiredService<IArchiveRepository>().ArchiveThreadAsync(ThreadFor(conversation));
        }
        foreach (var conversation in plan.TrashToApply)
        {
            await services.GetRequiredService<ITrashRepository>().TrashThreadAsync(ThreadFor(conversation));
        }
        foreach (var snooze in plan.SnoozesToApply)
        {
            await services.GetRequiredService<ISnoozeService>().SnoozeAsync(ThreadFor(snooze.Conversation), snooze.UntilUtc!.Value);
        }
        foreach (var mute in plan.MutesToApply)
        {
            await services.GetRequiredService<IMuteRepository>().MuteAsync(ThreadFor(mute.Conversation), mute.UntilUtc);
        }
        foreach (var number in plan.BlockedToAdd)
        {
            await services.GetRequiredService<IContactBlockService>().BlockAsync(number);
        }
        foreach (var address in plan.AllowedToAdd)
        {
            await services.GetRequiredService<IAllowedSenderRepository>().AllowAsync(address);
        }
        foreach (var draft in plan.DraftsToAdd)
        {
            await services.GetRequiredService<IDraftRepository>().SaveAsync(ThreadFor(draft.Conversation), draft.Text);
        }

        if (plan.Settings is { } settings)
        {
            services.GetRequiredService<IDisplayStyleService>().SaveDisplaySettings(settings.Display);
            services.GetRequiredService<INotificationSettingsStore>().Save(settings.Notifications);
            var theme = services.GetRequiredService<IThemeService>();
            if (Enum.TryParse<ThemeMode>(settings.ThemeMode, out var mode))
            {
                theme.SetThemeMode(mode);
            }
            theme.SetAccentColor(settings.AccentColor);
        }
    }
}
