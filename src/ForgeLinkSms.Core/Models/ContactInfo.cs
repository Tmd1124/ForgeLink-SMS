namespace ForgeLinkSms.Core.Models;

public class ContactInfo
{
    public required string DisplayName { get; init; }
    public required string PhoneNumber { get; init; }
    public string? PhotoUri { get; init; }

    // Defaults true: most ContactInfo instances (e.g. resolving an existing conversation's
    // display name/photo) have no phone-number-type lookup behind them and shouldn't be
    // restricted just because that information wasn't available.
    public bool CanReceiveText { get; init; } = true;

    public string Initials
    {
        get
        {
            var words = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
