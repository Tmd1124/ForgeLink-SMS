namespace ForgeLinkSms.Core.Services;

public interface IRatingService
{
    /// Sends the rating to the CloudCompass AI website. On success the app stops asking for a rating.
    Task<RatingSendOutcome> SendAsync(int stars, string? comment, string? name);
}
