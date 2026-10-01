using SQLite;

namespace ForgeLinkSms.Core.Models;

/// A name the person gave a group chat; kept on this phone only.
public class GroupName
{
    [PrimaryKey]
    public long ThreadId { get; set; }

    public string Name { get; set; } = string.Empty;
}
