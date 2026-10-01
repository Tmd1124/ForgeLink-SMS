using SQLite;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public class FilterRepository : IFilterRepository, IDisposable
{
    private readonly SQLiteAsyncConnection _db;

    public FilterRepository(string databasePath)
    {
        _db = new SQLiteAsyncConnection(databasePath);
    }

    public async Task InitializeAsync()
    {
        // ConfigureAwait(false) is required here specifically: MauiProgram.cs calls this
        // synchronously via .GetAwaiter().GetResult() on Android's main thread during app
        // startup. Without it, the second await's continuation would need to resume on that
        // same main thread's captured SynchronizationContext — which is unavailable because
        // that thread is blocked waiting on this very method, deadlocking app startup into an
        // ANR ("failed to complete startup"). None of this repository's other methods are ever
        // called this way (they're always awaited normally from a ViewModel), so they don't
        // need it.
        await _db.CreateTableAsync<Filter>().ConfigureAwait(false);
        await _db.CreateTableAsync<ThreadFilterAssignment>().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Filter>> GetAllFiltersAsync() =>
        await _db.Table<Filter>().OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync();

    public async Task<Filter> CreateFilterAsync(string name, string colorHex)
    {
        var last = await _db.ExecuteScalarAsync<int>("SELECT IFNULL(MAX(SortOrder), 0) FROM Filter");
        var filter = new Filter { Name = name, ColorHex = colorHex, SortOrder = last + 1 };
        await _db.InsertAsync(filter);
        return filter;
    }

    public Task RenameFilterAsync(long filterId, string newName) =>
        _db.ExecuteAsync("UPDATE Filter SET Name = ? WHERE Id = ?", newName, filterId);

    public Task SetFilterColorAsync(long filterId, string colorHex) =>
        _db.ExecuteAsync("UPDATE Filter SET ColorHex = ? WHERE Id = ?", colorHex, filterId);

    public Task ReorderFiltersAsync(IReadOnlyList<long> orderedIds) =>
        _db.RunInTransactionAsync(connection =>
        {
            for (var i = 0; i < orderedIds.Count; i++)
            {
                connection.Execute("UPDATE Filter SET SortOrder = ? WHERE Id = ?", i + 1, orderedIds[i]);
            }
        });

    public async Task DeleteFilterAsync(long filterId)
    {
        // ConfigureAwait(false) as a standing precaution: this method isn't called synchronously
        // today, but InitializeAsync's identical shape without it deadlocked app startup once
        // already (see the comment there) — cheap insurance against the same call pattern being
        // introduced here later.
        await _db.ExecuteAsync("DELETE FROM ThreadFilterAssignment WHERE FilterId = ?", filterId).ConfigureAwait(false);
        await _db.DeleteAsync<Filter>(filterId).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<long, List<long>>> GetAllAssignmentsAsync()
    {
        var rows = await _db.Table<ThreadFilterAssignment>().ToListAsync();
        return rows
            .GroupBy(r => r.ThreadId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.FilterId).ToList());
    }

    public async Task AssignFilterAsync(long threadId, long filterId)
    {
        // ConfigureAwait(false) as a standing precaution — see DeleteFilterAsync.
        var existing = await _db.Table<ThreadFilterAssignment>()
            .Where(a => a.ThreadId == threadId && a.FilterId == filterId)
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (existing is null)
        {
            await _db.InsertAsync(new ThreadFilterAssignment { ThreadId = threadId, FilterId = filterId }).ConfigureAwait(false);
        }
    }

    public Task UnassignFilterAsync(long threadId, long filterId) =>
        _db.ExecuteAsync("DELETE FROM ThreadFilterAssignment WHERE ThreadId = ? AND FilterId = ?", threadId, filterId);

    public Task RemoveAllAssignmentsForThreadAsync(long threadId) =>
        _db.ExecuteAsync("DELETE FROM ThreadFilterAssignment WHERE ThreadId = ?", threadId);

    public void Dispose()
    {
        _db.CloseAsync().GetAwaiter().GetResult();
    }
}
