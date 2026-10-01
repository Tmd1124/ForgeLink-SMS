using Android.Content;
using Android.Graphics;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.Core.Content.PM;
using AndroidX.Core.Graphics.Drawable;

namespace ForgeLinkSms.Platforms.Android;

// One long-lived shortcut per chat. Android only treats a notification as a conversation (the
// Conversations section, Priority, Android Auto) when it points at such a shortcut.
internal static class ConversationShortcuts
{
    public static string IdFor(long threadId) => $"chat-{threadId}";

    public static string? Push(Context context, long threadId, string address, string name, IReadOnlyList<Person> people, Bitmap? icon)
    {
        try
        {
            var id = IdFor(threadId);
            var intent = new Intent(context, typeof(MainActivity));
            intent.SetAction(Intent.ActionView);
            // Same as a notification tap: reuse the open app instead of stacking a second copy on it.
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            intent.PutExtra("initial_route", $"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}");
            var builder = new ShortcutInfoCompat.Builder(context, id)
                .SetShortLabel(string.IsNullOrWhiteSpace(name) ? address : name)
                .SetIntent(intent)
                .SetLongLived(true)
                .SetLocusId(new LocusIdCompat(id))
                .SetCategories(new List<string> { "android.shortcut.conversation" })
                .SetPersons(people.ToArray());
            if (icon is not null)
            {
                builder.SetIcon(IconCompat.CreateWithBitmap(icon));
            }
            ShortcutManagerCompat.PushDynamicShortcut(context, builder.Build());
            return id;
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("ForgeLinkSms", $"Conversation shortcut for {threadId} failed: {e.Message}");
            return null;
        }
    }

    public static void Remove(Context context, long threadId)
    {
        try
        {
            ShortcutManagerCompat.RemoveLongLivedShortcuts(context, new List<string> { IdFor(threadId) });
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("ForgeLinkSms", $"Removing conversation shortcut for {threadId} failed: {e.Message}");
        }
    }
}
