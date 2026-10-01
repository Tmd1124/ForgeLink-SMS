using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidUri = global::Android.Net.Uri;
using AndroidContentValues = global::Android.Content.ContentValues;
using AndroidMediaStore = global::Android.Provider.MediaStore;
using AndroidEnvironment = global::Android.OS.Environment;
using AndroidMimeTypeMap = global::Android.Webkit.MimeTypeMap;

namespace ForgeLinkSms.Platforms.Android;

public class AttachmentSaveService : IAttachmentSaveService
{
    public Task<bool> SaveToDeviceAsync(MessageAttachment attachment) =>
        Task.Run(() => SaveCore(attachment));

    public Task<PickedAttachment?> CopyForSendingAsync(MessageAttachment attachment) => Task.Run<PickedAttachment?>(() =>
    {
        try
        {
            var directory = Path.Combine(FileSystem.AppDataDirectory, "attachments");
            Directory.CreateDirectory(directory);
            var localPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{attachment.FileName}");
            using (var input = AndroidApp.Context.ContentResolver!.OpenInputStream(AndroidUri.Parse($"content://mms/part/{attachment.PartId}")!))
            {
                if (input is null)
                {
                    return null;
                }
                using var output = File.Create(localPath);
                input.CopyTo(output);
            }
            return new PickedAttachment { FileName = attachment.FileName, LocalPath = localPath, Kind = attachment.Kind };
        }
        catch (Exception)
        {
            return null;
        }
    });

    // The part is copied into the app's cache and shared through the app's FileProvider, since the
    // message store's own part URIs can't be handed to another app.
    public Task<bool> OpenInViewerAsync(MessageAttachment attachment) => Task.Run(() =>
    {
        try
        {
            var context = AndroidApp.Context;
            var file = CopyToViewCache(context, attachment);
            if (file is null)
            {
                return false;
            }

            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.PackageName + ".fileprovider", file);
            var extension = Path.GetExtension(attachment.FileName).TrimStart('.').ToLowerInvariant();
            var mime = AndroidMimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension)
                ?? (attachment.Kind == AttachmentKind.Video ? "video/*" : "image/*");
            var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView);
            intent.SetDataAndType(uri, mime);
            intent.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission | global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public Task<(byte[] Data, string ContentType)?> ReadAsync(MessageAttachment attachment) => Task.Run<(byte[] Data, string ContentType)?>(() =>
    {
        try
        {
            var resolver = AndroidApp.Context.ContentResolver!;
            var partUri = AndroidUri.Parse($"content://mms/part/{attachment.PartId}")!;
            string? contentType = null;
            using (var cursor = resolver.Query(partUri, new[] { "ct" }, null, null, null))
            {
                if (cursor?.MoveToFirst() == true)
                {
                    contentType = cursor.GetString(0);
                }
            }
            using var input = resolver.OpenInputStream(partUri);
            if (input is null)
            {
                return null;
            }
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            return (buffer.ToArray(), contentType ?? (attachment.Kind == AttachmentKind.Video ? "video/mp4" : "image/jpeg"));
        }
        catch (Exception)
        {
            return null;
        }
    });

    public Task<bool> PlayVideoAsync(MessageAttachment attachment) => Task.Run(() =>
    {
        try
        {
            var context = AndroidApp.Context;
            var file = CopyToViewCache(context, attachment);
            if (file is null)
            {
                return false;
            }
            var intent = new global::Android.Content.Intent(context, typeof(VideoPlayerActivity));
            intent.PutExtra(VideoPlayerActivity.PathExtra, file.AbsolutePath);
            intent.PutExtra(VideoPlayerActivity.PartIdExtra, attachment.PartId);
            intent.PutExtra(VideoPlayerActivity.FileNameExtra, attachment.FileName);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });

    // Only the latest opened file is kept; opening another clears the folder.
    private static Java.IO.File? CopyToViewCache(global::Android.Content.Context context, MessageAttachment attachment)
    {
        var directory = new Java.IO.File(context.CacheDir, "view");
        directory.Mkdirs();
        foreach (var old in directory.ListFiles() ?? Array.Empty<Java.IO.File>())
        {
            old.Delete();
        }
        var file = new Java.IO.File(directory, $"{attachment.PartId}-{attachment.FileName}");
        using var input = context.ContentResolver!.OpenInputStream(AndroidUri.Parse($"content://mms/part/{attachment.PartId}")!);
        if (input is null)
        {
            return null;
        }
        using var output = File.Create(file.AbsolutePath!);
        input.CopyTo(output);
        return file;
    }

    private static bool SaveCore(MessageAttachment attachment)
    {
        var context = AndroidApp.Context;
        var resolver = context.ContentResolver!;

        // Always re-read from the source part rather than reusing attachment.DataUri: DataUri is
        // only ever populated for images/GIFs under MmsReader's inline-size cap, so it's the only
        // path that also works for video/file attachments and for capped-out large images.
        byte[] data;
        try
        {
            using var input = resolver.OpenInputStream(AndroidUri.Parse($"content://mms/part/{attachment.PartId}")!);
            if (input is null)
            {
                return false;
            }
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            data = buffer.ToArray();
        }
        catch (Exception)
        {
            return false;
        }

        var contentType = GetContentType(attachment);
        var (collectionUri, relativeDir) = attachment.Kind switch
        {
            AttachmentKind.Image or AttachmentKind.Gif => (AndroidMediaStore.Images.Media.ExternalContentUri, AndroidEnvironment.DirectoryPictures),
            AttachmentKind.Video => (AndroidMediaStore.Video.Media.ExternalContentUri, AndroidEnvironment.DirectoryMovies),
            _ => (AndroidMediaStore.Downloads.ExternalContentUri, AndroidEnvironment.DirectoryDownloads)
        };

        var values = new AndroidContentValues();
        values.Put(AndroidMediaStore.IMediaColumns.DisplayName, attachment.FileName);
        values.Put(AndroidMediaStore.IMediaColumns.MimeType, contentType);
        // A dedicated subfolder keeps saved attachments easy to find instead of mixing them into
        // the top level of Pictures/Movies/Download alongside camera photos and other downloads.
        values.Put(AndroidMediaStore.IMediaColumns.RelativePath, $"{relativeDir}/ForgeLink SMS");

        try
        {
            var itemUri = resolver.Insert(collectionUri!, values);
            if (itemUri is null)
            {
                return false;
            }
            using var output = resolver.OpenOutputStream(itemUri);
            if (output is null)
            {
                return false;
            }
            output.Write(data, 0, data.Length);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string GetContentType(MessageAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName).TrimStart('.').ToLowerInvariant();
        return AndroidMimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? attachment.Kind switch
        {
            AttachmentKind.Image => "image/jpeg",
            AttachmentKind.Gif => "image/gif",
            AttachmentKind.Video => "video/mp4",
            _ => "application/octet-stream"
        };
    }
}
