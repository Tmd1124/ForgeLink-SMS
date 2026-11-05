using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

// Everyone who qualifies is asked the same way — Play policy forbids asking only happy users.
public static class ReviewPromptRules
{
    public static readonly TimeSpan MinimumUse = TimeSpan.FromDays(7);
    public const int MinimumSent = 20;
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(120);

    public static bool ShouldAsk(ReviewPromptState state, DateTimeOffset now) =>
        !state.Rated
        && EnoughUse(state, now)
        && (state.LastAskedUtc is not { } lastAsked || now - lastAsked >= AskAgainAfter);

    // The slim "Enjoying ForgeLink?" card on the Chats list, for installs Google's own card never reaches
    // (anything not installed from Google Play, such as the copy sold on the website).
    public static bool ShouldShowCard(ReviewPromptState state, DateTimeOffset now) =>
        !state.Rated
        && !state.InstalledFromPlay
        && EnoughUse(state, now)
        && (state.CardDismissedUtc is not { } dismissed || now - dismissed >= AskAgainAfter);

    private static bool EnoughUse(ReviewPromptState state, DateTimeOffset now) =>
        state.FirstUseUtc is { } firstUse
        && now - firstUse >= MinimumUse
        && state.SentCount >= MinimumSent;
}
