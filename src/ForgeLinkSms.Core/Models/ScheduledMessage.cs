using SQLite;

namespace ForgeLinkSms.Core.Models;

public class ScheduledMessage
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Address { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset SendAtUtc { get; set; }

    /// The conversation a group message belongs to; 0 lets the sender find or create it.
    public long ThreadId { get; set; }

    /// Comma-separated recipients of a group message; empty for a plain one-to-one text.
    public string GroupAddresses { get; set; } = string.Empty;

    public ScheduleRepeat Repeat { get; set; }

    /// The first send time of a repeating text; later times are counted from it. Null for older rows.
    public DateTimeOffset? RepeatFromUtc { get; set; }

    [Ignore]
    public bool IsGroup => !string.IsNullOrEmpty(GroupAddresses);

    [Ignore]
    public IReadOnlyList<string> Recipients => IsGroup
        ? GroupAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries)
        : new[] { Address };
}
