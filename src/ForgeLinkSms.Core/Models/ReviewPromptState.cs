namespace ForgeLinkSms.Core.Models;

public record ReviewPromptState
{
    public DateTimeOffset? FirstUseUtc { get; init; }
    public int SentCount { get; init; }
    public DateTimeOffset? LastAskedUtc { get; init; }
}
