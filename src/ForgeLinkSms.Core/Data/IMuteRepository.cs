namespace ForgeLinkSms.Core.Data;

public interface IMuteRepository
{
    Task InitializeAsync();

    /// untilUtc null mutes until unmuted.
    Task MuteAsync(long threadId, DateTimeOffset? untilUtc);

    Task UnmuteAsync(long threadId);
    Task<bool> IsMutedAsync(long threadId, DateTimeOffset now);
    Task<IReadOnlySet<long>> GetMutedThreadIdsAsync(DateTimeOffset now);

    Task<IReadOnlyList<Models.MutedThread>> GetAllAsync();
}
