using ForgeLinkSms.Core.Services;
using AndroidApp = global::Android.App.Application;
using AndroidTelephony = global::Android.Provider.Telephony;
using AndroidContentValues = global::Android.Content.ContentValues;

namespace ForgeLinkSms.Platforms.Android;

public class MarkAsReadService : IMarkAsReadService
{
    private readonly IThreadService _threadService;

    public MarkAsReadService(IThreadService threadService)
    {
        _threadService = threadService;
    }

    public async Task<IReadOnlyList<long>> MarkAllAsReadAsync()
    {
        var threads = await _threadService.GetThreadsAsync();
        var unreadThreadIds = threads.Where(t => t.UnreadCount > 0).Select(t => t.Id).ToList();

        await Task.Run(() => SetReadFlag(unreadThreadIds, read: 1));

        return unreadThreadIds;
    }

    // SetReadFlag is blocking ContentResolver work — Task.Run here matches SmsService/
    // ThreadService, which back this same content://sms and content://mms tables off the UI
    // thread for the same reason (a stall here can otherwise block a Blazor page's render).
    public Task MarkThreadAsReadAsync(long threadId) =>
        Task.Run(() => SetReadFlag(new[] { threadId }, read: 1));

    public Task MarkThreadsAsReadAsync(IReadOnlyList<long> threadIds) =>
        Task.Run(() => SetReadFlag(threadIds, read: 1));

    public Task MarkThreadsAsUnreadAsync(IReadOnlyList<long> threadIds) =>
        Task.Run(() => SetReadFlag(threadIds, read: 0));

    private static void SetReadFlag(IReadOnlyList<long> threadIds, int read)
    {
        try
        {
            WriteReadFlag(threadIds, read);
        }
        finally
        {
            ForgeLinkSms.Platforms.Android.Widget.WidgetUpdater.RequestUpdate(global::Android.App.Application.Context);
        }
    }

    private static void WriteReadFlag(IReadOnlyList<long> threadIds, int read)
    {
        var context = AndroidApp.Context;
        var values = new AndroidContentValues();
        values.Put("read", read);
        var mmsUri = global::Android.Net.Uri.Parse("content://mms")!;

        foreach (var threadId in threadIds)
        {
            var args = new[] { threadId.ToString() };
            context.ContentResolver!.Update(AndroidTelephony.Sms.ContentUri!, values, "thread_id = ?", args);
            // A thread's unread state can come from either table (see ThreadService), so both
            // have to be flipped or a thread whose newest message is an MMS would stay stuck
            // showing unread after being opened/marked read.
            context.ContentResolver!.Update(mmsUri, values, "thread_id = ?", args);
        }
    }
}
