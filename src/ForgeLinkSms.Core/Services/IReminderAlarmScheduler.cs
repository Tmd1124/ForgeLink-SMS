namespace ForgeLinkSms.Core.Services;

public interface IReminderAlarmScheduler
{
    void Arm(int reminderId, DateTimeOffset remindAtUtc);
    void Disarm(int reminderId);
}
