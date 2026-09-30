using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class MuteRepository : IMuteRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public MuteRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<MutedThread>();

    public Task MuteAsync(long threadId, DateTimeOffset? untilUtc) =>
        _db.InsertOrReplaceAsync(new MutedThread { ThreadId = threadId, UntilUtc = untilUtc });

    public Task UnmuteAsync(long threadId) => _db.DeleteAsync<MutedThread>(threadId);

    public async Task<bool> IsMutedAsync(long threadId, DateTimeOffset now) =>
        await _db.FindAsync<MutedThread>(threadId).ConfigureAwait(false) is { } mute && IsActive(mute, now);

    public async Task<IReadOnlySet<long>> GetMutedThreadIdsAsync(DateTimeOffset now) =>
        (await _db.Table<MutedThread>().ToListAsync().ConfigureAwait(false))
            .Where(m => IsActive(m, now))
            .Select(m => m.ThreadId)
            .ToHashSet();

    public async Task<IReadOnlyList<MutedThread>> GetAllAsync() =>
        await _db.Table<MutedThread>().ToListAsync().ConfigureAwait(false);

    private static bool IsActive(MutedThread mute, DateTimeOffset now) => mute.UntilUtc is null || mute.UntilUtc > now;

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
