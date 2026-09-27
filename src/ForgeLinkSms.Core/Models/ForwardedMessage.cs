using SQLite;

namespace ForgeLinkSms.Core.Models;

public class ForwardedMessage
{
    // SMS and MMS rows have separate id sequences, so the id alone isn't unique.
    [PrimaryKey]
    public string MessageKey { get; set; } = string.Empty;

    [Indexed]
    public long ThreadId { get; set; }

    public string ForwardedTo { get; set; } = string.Empty;

    public DateTimeOffset ForwardedAtUtc { get; set; }

    public static string KeyFor(SmsMessage message) => (message.IsMms ? "mms:" : "sms:") + message.Id;
}
