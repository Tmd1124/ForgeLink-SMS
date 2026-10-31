using SQLite;

namespace ForgeLinkSms.Core.Models;

public class Filter
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    /// Stored as "hidden" so filters made before this existed (null in the database) stay active.
    public bool IsHidden { get; set; }

    /// A small square picture chosen from the phone, shown beside the name; null when the filter has none.
    public string? IconDataUri { get; set; }

    /// Where the filter sits in the person's chosen order; ties (filters made before ordering existed) fall back to creation order.
    public int SortOrder { get; set; }
}
