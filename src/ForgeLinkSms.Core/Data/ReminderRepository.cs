using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class ReminderRepository : IReminderRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public ReminderRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<MessageReminder>();

    public Task SaveAsync(MessageReminder reminder) =>
        reminder.Id == 0 ? _db.InsertAsync(reminder) : _db.UpdateAsync(reminder);

    public Task RemoveAsync(int id) => _db.DeleteAsync<MessageReminder>(id);

    public async Task<MessageReminder?> GetAsync(int id) => await _db.FindAsync<MessageReminder>(id);

    public async Task<MessageReminder?> GetByMessageKeyAsync(string messageKey) =>
        await _db.Table<MessageReminder>().Where(r => r.MessageKey == messageKey).FirstOrDefaultAsync();

    public async Task<IReadOnlyList<MessageReminder>> GetAllAsync() =>
        await _db.Table<MessageReminder>().OrderBy(r => r.RemindAtUtc).ToListAsync();

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
