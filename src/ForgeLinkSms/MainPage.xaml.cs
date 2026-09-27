namespace ForgeLinkSms;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
		ErrorDetailsLoggerProvider.ShowDetails = details => MainThread.BeginInvokeOnMainThread(() =>
		{
#if ANDROID
			var script = $"window.forgeLinkRecordError && window.forgeLinkRecordError({System.Text.Json.JsonSerializer.Serialize(details)})";
			(blazorWebView.Handler?.PlatformView as Android.Webkit.WebView)?.EvaluateJavascript(script, null);
#endif
		});
	}
}
