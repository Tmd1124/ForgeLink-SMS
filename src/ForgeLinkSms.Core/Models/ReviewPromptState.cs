namespace ForgeLinkSms.Core.Models;

public record ReviewPromptState
{
    public DateTimeOffset? FirstUseUtc { get; init; }
    public int SentCount { get; init; }
    public DateTimeOffset? LastAskedUtc { get; init; }

    /// The person already sent a rating from the app.
    public bool Rated { get; init; }

    /// Installed from Google Play, which shows its own rating card.
    public bool InstalledFromPlay { get; init; }

    public DateTimeOffset? CardDismissedUtc { get; init; }
}
