using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density, WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : MauiAppCompatActivity
{
    private NavigationHistoryTracker? _historyTracker;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CapturePendingRoute(Intent);
        ApplyImeInsetPadding();
        _historyTracker = MauiApplication.Current.Services.GetRequiredService<NavigationHistoryTracker>();
        // With predictive back (enabled in the manifest) Android 13+ delivers back through the
        // back dispatcher, so the tracked-history navigation is registered there.
        OnBackPressedDispatcher.AddCallback(this, new HistoryBackCallback(this));
    }

    private void NavigateBack()
    {
        if (!_historyTracker!.TryConsumeLocalBack() && !_historyTracker.TryGoBack())
        {
            Finish();
        }
    }

    private sealed class HistoryBackCallback(MainActivity activity) : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed() => activity.NavigateBack();
    }

    // BlazorWebView's own Android WebView subclass handles the hardware back key itself: it consumes
    // KEYCODE_BACK on the ACTION_DOWN event directly inside its own OnKeyDown override, calling the
    // native WebView.GoBack()/CanGoBack() browser history right there — confirmed via live logcat
    // capture (the WebView never sets FLAG_TRACKING on the down event, and the dispatcher/onBackPressed
    // path never runs, yet navigation still happens). That native history was confirmed to sometimes
    // pop more than one entry per press, landing the app back on the splash screen instead of one
    // screen back (reproduced consistently after long-pressing a message to open the reaction picker,
    // then pressing back). Overriding DispatchKeyEvent lets us claim the ACTION_DOWN event before it
    // ever reaches the WebView, so we drive back-navigation from our own tracked stack
    // (NavigationHistoryTracker) instead of the WebView's browser history. Below Android 13 the
    // navigation happens here; from 13 on, predictive back routes the press to HistoryBackCallback.
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is { KeyCode: Keycode.Back })
        {
            // On Android 13+ the same press also reaches HistoryBackCallback, so navigating here
            // too would go back twice. The key is still swallowed to keep it from the WebView.
            if (e.Action == KeyEventActions.Down && !OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                NavigateBack();
            }
            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    // Apps targeting API 35+ get edge-to-edge enforced, which makes windowSoftInputMode=
    // "adjustResize" a no-op — the window no longer shrinks when the keyboard appears, so a
    // bottom-pinned input in a height:100vh layout stays covered. Google's documented replacement
    // is to read the IME inset via WindowInsetsCompat and pad a view by that amount ourselves,
    // which is what adjustResize used to do automatically. The listener has to go on the
    // DecorView specifically — attaching it to the content view (FindViewById(Android.Resource.
    // Id.Content), one level down) silently never fires, because AppCompat/MAUI's own edge-to-edge
    // handling consumes the insets dispatch before it reaches that view; DecorView is first in
    // line and gets them unconsumed (confirmed via logcat: the listener never ran on the content
    // view, but fires reliably on DecorView with the correct IME height).
    private void ApplyImeInsetPadding()
    {
        var decorView = Window!.DecorView;
        ViewCompat.SetOnApplyWindowInsetsListener(decorView, new ImeInsetListener());
        ViewCompat.RequestApplyInsets(decorView);
    }

    private sealed class ImeInsetListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(global::Android.Views.View v, WindowInsetsCompat insets)
        {
            var imeBottom = insets.GetInsets(WindowInsetsCompat.Type.Ime()).Bottom;
            var navBarBottom = insets.GetInsets(WindowInsetsCompat.Type.SystemBars()).Bottom;
            v.SetPadding(v.PaddingLeft, v.PaddingTop, v.PaddingRight, Math.Max(imeBottom - navBarBottom, 0));
            return insets;
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        CapturePendingRoute(intent);
    }

    // Fires whenever this activity comes back to the foreground — including returning from
    // the native Add Contact screen launched by the conversations list's "+" avatar button —
    // so pages can refresh data that may have changed while we were away.
    protected override void OnResume()
    {
        base.OnResume();
        MauiApplication.Current.Services.GetRequiredService<IAppResumeNotifier>().NotifyResumed();
    }

    // Notification taps and ACTION_SENDTO hand-offs (ComposeSmsActivity) launch this Activity with
    // an "initial_route" extra. MainActivity and the BlazorWebView's NavigationManager are in
    // different DI scopes, so the route is staged here and picked up by SplashPage on the circuit
    // that always runs first on a fresh BlazorWebView load.
    private static void CapturePendingRoute(Intent? intent)
    {
        var route = intent?.GetStringExtra("initial_route");
        if (!string.IsNullOrEmpty(route))
        {
            MauiApplication.Current.Services.GetRequiredService<PendingNavigationStore>().SetPendingRoute(route);
        }
    }
}
