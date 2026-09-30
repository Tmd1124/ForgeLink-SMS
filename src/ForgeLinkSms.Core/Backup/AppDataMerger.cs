using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Backup;

public sealed record FilterToCreate(string Name, string ColorHex);

public sealed record FilterMember(string FilterName, string Conversation);

public sealed record MergePlan
{
    public IReadOnlyList<string> FavoritesToAdd { get; init; } = [];
    public IReadOnlyList<FilterToCreate> FiltersToCreate { get; init; } = [];
    public IReadOnlyList<FilterMember> MembersToAdd { get; init; } = [];
    public IReadOnlyList<string> QuickRepliesToAdd { get; init; } = [];
    public IReadOnlyList<BackupScheduled> ScheduledToAdd { get; init; } = [];
    public IReadOnlyList<string> ArchiveToApply { get; init; } = [];
    public IReadOnlyList<string> PinsToApply { get; init; } = [];
    public IReadOnlyList<string> TrashToApply { get; init; } = [];
    public IReadOnlyList<BackupTimed> SnoozesToApply { get; init; } = [];
    public IReadOnlyList<BackupTimed> MutesToApply { get; init; } = [];
    public IReadOnlyList<string> BlockedToAdd { get; init; } = [];
    public IReadOnlyList<string> AllowedToAdd { get; init; } = [];
    public IReadOnlyList<BackupDraft> DraftsToAdd { get; init; } = [];
    public BackupSettings? Settings { get; init; }
}

// Restore only adds: nothing already on the phone is removed or overwritten.
public static class AppDataMerger
{
    public static MergePlan Plan(AppData current, AppData backup, IReadOnlySet<string> restoredConversations, bool includeSettings, bool includeScheduled, DateTimeOffset now)
    {
        // The app allows two filters whose names differ only by case; the first one wins.
        var existingFilters = current.Filters.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var filtersToCreate = new List<FilterToCreate>();
        var members = new List<FilterMember>();
        foreach (var filter in backup.Filters)
        {
            existingFilters.TryGetValue(filter.Name, out var existing);
            var name = existing?.Name ?? filter.Name;
            if (existing is null && filtersToCreate.All(f => !f.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase)))
            {
                filtersToCreate.Add(new FilterToCreate(filter.Name, filter.ColorHex));
            }
            var have = existing?.Members.ToHashSet() ?? new HashSet<string>();
            members.AddRange(filter.Members.Where(m => !have.Contains(m)).Distinct().Select(m => new FilterMember(name, m)));
        }

        bool Restored(string conversation) => restoredConversations.Contains(conversation);
        var snoozed = current.Snoozed.Select(s => s.Conversation).ToHashSet();
        var muted = current.Muted.Select(m => m.Conversation).ToHashSet();
        var drafts = current.Drafts.Select(d => d.Conversation).ToHashSet();

        return new MergePlan
        {
            FavoritesToAdd = Missing(backup.Favorites, current.Favorites),
            FiltersToCreate = filtersToCreate,
            MembersToAdd = members,
            QuickRepliesToAdd = Missing(backup.QuickReplies.Select(q => q.Trim()), current.QuickReplies.Select(q => q.Trim())),
            // A re-added scheduled text really gets sent, so it's only restored when the user opts in.
            ScheduledToAdd = !includeScheduled ? [] : backup.Scheduled
                .Where(s => (s.SendAtUtc > now || s.Repeat != ScheduleRepeat.None) && !current.Scheduled.Any(c => SameScheduled(c, s)))
                .Distinct()
                .ToList(),
            ArchiveToApply = Missing(backup.Archived.Where(Restored), current.Archived),
            PinsToApply = Missing(backup.Pinned.Where(Restored), current.Pinned),
            TrashToApply = Missing(backup.Trashed.Where(Restored), current.Trashed),
            SnoozesToApply = backup.Snoozed.Where(s => Restored(s.Conversation) && s.UntilUtc > now && !snoozed.Contains(s.Conversation)).ToList(),
            MutesToApply = backup.Muted.Where(m => Restored(m.Conversation) && (m.UntilUtc is null || m.UntilUtc > now) && !muted.Contains(m.Conversation)).ToList(),
            BlockedToAdd = Missing(backup.Blocked, current.Blocked),
            AllowedToAdd = Missing(backup.Allowed, current.Allowed),
            DraftsToAdd = backup.Drafts.Where(d => Restored(d.Conversation) && !drafts.Contains(d.Conversation)).ToList(),
            Settings = includeSettings ? backup.Settings : null
        };
    }

    // A repeating text's send time moves on after every send, so the series is matched by what it is.
    private static bool SameScheduled(BackupScheduled a, BackupScheduled b) =>
        a.Conversation == b.Conversation && a.Body == b.Body && a.Repeat == b.Repeat
        && (a.Repeat != ScheduleRepeat.None || a.SendAtUtc == b.SendAtUtc);

    private static List<string> Missing(IEnumerable<string> wanted, IEnumerable<string> have)
    {
        var existing = have.ToHashSet(StringComparer.Ordinal);
        return wanted.Where(w => w.Length > 0 && !existing.Contains(w)).Distinct().ToList();
    }
}
