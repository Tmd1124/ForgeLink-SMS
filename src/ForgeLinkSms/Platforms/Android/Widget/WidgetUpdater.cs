using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Android.Widget;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using ForgeLinkSms.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using AndroidColor = Android.Graphics.Color;

namespace ForgeLinkSms.Platforms.Android.Widget;

public static class WidgetUpdater
{
    private const int TallMinHeightDp = 110;

    private static readonly int[] FavoriteSlots = [Resource.Id.fav0, Resource.Id.fav1, Resource.Id.fav2, Resource.Id.fav3, Resource.Id.fav4];
    private static readonly int[] FavoritePhotos = [Resource.Id.fav0_photo, Resource.Id.fav1_photo, Resource.Id.fav2_photo, Resource.Id.fav3_photo, Resource.Id.fav4_photo];
    private static readonly int[] FavoriteBadges = [Resource.Id.fav0_badge, Resource.Id.fav1_badge, Resource.Id.fav2_badge, Resource.Id.fav3_badge, Resource.Id.fav4_badge];
    private static readonly int[] FavoriteNames = [Resource.Id.fav0_name, Resource.Id.fav1_name, Resource.Id.fav2_name, Resource.Id.fav3_name, Resource.Id.fav4_name];
    private static readonly int[] Rows = [Resource.Id.row0, Resource.Id.row1, Resource.Id.row2];
    private static readonly int[] RowNames = [Resource.Id.row0_name, Resource.Id.row1_name, Resource.Id.row2_name];
    private static readonly int[] RowTexts = [Resource.Id.row0_text, Resource.Id.row1_text, Resource.Id.row2_text];
    private static readonly int[] RowTimes = [Resource.Id.row0_when, Resource.Id.row1_when, Resource.Id.row2_when];

    private static readonly object Gate = new();
    private static bool _running;
    private static bool _pending;
    private static Task _current = Task.CompletedTask;

    // Bursts of texts ask for many refreshes; one rebuild runs at a time and one more follows if asked meanwhile.
    // The returned task finishes once the widget shows this request's data, so a broadcast receiver can keep
    // its process alive until then (an app started only for a widget update may otherwise be killed first).
    public static Task RequestUpdate(Context context)
    {
        var app = context.ApplicationContext ?? context;
        lock (Gate)
        {
            if (_running)
            {
                _pending = true;
                return _current;
            }
            _running = true;
            _current = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await UpdateAllAsync(app);
                    }
                    catch (Exception)
                    {
                    }
                    lock (Gate)
                    {
                        if (!_pending)
                        {
                            _running = false;
                            return;
                        }
                        _pending = false;
                    }
                }
            });
            return _current;
        }
    }

    private static async Task UpdateAllAsync(Context context)
    {
        var manager = AppWidgetManager.GetInstance(context)!;
        var ids = manager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(ForgeLinkWidgetProvider))))!;
        if (ids.Length == 0)
        {
            return;
        }

        WidgetSnapshot? snapshot = null;
        var accent = AndroidColor.ParseColor("#2563EB");
        try
        {
            var services = MauiApplication.Current.Services;
            accent = Accent(services);
            // The Chats tab's own loading, so the widget counts exactly what the app shows.
            var chats = services.GetRequiredService<ConversationsViewModel>();
            await chats.LoadCommand.ExecuteAsync(null);
            var showText = services.GetRequiredService<IDisplayStyleService>().GetDisplaySettings().ShowWidgetText;
            snapshot = WidgetContent.Build(chats.Threads.ToList(), showText, DateTime.Today);
        }
        catch (Exception)
        {
        }

        foreach (var id in ids)
        {
            var minHeight = manager.GetAppWidgetOptions(id)?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0;
            manager.UpdateAppWidget(id, Render(context, snapshot, minHeight >= TallMinHeightDp, accent));
        }
    }

    private static AndroidColor Accent(IServiceProvider services)
    {
        try
        {
            return AndroidColor.ParseColor(services.GetRequiredService<IThemeService>().GetAccentColor());
        }
        catch (Exception)
        {
            return AndroidColor.ParseColor("#2563EB");
        }
    }

    private static RemoteViews Render(Context context, WidgetSnapshot? snapshot, bool tall, AndroidColor accent)
    {
        var views = new RemoteViews(context.PackageName, tall ? Resource.Layout.forgelink_widget_tall : Resource.Layout.forgelink_widget_small);
        views.SetOnClickPendingIntent(Resource.Id.widget_header, Open(context, "/conversations", 1));
        views.SetOnClickPendingIntent(Resource.Id.widget_compose, Open(context, "/compose", 2));
        views.SetInt(Resource.Id.widget_compose, "setColorFilter", accent.ToArgb());
        Tint(views, Resource.Id.widget_total, accent);

        if (snapshot is null)
        {
            views.SetViewVisibility(Resource.Id.widget_total, ViewStates.Gone);
            views.SetViewVisibility(Resource.Id.widget_favorites, ViewStates.Gone);
            views.SetViewVisibility(Resource.Id.widget_error, ViewStates.Visible);
            views.SetOnClickPendingIntent(Resource.Id.widget_error, Open(context, "/conversations", 3));
            if (tall)
            {
                foreach (var row in Rows)
                {
                    views.SetViewVisibility(row, ViewStates.Gone);
                }
            }
            return views;
        }

        views.SetTextViewText(Resource.Id.widget_total, snapshot.TotalUnread > 99 ? "99+" : snapshot.TotalUnread.ToString());
        views.SetViewVisibility(Resource.Id.widget_total, snapshot.TotalUnread > 0 ? ViewStates.Visible : ViewStates.Gone);

        views.SetViewVisibility(Resource.Id.widget_empty_favorites, snapshot.Favorites.Count == 0 ? ViewStates.Visible : ViewStates.Gone);
        views.SetViewVisibility(Resource.Id.widget_favorites, snapshot.Favorites.Count == 0 ? ViewStates.Gone : ViewStates.Visible);
        for (var i = 0; i < FavoriteSlots.Length; i++)
        {
            if (i >= snapshot.Favorites.Count)
            {
                views.SetViewVisibility(FavoriteSlots[i], ViewStates.Invisible);
                continue;
            }
            var person = snapshot.Favorites[i];
            views.SetViewVisibility(FavoriteSlots[i], ViewStates.Visible);
            views.SetImageViewBitmap(FavoritePhotos[i], WidgetBitmaps.Circle(context, person.PhotoUri, person.Initials, accent));
            views.SetTextViewText(FavoriteNames[i], person.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? person.Name);
            views.SetTextViewText(FavoriteBadges[i], person.Unread > 99 ? "99+" : person.Unread.ToString());
            views.SetViewVisibility(FavoriteBadges[i], person.Unread > 0 ? ViewStates.Visible : ViewStates.Gone);
            Tint(views, FavoriteBadges[i], accent);
            views.SetContentDescription(FavoriteSlots[i], AccessibleLabelFor(person));
            views.SetOnClickPendingIntent(FavoriteSlots[i], Open(context, ChatRoute(person.ThreadId, person.Address), 100 + i));
        }

        if (tall)
        {
            views.SetViewVisibility(Resource.Id.widget_caught_up, snapshot.RecentUnread.Count == 0 ? ViewStates.Visible : ViewStates.Gone);
            for (var i = 0; i < Rows.Length; i++)
            {
                if (i >= snapshot.RecentUnread.Count)
                {
                    views.SetViewVisibility(Rows[i], ViewStates.Gone);
                    continue;
                }
                var row = snapshot.RecentUnread[i];
                views.SetViewVisibility(Rows[i], ViewStates.Visible);
                views.SetTextViewText(RowNames[i], row.Name);
                views.SetTextViewText(RowTexts[i], row.Text);
                views.SetTextViewText(RowTimes[i], row.When);
                views.SetOnClickPendingIntent(Rows[i], Open(context, ChatRoute(row.ThreadId, row.Address), 200 + i));
            }
        }
        return views;
    }

    private static string AccessibleLabelFor(WidgetPerson person) =>
        person.Unread > 0 ? $"{person.Name}, {person.Unread} unread" : person.Name;

    private static string ChatRoute(long threadId, string address) =>
        $"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}";

    // Each tap target needs its own request code, or Android merges them and every tap opens the last route.
    private static PendingIntent Open(Context context, string route, int requestCode)
    {
        var intent = new Intent(context, typeof(MainActivity));
        // Notifications open MainActivity with thread-id request codes; a distinct action keeps a chat whose
        // id equals one of these codes from replacing where a widget button goes.
        intent.SetAction($"com.forgelink.sms.widget.{requestCode}");
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra("initial_route", route);
        return PendingIntent.GetActivity(context, 0x7A00 + requestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    // Badge tinting needs API 31; older launchers keep the drawable's built-in blue.
    private static void Tint(RemoteViews views, int viewId, AndroidColor accent)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            views.SetColorStateList(viewId, "setBackgroundTintList", ColorStateList.ValueOf(accent));
        }
    }
}
