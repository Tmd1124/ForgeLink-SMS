using Android.Content;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using AndroidApp = Android.App.Application;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

public class CrashReportService : ICrashReportService
{
    private static string CrashFile => Path.Combine(FileSystem.AppDataDirectory, "last-crash.txt");

    // Called from the crash handlers, so it must never throw and keeps the work minimal.
    public static void Save(Exception exception)
    {
        try
        {
            File.WriteAllText(CrashFile, $"{DateTimeOffset.Now:O}\n{ErrorReport.Format(exception)}");
        }
        catch (Exception)
        {
        }
    }

    public string? PendingCrash()
    {
        try
        {
            return File.Exists(CrashFile) ? File.ReadAllText(CrashFile) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void ClearPendingCrash()
    {
        try
        {
            File.Delete(CrashFile);
        }
        catch (Exception)
        {
        }
    }

    public void EmailCrashReport(string details)
    {
        // The saved file starts with the crash time on its own line.
        var newline = details.IndexOf('\n');
        if (newline > 0 && DateTimeOffset.TryParse(details[..newline], out var when))
        {
            Open(CrashReport.ForCrash(Details(), when, details[(newline + 1)..]));
        }
        else
        {
            Open(CrashReport.ForCrash(Details(), DateTimeOffset.Now, details));
        }
    }

    public void EmailFeedback() => Open(CrashReport.ForFeedback(Details()));

    private static AppDetails Details() =>
        new($"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})",
            $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}",
            $"Android {DeviceInfo.Current.VersionString}");

    // SENDTO with a mailto: URI limits the chooser to email apps.
    private static void Open(ReportEmail email)
    {
        var intent = new Intent(Intent.ActionSendto, AndroidUri.Parse("mailto:"));
        intent.PutExtra(Intent.ExtraEmail, new[] { CrashReport.To });
        intent.PutExtra(Intent.ExtraSubject, email.Subject);
        intent.PutExtra(Intent.ExtraText, email.Body);
        intent.AddFlags(ActivityFlags.NewTask);
        AndroidApp.Context.StartActivity(intent);
    }
}
