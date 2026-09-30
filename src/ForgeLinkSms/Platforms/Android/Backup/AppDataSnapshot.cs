using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeLinkSms.Platforms.Android.Backup;

// ForgeLink data is stored by Android thread id; backups store it by conversation (participants).
internal static class AppDataSnapshot
{
    public static async Task<(AppData Data, IReadOnlyDictionary<long, IReadOnlyList<string>> Participants, IReadOnlyDictionary<string, long> ThreadOf)> ReadAsync(IServiceProvider services)
    {
        var threads = await services.GetRequiredService<IThreadService>().GetThreadsAsync();
        var participants = threads.ToDictionary(t => t.Id, t => (IReadOnlyList<string>)(t.Participants.Count > 0 ? t.Participants : new[] { t.Address }));
        var keyOf = participants.ToDictionary(p => p.Key, p => ConversationKey.From(p.Value));
        var threadOf = new Dictionary<string, long>();
        foreach (var (id, key) in keyOf)
        {
            threadOf.TryAdd(key, id);
        }
        string? Key(long threadId) => keyOf.TryGetValue(threadId, out var k) ? k : null;
        List<string> Keys(IEnumerable<long> ids) => ids.Select(Key).OfType<string>().Distinct().ToList();

        var filterRepo = services.GetRequiredService<IFilterRepository>();
        var filters = await filterRepo.GetAllFiltersAsync();
        var assignments = await filterRepo.GetAllAssignmentsAsync();
        var scheduled = await services.GetRequiredService<IScheduledMessageRepository>().GetAllAsync();
        var snoozed = await services.GetRequiredService<ISnoozeRepository>().GetAllAsync();
        var muted = await services.GetRequiredService<IMuteRepository>().GetAllAsync();
        var drafts = await services.GetRequiredService<IDraftRepository>().GetAllAsync();
        var theme = services.GetRequiredService<IThemeService>();

        var data = new AppData
        {
            Favorites = Keys(await services.GetRequiredService<IFavoriteRepository>().GetFavoriteThreadIdsAsync()),
            Filters = filters.Select(f => new BackupFilter(f.Name, f.ColorHex,
                Keys(assignments.Where(a => a.Value.Contains(f.Id)).Select(a => a.Key)))).ToList(),
            QuickReplies = (await services.GetRequiredService<IQuickReplyRepository>().GetAllAsync()).OrderBy(q => q.SortOrder).Select(q => q.Text).ToList(),
            Scheduled = scheduled.Select(s => new BackupScheduled(
                Key(s.ThreadId) ?? ConversationKey.From(string.IsNullOrEmpty(s.GroupAddresses) ? new[] { s.Address } : s.GroupAddresses.Split(',')),
                s.Address, s.Body, s.SendAtUtc, s.GroupAddresses)).ToList(),
            Archived = Keys(await services.GetRequiredService<IArchiveRepository>().GetArchivedThreadIdsAsync()),
            Trashed = Keys(await services.GetRequiredService<ITrashRepository>().GetTrashedThreadIdsAsync()),
            Snoozed = snoozed.Where(s => Key(s.ThreadId) is not null).Select(s => new BackupTimed(Key(s.ThreadId)!, s.UntilUtc)).ToList(),
            Muted = muted.Where(m => Key(m.ThreadId) is not null).Select(m => new BackupTimed(Key(m.ThreadId)!, m.UntilUtc)).ToList(),
            Blocked = (await services.GetRequiredService<IContactBlockService>().GetBlockedNumbersAsync()).ToList(),
            Allowed = (await services.GetRequiredService<IAllowedSenderRepository>().GetAllAsync()).ToList(),
            Drafts = drafts.Where(d => Key(d.Key) is not null && !string.IsNullOrWhiteSpace(d.Value)).Select(d => new BackupDraft(Key(d.Key)!, d.Value)).ToList(),
            Settings = new BackupSettings(
                services.GetRequiredService<IDisplayStyleService>().GetDisplaySettings(),
                services.GetRequiredService<INotificationSettingsStore>().Get(),
                theme.GetThemeMode().ToString(),
                theme.GetAccentColor())
        };
        return (data, participants, threadOf);
    }
}
