using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public interface IScheduledMessageRepository
{
    Task InitializeAsync();
    Task<int> AddAsync(ScheduledMessage message);
    Task RemoveAsync(int id);
    Task UpdateAsync(ScheduledMessage message);
    Task<ScheduledMessage?> GetAsync(int id);
    Task<IReadOnlyList<ScheduledMessage>> GetAllAsync();
}
