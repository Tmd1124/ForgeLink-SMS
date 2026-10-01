using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

public enum SearchKind { All, Photos, Links }

public enum SearchPeriod { AnyTime, PastWeek, PastMonth, PastYear }

// Narrows message search results by what they contain, how old they are, and which chat they're in.
public static class SearchFilters
{
    public static IEnumerable<SmsMessage> Apply(IEnumerable<SmsMessage> messages, SearchKind kind, SearchPeriod period, long? chatId, DateTimeOffset now)
    {
        var since = period switch
        {
            SearchPeriod.PastWeek => now.AddDays(-7),
            SearchPeriod.PastMonth => now.AddMonths(-1),
            SearchPeriod.PastYear => now.AddYears(-1),
            _ => DateTimeOffset.MinValue
        };
        return messages.Where(m => m.Timestamp >= since
            && (chatId is null || m.ThreadId == chatId)
            && kind switch
            {
                SearchKind.Photos => IsPhoto(m),
                SearchKind.Links => HasLink(m),
                _ => true
            });
    }

    public static bool IsPhoto(SmsMessage message) =>
        message.Attachments.Any(a => a.Kind is AttachmentKind.Image or AttachmentKind.Gif or AttachmentKind.Video);

    public static bool HasLink(SmsMessage message) =>
        MessageLinkParser.Parse(message.Body).Any(segment => segment.Kind == MessageLinkKind.Url);

    public static string Label(SearchKind kind) => kind switch
    {
        SearchKind.Photos => "📷 Photos",
        SearchKind.Links => "🔗 Links",
        _ => "All"
    };

    public static string Label(SearchPeriod period) => period switch
    {
        SearchPeriod.PastWeek => "Past week",
        SearchPeriod.PastMonth => "Past month",
        SearchPeriod.PastYear => "Past year",
        _ => "Any time"
    };
}
