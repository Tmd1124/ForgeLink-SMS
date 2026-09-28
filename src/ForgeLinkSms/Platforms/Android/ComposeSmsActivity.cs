using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Microsoft.Extensions.DependencyInjection;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;
using AndroidUri = global::Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

// Entry point for "send an SMS to…" links and for the system Share menu. Shared text and files
// are handed to the New Message screen the same way a forwarded message is.
[Activity(Exported = true)]
[IntentFilter(new[] { Intent.ActionSendto }, Categories = new[] { Intent.CategoryDefault }, DataSchemes = new[] { "sms", "smsto", "mms", "mmsto" })]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault },
    DataMimeTypes = new[] { "text/plain", "image/*", "video/*", "audio/*", "text/x-vcard", "text/vcard" })]
public class ComposeSmsActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var intent = Intent;
        var address = intent?.Data?.SchemeSpecificPart;
        var text = intent?.GetStringExtra(Intent.ExtraText) ?? intent?.GetStringExtra("sms_body");
        var attachment = intent?.Action == Intent.ActionSend ? CopySharedFile(intent) : null;
        if (!string.IsNullOrEmpty(text) || attachment is not null)
        {
            MauiApplication.Current.Services.GetRequiredService<ForwardRequestStore>().Set(text, attachment);
        }

        var route = $"/compose?add={Uri.EscapeDataString(address ?? string.Empty)}";
        var mainIntent = new Intent(this, typeof(MainActivity));
        mainIntent.PutExtra("initial_route", route);
        mainIntent.AddFlags(ActivityFlags.NewTask);
        StartActivity(mainIntent);
        Finish();
    }

    // The sharing app only grants read access for the lifetime of this intent, so the file is
    // copied into ForgeLink's own storage right away.
    private PickedAttachment? CopySharedFile(Intent intent)
    {
        var uri = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? intent.GetParcelableExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(AndroidUri))) as AndroidUri
#pragma warning disable CA1422 // the typed overload above covers API 33+
            : intent.GetParcelableExtra(Intent.ExtraStream) as AndroidUri;
#pragma warning restore CA1422
        if (uri is null)
        {
            return null;
        }

        try
        {
            var contentType = ContentResolver!.GetType(uri) ?? intent.Type ?? "application/octet-stream";
            var fileName = DisplayName(uri) ?? $"shared-{DateTime.Now:yyyyMMdd-HHmmss}";
            var directory = Path.Combine(FileSystem.AppDataDirectory, "attachments");
            Directory.CreateDirectory(directory);
            var localPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
            using (var input = ContentResolver.OpenInputStream(uri))
            {
                if (input is null)
                {
                    return null;
                }
                using var output = File.Create(localPath);
                input.CopyTo(output);
            }
            return new PickedAttachment { FileName = fileName, LocalPath = localPath, Kind = AttachmentKindClassifier.FromContentType(contentType) };
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("ForgeLinkSms", $"Couldn't read shared file {uri}: {ex.Message}");
            return null;
        }
    }

    private string? DisplayName(AndroidUri uri)
    {
        using var cursor = ContentResolver!.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
    }
}
