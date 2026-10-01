using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidUri = global::Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android;

public class ThreadDeletionService : IThreadDeletionService
{
    public Task DeleteThreadAsync(long threadId) => Task.Run(() =>
    {
        // content://mms-sms/conversations/{id} is Android's own whole-thread delete endpoint —
        // the same one the platform's own Messages app uses — and removes the thread's SMS and
        // MMS rows (and the thread's own conversations-table entry) in a single call, rather
        // than deleting from content://sms and content://mms separately.
        var uri = AndroidUri.Parse($"content://mms-sms/conversations/{threadId}");
        AndroidApp.Context.ContentResolver!.Delete(uri!, null, null);
        ConversationShortcuts.Remove(AndroidApp.Context, threadId);
    });
}
