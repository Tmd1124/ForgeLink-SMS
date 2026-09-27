using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public static class PondSelector
{
    public const int MaxBubbles = 8;
    public const int MinBubbles = 3;

    private static readonly string[] Palette =
    {
        "#e11d48", "#7c3aed", "#0ea5e9", "#16a34a", "#f59e0b", "#db2777", "#0d9488", "#6366f1"
    };

    // A pond of one or two bubbles looks broken rather than playful, so below MinBubbles
    // there's no pond at all and the plain list shows instead.
    public static IReadOnlyList<SmsThread> Select(IEnumerable<SmsThread> chats, int max = MaxBubbles)
    {
        var picked = Rank(chats.Where(t => !string.IsNullOrWhiteSpace(t.DisplayName)))
            .Take(max)
            .ToList();
        return picked.Count >= MinBubbles ? picked : Array.Empty<SmsThread>();
    }

    public static IOrderedEnumerable<SmsThread> Rank(IEnumerable<SmsThread> chats) => chats
        .OrderByDescending(t => t.IsFavorite)
        .ThenByDescending(t => t.UnreadCount > 0)
        .ThenByDescending(t => t.LastMessageTimestamp);

    // FNV-1a instead of string.GetHashCode(), which .NET randomizes per process: that would
    // give every person a different color each time the app launches.
    public static string ColorFor(string address)
    {
        uint hash = 2166136261;
        foreach (var c in PhoneNumberFormatter.ToComparableDigits(address))
        {
            hash ^= c;
            hash *= 16777619;
        }
        return Palette[hash % (uint)Palette.Length];
    }
}
