using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class PinnedRepository : IPinnedRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public PinnedRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<PinnedThread>();

    public Task PinThreadAsync(long threadId) =>
        _db.InsertOrReplaceAsync(new PinnedThread { ThreadId = threadId, PinnedAtUtc = DateTimeOffset.UtcNow });

    public Task UnpinThreadAsync(long threadId) => _db.DeleteAsync<PinnedThread>(threadId);

    public async Task<IReadOnlyList<long>> GetPinnedThreadIdsAsync() =>
        (await _db.Table<PinnedThread>().ToListAsync()).Select(r => r.ThreadId).ToList();

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
