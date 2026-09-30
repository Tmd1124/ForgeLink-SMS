namespace ForgeLinkSms.Core.Utils;

// 7726 ("SPAM") is the free reporting short code shared by the major US carriers: they ask for the
// spam text first, then the number it came from.
public static class SpamReport
{
    public const string Number = "7726";

    public static IReadOnlyList<string> MessagesFor(string? spamBody, string senderAddress)
    {
        var body = spamBody?.Trim();
        return new[] { string.IsNullOrEmpty(body) ? "(picture message)" : body, senderAddress };
    }

    public static bool IsReportThread(string address) => PhoneNumberFormatter.ToComparableDigits(address) == Number;
}
