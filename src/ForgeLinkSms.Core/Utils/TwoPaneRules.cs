namespace ForgeLinkSms.Core.Utils;

public static class TwoPaneRules
{
    public const double MinWidthPx = 600;

    public static bool ShowsTwoPanes(double widthPx, string relativeUri) =>
        widthPx >= MinWidthPx && PathOf(relativeUri) is "conversations" or "conversations/thread";

    public static bool IsChat(string relativeUri) => PathOf(relativeUri) == "conversations/thread";

    public static long? OpenThreadId(string relativeUri)
    {
        if (!IsChat(relativeUri))
        {
            return null;
        }
        var query = relativeUri.Contains('?') ? relativeUri[(relativeUri.IndexOf('?') + 1)..] : string.Empty;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts[0] == "id" && parts.Length == 2 && long.TryParse(parts[1], out var id))
            {
                return id;
            }
        }
        return null;
    }

    private static string PathOf(string relativeUri) => relativeUri.Split('?')[0].Trim('/');
}
