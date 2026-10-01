using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Help;

/// The company behind ForgeLink, shown at the bottom of every help panel.
public static class AzureForge
{
    public const string Company = "AzureForge AI";
    public const string Website = "https://AzureForgeAI.com";
    public const string WebsiteLabel = "AzureForgeAI.com";
    public const string Email = "Tmd1124@AzureForgeAI.com";

    public static ReportEmail RatingEmail(int stars, string? comment, AppDetails app)
    {
        var count = Math.Clamp(stars, 1, 5);
        var shown = new string('★', count) + new string('☆', 5 - count);
        var body = $"I rate ForgeLink SMS {count} out of 5 stars."
            + (string.IsNullOrWhiteSpace(comment) ? "" : $"\n\n{comment.Trim()}")
            + $"\n\n---\nApp: ForgeLink SMS {app.Version}\nPhone: {app.Phone}, {app.AndroidVersion}";
        return new ReportEmail($"ForgeLink SMS rating: {shown}", body, Email);
    }
}
