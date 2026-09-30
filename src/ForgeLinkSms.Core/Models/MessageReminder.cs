using SQLite;

namespace ForgeLinkSms.Core.Models;

public class MessageReminder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// ForwardedMessage.KeyFor — one reminder per message.
    [Indexed(Unique = true)]
    public string MessageKey { get; set; } = string.Empty;

    [Indexed]
    public long ThreadId { get; set; }

    public string Address { get; set; } = string.Empty;

    public long MessageId { get; set; }

    public DateTimeOffset MessageTimestamp { get; set; }

    public string Preview { get; set; } = string.Empty;

    public DateTimeOffset RemindAtUtc { get; set; }
}
