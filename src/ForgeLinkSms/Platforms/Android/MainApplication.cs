using Android.App;
using Android.Runtime;

[assembly: UsesPermission(Android.Manifest.Permission.ReadSms)]
[assembly: UsesPermission(Android.Manifest.Permission.SendSms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveSms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveMms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadContacts)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadPhoneState)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadPhoneNumbers)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveWapPush)]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission(Android.Manifest.Permission.AccessFineLocation)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessCoarseLocation)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveBootCompleted)]
[assembly: UsesPermission(Android.Manifest.Permission.RecordAudio)]

namespace ForgeLinkSms;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
		// Saved so the next launch can offer to email a report; the crash itself still happens as usual.
		AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Platforms.Android.CrashReportService.Save(e.Exception);
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			if (e.ExceptionObject is Exception exception)
			{
				Platforms.Android.CrashReportService.Save(exception);
			}
		};
		// Crashes raised in Java code never reach the .NET handlers above.
		Java.Lang.Thread.DefaultUncaughtExceptionHandler = new CrashSavingHandler(Java.Lang.Thread.DefaultUncaughtExceptionHandler);
	}

	private sealed class CrashSavingHandler(Java.Lang.Thread.IUncaughtExceptionHandler? next) : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
	{
		public void UncaughtException(Java.Lang.Thread thread, Java.Lang.Throwable exception)
		{
			Platforms.Android.CrashReportService.Save(exception);
			next?.UncaughtException(thread, exception);
		}
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
