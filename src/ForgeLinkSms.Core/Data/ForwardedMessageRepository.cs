using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class ForwardedMessageRepository : IForwardedMessageRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public ForwardedMessageRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<ForwardedMessage>();

    public Task RecordAsync(long threadId, string messageKey, string forwardedTo) =>
        _db.InsertOrReplaceAsync(new ForwardedMessage
        {
            MessageKey = messageKey,
            ThreadId = threadId,
            ForwardedTo = forwardedTo,
            ForwardedAtUtc = DateTimeOffset.UtcNow
        });

    public async Task<IReadOnlyDictionary<string, string>> GetForThreadAsync(long threadId) =>
        (await _db.Table<ForwardedMessage>().Where(f => f.ThreadId == threadId).ToListAsync().ConfigureAwait(false))
            .ToDictionary(f => f.MessageKey, f => f.ForwardedTo);

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
