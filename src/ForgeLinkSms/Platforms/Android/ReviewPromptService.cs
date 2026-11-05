using Android.Content;
using Android.Gms.Tasks;
using Google.Android.Play.Core.Review;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using AndroidUri = Android.Net.Uri;
using GmsTask = Android.Gms.Tasks.Task;

namespace ForgeLinkSms.Platforms.Android;

// Google decides whether the rating card actually appears (and never for sideloaded builds), and
// never says what was rated — so an ask is recorded as soon as it's requested.
public class ReviewPromptService : IReviewPromptService
{
    private const string FirstUseKey = "review_first_use_utc";
    private const string SentCountKey = "review_sent_count";
    private const string LastAskedKey = "review_last_asked_utc";
    private const string RatedKey = "review_rated";
    private const string CardDismissedKey = "review_card_dismissed_utc";
    private const string StoreId = "com.forgelink.sms";

    private bool _sentSinceLastCheck;
    private bool _ratingCardDue;

    public ReviewPromptService()
    {
        if (!Preferences.ContainsKey(FirstUseKey))
        {
            Preferences.Set(FirstUseKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
    }

    public void RecordSent()
    {
        Preferences.Set(SentCountKey, Preferences.Get(SentCountKey, 0) + 1);
        _sentSinceLastCheck = true;
    }

    public System.Threading.Tasks.Task MaybeAskAsync()
    {
        if (!_sentSinceLastCheck)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
        _sentSinceLastCheck = false;

        var now = DateTimeOffset.UtcNow;
        var state = ReadState();
        _ratingCardDue = ReviewPromptRules.ShouldShowCard(state, now);
        if (!ReviewPromptRules.ShouldAsk(state, now) || Platform.CurrentActivity is not { } activity)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
        Preferences.Set(LastAskedKey, now.ToUnixTimeSeconds());

        var manager = ReviewManagerFactory.Create(activity);
        manager.RequestReviewFlow().AddOnCompleteListener(new CompletionListener(request =>
        {
            if (request.IsSuccessful && request.Result is ReviewInfo info)
            {
                manager.LaunchReviewFlow(activity, info);
            }
        }));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public void OpenStoreListing()
    {
        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        var market = new Intent(Intent.ActionView, AndroidUri.Parse($"market://details?id={StoreId}"));
        market.AddFlags(ActivityFlags.NewTask);
        try
        {
            context.StartActivity(market);
        }
        catch (ActivityNotFoundException)
        {
            var web = new Intent(Intent.ActionView, AndroidUri.Parse($"https://play.google.com/store/apps/details?id={StoreId}"));
            web.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(web);
        }
    }

    // Due once per qualifying check (right after a message was sent), so the card shows at a calm moment
    // instead of every time the list opens.
    public bool ShouldShowRatingCard()
    {
        var due = _ratingCardDue;
        _ratingCardDue = false;
        return due;
    }

    public void DismissRatingCard() => Preferences.Set(CardDismissedKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public void MarkRated() => Preferences.Set(RatedKey, true);

    private static ReviewPromptState ReadState() => new()
    {
        FirstUseUtc = Preferences.ContainsKey(FirstUseKey) ? DateTimeOffset.FromUnixTimeSeconds(Preferences.Get(FirstUseKey, 0L)) : null,
        SentCount = Preferences.Get(SentCountKey, 0),
        LastAskedUtc = Preferences.ContainsKey(LastAskedKey) ? DateTimeOffset.FromUnixTimeSeconds(Preferences.Get(LastAskedKey, 0L)) : null,
        Rated = Preferences.Get(RatedKey, false),
        InstalledFromPlay = IsInstalledFromPlay(),
        CardDismissedUtc = Preferences.ContainsKey(CardDismissedKey) ? DateTimeOffset.FromUnixTimeSeconds(Preferences.Get(CardDismissedKey, 0L)) : null
    };

    private static bool IsInstalledFromPlay()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var manager = context.PackageManager;
            var package = context.PackageName;
            if (manager is null || package is null)
            {
                return false;
            }
            var installer = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? manager.GetInstallSourceInfo(package).InstallingPackageName
                : manager.GetInstallerPackageName(package);
            return installer == "com.android.vending";
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class CompletionListener(Action<GmsTask> callback) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(GmsTask task) => callback(task);
    }
}
