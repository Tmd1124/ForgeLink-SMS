using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class GroupNameRepository : IGroupNameRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public GroupNameRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public Task InitializeAsync() => _db.CreateTableAsync<GroupName>();

    public Task SetNameAsync(long threadId, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? _db.DeleteAsync<GroupName>(threadId)
            : _db.InsertOrReplaceAsync(new GroupName { ThreadId = threadId, Name = name.Trim() });

    public async Task<string?> GetNameAsync(long threadId) =>
        (await _db.FindAsync<GroupName>(threadId))?.Name;

    public async Task<IReadOnlyDictionary<long, string>> GetAllAsync() =>
        (await _db.Table<GroupName>().ToListAsync()).ToDictionary(g => g.ThreadId, g => g.Name);

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
