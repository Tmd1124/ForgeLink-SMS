namespace ForgeLinkSms.Core.Data;

public interface IGroupNameRepository
{
    Task InitializeAsync();

    /// A blank name removes it, so the group goes back to showing its members.
    Task SetNameAsync(long threadId, string? name);

    Task<string?> GetNameAsync(long threadId);

    Task<IReadOnlyDictionary<long, string>> GetAllAsync();
}
