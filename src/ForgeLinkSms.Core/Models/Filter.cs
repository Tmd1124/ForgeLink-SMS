using SQLite;

namespace ForgeLinkSms.Core.Models;

public class Filter
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    /// Where the filter sits in the person's chosen order; ties (filters made before ordering existed) fall back to creation order.
    public int SortOrder { get; set; }
}
