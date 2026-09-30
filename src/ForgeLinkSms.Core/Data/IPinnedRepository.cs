namespace ForgeLinkSms.Core.Data;

public interface IPinnedRepository
{
    Task InitializeAsync();
    Task PinThreadAsync(long threadId);
    Task UnpinThreadAsync(long threadId);
    Task<IReadOnlyList<long>> GetPinnedThreadIdsAsync();
}
