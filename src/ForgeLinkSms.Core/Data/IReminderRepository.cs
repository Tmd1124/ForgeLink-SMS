using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Data;

public interface IReminderRepository
{
    Task InitializeAsync();
    Task SaveAsync(MessageReminder reminder);
    Task RemoveAsync(int id);
    Task<MessageReminder?> GetAsync(int id);
    Task<MessageReminder?> GetByMessageKeyAsync(string messageKey);
    Task<IReadOnlyList<MessageReminder>> GetAllAsync();
}
