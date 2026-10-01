using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public interface IFilterRepository
{
    Task InitializeAsync();
    Task<IReadOnlyList<Filter>> GetAllFiltersAsync();
    Task<Filter> CreateFilterAsync(string name, string colorHex);
    Task RenameFilterAsync(long filterId, string newName);
    Task SetFilterColorAsync(long filterId, string colorHex);
    Task DeleteFilterAsync(long filterId);

    /// Saves the order filters are shown in, first to last.
    Task ReorderFiltersAsync(IReadOnlyList<long> orderedIds);
    Task<IReadOnlyDictionary<long, List<long>>> GetAllAssignmentsAsync();
    Task AssignFilterAsync(long threadId, long filterId);
    Task UnassignFilterAsync(long threadId, long filterId);

    /// Removes every filter assignment for a thread — used when a thread is permanently
    /// deleted, since Android can reuse a thread_id for an unrelated later conversation and a
    /// leftover assignment would otherwise silently apply to it.
    Task RemoveAllAssignmentsForThreadAsync(long threadId);
}
