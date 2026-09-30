using SQLite;

namespace ForgeLinkSms.Core.Models;

public class PinnedThread
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public DateTimeOffset PinnedAtUtc { get; set; }
}
