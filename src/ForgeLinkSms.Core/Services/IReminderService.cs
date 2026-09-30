using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IReminderService
{
    /// Sets or moves the reminder for this message.
    Task<MessageReminder> SetAsync(SmsMessage message, DateTimeOffset remindAtUtc);
    Task CancelAsync(int reminderId);

    /// Removes and returns the reminder when its alarm fires; null if it was cancelled meanwhile.
    Task<MessageReminder?> FireAsync(int reminderId);
    Task<IReadOnlyList<MessageReminder>> GetAllAsync();

    /// Pending reminder times in one chat, keyed by ForwardedMessage.KeyFor.
    Task<IReadOnlyDictionary<string, DateTimeOffset>> GetForThreadAsync(long threadId);
    Task RearmAllAsync();
}
