using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public class ReminderService : IReminderService
{
    private const int PreviewLength = 120;

    private readonly IReminderRepository _repository;
    private readonly IReminderAlarmScheduler _alarms;

    public ReminderService(IReminderRepository repository, IReminderAlarmScheduler alarms)
    {
        _repository = repository;
        _alarms = alarms;
    }

    public async Task<MessageReminder> SetAsync(SmsMessage message, DateTimeOffset remindAtUtc)
    {
        var key = ForwardedMessage.KeyFor(message);
        var reminder = await _repository.GetByMessageKeyAsync(key) ?? new MessageReminder { MessageKey = key };
        reminder.ThreadId = message.ThreadId;
        reminder.Address = message.Address;
        reminder.MessageId = message.Id;
        reminder.MessageTimestamp = message.Timestamp;
        reminder.Preview = PreviewOf(message);
        reminder.RemindAtUtc = remindAtUtc;
        await _repository.SaveAsync(reminder);
        _alarms.Arm(reminder.Id, remindAtUtc);
        return reminder;
    }

    public async Task CancelAsync(int reminderId)
    {
        _alarms.Disarm(reminderId);
        await _repository.RemoveAsync(reminderId);
    }

    public async Task<MessageReminder?> FireAsync(int reminderId)
    {
        var reminder = await _repository.GetAsync(reminderId);
        if (reminder is not null)
        {
            await _repository.RemoveAsync(reminderId);
        }
        return reminder;
    }

    public Task<IReadOnlyList<MessageReminder>> GetAllAsync() => _repository.GetAllAsync();

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetForThreadAsync(long threadId) =>
        (await _repository.GetAllAsync()).Where(r => r.ThreadId == threadId).ToDictionary(r => r.MessageKey, r => r.RemindAtUtc);

    public async Task RearmAllAsync()
    {
        foreach (var reminder in await _repository.GetAllAsync())
        {
            _alarms.Arm(reminder.Id, reminder.RemindAtUtc);
        }
    }

    private static string PreviewOf(SmsMessage message)
    {
        var body = message.Body.Trim();
        if (body.Length == 0)
        {
            return message.Attachments.Count > 0 ? "📎 Attachment" : string.Empty;
        }
        return body.Length > PreviewLength ? body[..PreviewLength] + "…" : body;
    }
}
