using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class ScheduledMessageRepository : IScheduledMessageRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public ScheduledMessageRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<ScheduledMessage>();

    public async Task<int> AddAsync(ScheduledMessage message)
    {
        await _db.InsertAsync(message);
        return message.Id;
    }

    public Task RemoveAsync(int id) => _db.DeleteAsync<ScheduledMessage>(id);

    public Task UpdateAsync(ScheduledMessage message) => _db.UpdateAsync(message);

    public async Task<ScheduledMessage?> GetAsync(int id) => await _db.FindAsync<ScheduledMessage>(id);

    public async Task<IReadOnlyList<ScheduledMessage>> GetAllAsync() =>
        await _db.Table<ScheduledMessage>().OrderBy(m => m.SendAtUtc).ToListAsync();

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
