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
    private const string StoreId = "com.forgelink.sms";

    private bool _sentSinceLastCheck;

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
        if (!ReviewPromptRules.ShouldAsk(ReadState(), now) || Platform.CurrentActivity is not { } activity)
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

    private static ReviewPromptState ReadState() => new()
    {
        FirstUseUtc = Preferences.ContainsKey(FirstUseKey) ? DateTimeOffset.FromUnixTimeSeconds(Preferences.Get(FirstUseKey, 0L)) : null,
        SentCount = Preferences.Get(SentCountKey, 0),
        LastAskedUtc = Preferences.ContainsKey(LastAskedKey) ? DateTimeOffset.FromUnixTimeSeconds(Preferences.Get(LastAskedKey, 0L)) : null
    };

    private sealed class CompletionListener(Action<GmsTask> callback) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(GmsTask task) => callback(task);
    }
}
