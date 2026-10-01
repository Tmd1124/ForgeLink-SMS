using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;

namespace ForgeLinkSms.Platforms.Android.Widget;

[BroadcastReceiver(Label = "ForgeLink", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/forgelink_widget_info")]
public class ForgeLinkWidgetProvider : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds) => Refresh(context);

    // GoAsync keeps the process alive until the widget is drawn; without it an app started only for
    // this broadcast can be killed as soon as OnReceive returns.
    private void Refresh(Context? context)
    {
        if (context is null)
        {
            return;
        }
        var pending = GoAsync();
        WidgetUpdater.RequestUpdate(context).ContinueWith(_ => pending?.Finish(), TaskScheduler.Default);
    }

    // Resizing between one and two rows swaps between the small and tall layouts.
    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Bundle? newOptions) => Refresh(context);
}
