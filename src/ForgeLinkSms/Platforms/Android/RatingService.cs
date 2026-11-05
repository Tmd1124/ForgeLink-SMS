using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

public class RatingService(RatingClient client, IReviewPromptService prompts) : IRatingService
{
    private const string InstallIdKey = "rating_install_id";

    // Random, made on first use; identifies one install so a person can't rate twice. Not a device id.
    internal static string InstallId()
    {
        var id = Preferences.Get(InstallIdKey, "");
        if (id.Length == 0)
        {
            id = Guid.NewGuid().ToString();
            Preferences.Set(InstallIdKey, id);
        }
        return id;
    }

    public async Task<RatingSendOutcome> SendAsync(int stars, string? comment, string? name)
    {
        var version = $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
        var outcome = await client.SendAsync(InstallId(), version, stars, comment, name);
        if (outcome.Result != RatingSendResult.Failed)
        {
            prompts.MarkRated();
        }
        return outcome;
    }
}
