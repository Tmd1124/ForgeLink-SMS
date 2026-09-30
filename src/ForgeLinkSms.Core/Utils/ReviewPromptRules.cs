using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Utils;

// Everyone who qualifies is asked the same way — Play policy forbids asking only happy users.
public static class ReviewPromptRules
{
    public static readonly TimeSpan MinimumUse = TimeSpan.FromDays(7);
    public const int MinimumSent = 20;
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(120);

    public static bool ShouldAsk(ReviewPromptState state, DateTimeOffset now) =>
        state.FirstUseUtc is { } firstUse
        && now - firstUse >= MinimumUse
        && state.SentCount >= MinimumSent
        && (state.LastAskedUtc is not { } lastAsked || now - lastAsked >= AskAgainAfter);
}
