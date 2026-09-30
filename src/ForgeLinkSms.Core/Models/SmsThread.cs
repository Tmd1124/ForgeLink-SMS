namespace ForgeLinkSms.Core.Models;

public class SmsThread
{
    public required long Id { get; init; }
    public required string Address { get; init; }
    public required string? DisplayName { get; init; }
    public required string LastMessageBody { get; init; }
    public required DateTimeOffset LastMessageTimestamp { get; init; }
    public required int UnreadCount { get; set; }
    public string? PhotoUri { get; init; }
    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }
    public bool HasOutgoing { get; init; }
    public IReadOnlyList<string> Participants { get; init; } = Array.Empty<string>();
    public string? DraftText { get; set; }
    public bool IsMuted { get; set; }
    public bool IsGroup => Participants.Count > 1;
    public IReadOnlyList<long> FilterIds { get; set; } = Array.Empty<long>();

    public string DisplayNameOrAddress => string.IsNullOrWhiteSpace(DisplayName) ? Address : DisplayName;

    /// The last message as the list shows it; a reaction reads as "❤️ to “…”" rather than "Loved “…”".
    public string LastMessageText => Utils.ReactionParser.Describe(LastMessageBody) ?? LastMessageBody;

    public string PreviewText => LastMessageText.Length > 60
        ? LastMessageText[..60] + "…"
        : LastMessageText;

    public string Initials
    {
        get
        {
            var words = DisplayNameOrAddress.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var letters = words.Take(2).Select(FirstAlphanumericChar).Where(c => c is not null).Select(c => c!.Value);
            return string.Concat(letters).ToUpperInvariant();
        }
    }

    private static char? FirstAlphanumericChar(string word)
    {
        foreach (var c in word)
        {
            if (char.IsLetterOrDigit(c))
            {
                return c;
            }
        }
        return null;
    }
}
