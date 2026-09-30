using Android.App;
using Android.Content;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// MAUI has no API for the system "Save as" and folder pickers, so results come back through MainActivity.
internal static class ActivityResultBridge
{
    private static int _nextRequestCode = 7300;
    private static readonly Dictionary<int, TaskCompletionSource<AndroidUri?>> Pending = new();

    public static Task<AndroidUri?> StartAsync(Intent intent)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No activity to show the picker.");
        var code = Interlocked.Increment(ref _nextRequestCode);
        var tcs = new TaskCompletionSource<AndroidUri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Pending)
        {
            Pending[code] = tcs;
        }
        activity.StartActivityForResult(intent, code);
        return tcs.Task;
    }

    public static bool Complete(int requestCode, Result resultCode, Intent? data)
    {
        TaskCompletionSource<AndroidUri?>? tcs;
        lock (Pending)
        {
            if (!Pending.Remove(requestCode, out tcs))
            {
                return false;
            }
        }
        tcs.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
        return true;
    }
}
