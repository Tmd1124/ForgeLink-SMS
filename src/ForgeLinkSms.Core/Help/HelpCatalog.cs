using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Help;

/// One icon on a screen and what it does, in a few words.
public sealed record HelpItem(string Icon, string Text);

public sealed record HelpTopic(string Title, string Summary, IReadOnlyList<HelpItem> Icons, IReadOnlyList<string> Tips);

// What the ⓘ panel says on each screen. Kept short on purpose: a few words per icon, a line per tip.
public static class HelpCatalog
{
    /// Icons the help panel knows how to draw (as outlines).
    public static readonly IReadOnlySet<string> IconNames = new HashSet<string>
    {
        "eye", "filter", "search", "user", "plus", "swipe", "bubbles", "pin", "tag", "chevron", "check", "ban", "flag",
        "trash", "image", "paperclip", "mic", "send", "download", "play", "reply", "bell", "clock", "undo", "archive",
        "moon", "palette", "settings", "star", "mail", "users", "key", "shield", "lock", "refresh", "contact", "sun",
        "phone", "text", "info"
    };

    private static HelpItem I(string icon, string text) => new(icon, text);

    private static readonly HelpTopic Chats = new("Chats", "All your conversations in one place.",
        new[]
        {
            I("eye", "Show unread only"),
            I("filter", "Show chats in a filter"),
            I("search", "Search chats, photos and links"),
            I("user", "Menu and your profile"),
            I("plus", "Start a new message"),
            I("swipe", "Swipe a chat to archive or delete"),
            I("bubbles", "Favorites and unread show as bubbles"),
            I("tag", "Filter tabs — long-press to reorder")
        },
        new[]
        {
            "Long-press a chat to select it, pin it, mute it or add it to a filter.",
            "Choose what swiping does in Menu → Scrolling.",
            "Undo appears right where the chat was for 3 seconds."
        });

    private static readonly HelpTopic Updates = new("Updates", "Automated texts from businesses, grouped by topic.",
        new[]
        {
            I("chevron", "Open a topic"),
            I("check", "Move a sender to Chats"),
            I("ban", "Block the sender"),
            I("flag", "Report spam to your carrier (7726)"),
            I("trash", "Delete")
        },
        new[]
        {
            "Short codes, toll-free numbers and named senders land here.",
            "They arrive without a notification, except verification codes.",
            "Businesses you've saved as contacts stay in Chats."
        });

    private static readonly HelpTopic Screener = new("Screener", "Texts from numbers you don't know yet.",
        new[]
        {
            I("check", "Move the sender to Chats"),
            I("ban", "Block the sender"),
            I("flag", "Report spam to your carrier (7726)"),
            I("trash", "Delete")
        },
        new[]
        {
            "Numbers not in your contacts that you've never replied to.",
            "Often spam, wrong numbers or someone new.",
            "They arrive silently, except verification codes."
        });

    private static readonly Dictionary<string, HelpTopic> Topics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["conversations/thread"] = new("Chat", "Read and reply to one conversation.",
            new[]
            {
                I("image", "Photos, videos and links in this chat"),
                I("search", "Search this chat"),
                I("plus", "Attach, schedule, quick replies, My card"),
                I("mic", "Record a voice message"),
                I("send", "Send"),
                I("play", "Tap a photo or video to open it"),
                I("download", "Save a photo or video")
            },
            new[]
            {
                "Long-press a message to react, reply, copy or set a reminder.",
                "After you tap send you have 5 seconds to undo.",
                "Status under your text: Sending, Sent, Delivered or Not sent."
            }),
        ["conversations/media"] = new("Photos & links", "Everything shared in this chat.",
            new[] { I("image", "Photos and videos"), I("play", "Tap a video to play it"), I("download", "Save to your phone") },
            new[] { "Switch to Links to see every web link in the chat." }),
        ["compose"] = new("New message", "Start a text or a group.",
            new[]
            {
                I("contact", "Pick contacts or type a number"),
                I("users", "Two or more people: group or separate texts"),
                I("plus", "Attach, schedule or add My card"),
                I("send", "Send")
            },
            new[] { "Press Enter after typing a number to add it." }),
        ["menu"] = new("Menu", "Everything beyond your chats.",
            new[]
            {
                I("check", "Mark every chat as read"),
                I("undo", "Undo the last action"),
                I("clock", "Scheduled texts"),
                I("bell", "Message reminders"),
                I("swipe", "Choose what swiping does"),
                I("tag", "Filters"),
                I("archive", "Archived chats"),
                I("moon", "Snoozed chats"),
                I("ban", "Blocked numbers"),
                I("trash", "Trash"),
                I("palette", "Theme"),
                I("settings", "Settings")
            },
            new[] { "Tap your photo at the top to edit your profile." }),
        ["settings"] = new("Settings", "Make ForgeLink work your way.",
            new[]
            {
                I("palette", "Appearance: theme and text size"),
                I("bell", "Notifications and quiet hours"),
                I("bubbles", "Chat list layout and colors"),
                I("archive", "Backup & restore"),
                I("mail", "Help & feedback"),
                I("lock", "Privacy")
            },
            new[] { "Each row shows what's set now." }),
        ["settings/appearance"] = new("Appearance", "Theme and text size.",
            new[] { I("palette", "Colors and light or dark mode"), I("text", "Make all text bigger or smaller") },
            new[] { "Text size changes the whole app, not just messages." }),
        ["settings/notifications"] = new("Notifications", "Who can notify you, and how.",
            new[] { I("bell", "Notify for each group"), I("key", "Always alert for verification codes"), I("moon", "Quiet hours: no sound") },
            new[] { "Muted chats never notify.", "Favorites can still ring during quiet hours." }),
        ["settings/chat-list"] = new("Chat list", "How your Chats screen looks.",
            new[] { I("bubbles", "Choose who gets a bubble"), I("palette", "Color the list by person or filter"), I("tag", "Show filters as tabs") },
            new[] { "A quick preset sets everything in one tap." }),
        ["settings/backup"] = new("Backup & restore", "Keep your messages safe.",
            new[]
            {
                I("archive", "Back up now"),
                I("refresh", "Back up every week while charging"),
                I("lock", "Protect backups with a password"),
                I("download", "Restore from a backup"),
                I("send", "Export for other apps")
            },
            new[] { "Restoring only adds what's missing — nothing is deleted.", "Pick a Google Drive folder to keep backups off the phone." }),
        ["settings/help"] = new("Help & feedback", "Reach the people who make ForgeLink.",
            new[] { I("mail", "Send feedback by email"), I("star", "Rate ForgeLink") },
            new[] { "Nothing is sent until you send the email yourself." }),
        ["settings/privacy"] = new("Privacy", "What leaves your phone.",
            new[] { I("eye", "Link previews visit the linked page"), I("lock", "Hide message text in the widget") },
            new[] { "Everything else stays on your phone." }),
        ["scrolling"] = new("Scrolling", "Choose what swiping a chat does.",
            new[] { I("archive", "Archive"), I("trash", "Move to Trash"), I("ban", "Nothing") },
            new[] { "Undo appears right where the chat was for 3 seconds." }),
        ["filters"] = new("Filters", "Group chats under a name, emoji and color.",
            new[] { I("tag", "Tap a name to rename it"), I("palette", "Tap the dot to change its color"), I("trash", "Delete — Undo shows for 3 seconds") },
            new[] { "Filters with chats show as tabs above your chats.", "Long-press a tab there to reorder." }),
        ["scheduled"] = new("Scheduled", "Texts waiting to be sent.",
            new[] { I("clock", "Tap a text to change it"), I("trash", "Cancel — Undo shows for 3 seconds") },
            new[] { "Schedule one from + in any chat." }),
        ["reminders"] = new("Reminders", "Messages you asked to be reminded about.",
            new[] { I("bell", "Tap to jump to the message"), I("trash", "Remove a reminder") },
            new[] { "Long-press a message and tap Remind me to add one." }),
        ["archived"] = new("Archived", "Chats you've put away.",
            new[] { I("archive", "Unarchive to bring a chat back") },
            new[] { "A new message brings a chat back on its own." }),
        ["snoozed"] = new("Snoozed", "Chats hidden until later.",
            new[] { I("moon", "They return at the time you chose") },
            new[] { "A new message wakes a chat early." }),
        ["blocked"] = new("Blocked", "Numbers that can't reach you.",
            new[] { I("ban", "Unblock to hear from them again") },
            Array.Empty<string>()),
        ["trash"] = new("Trash", "Deleted chats.",
            new[] { I("refresh", "Restore a chat"), I("trash", "Delete forever") },
            new[] { "Deleting from Trash can't be undone." }),
        ["theme"] = new("Theme", "Colors for the whole app.",
            new[] { I("sun", "Light, dark or match your phone"), I("palette", "Pick an accent color") },
            Array.Empty<string>()),
        ["profile"] = new("Your Profile", "Your details, kept on this phone.",
            new[] { I("contact", "Fill from your phone"), I("pin", "Use your current location"), I("send", "Share them with My card") },
            new[] { "Nothing is shared until you send My card." }),
        ["onboarding"] = new("Welcome", "Set ForgeLink as your texting app.",
            new[] { I("check", "Make ForgeLink your default SMS app") },
            new[] { "Android only lets one app send and receive texts." }),
        ["rate"] = new("Rate ForgeLink", "Tell us how we're doing.",
            new[] { I("star", "Tap a star"), I("mail", "Send opens your email") },
            new[] { "Nothing is sent until you send the email yourself." })
    };

    private static readonly HelpTopic General = new("ForgeLink SMS", "Fast, private texting.",
        Array.Empty<HelpItem>(), new[] { "Tap ⓘ on any screen for help with it." });

    public static IEnumerable<HelpTopic> All => Topics.Values.Concat(new[] { Chats, Updates, Screener, General });

    public static HelpTopic ForRoute(string relativePath, ConversationLane lane = ConversationLane.Conversations)
    {
        var path = relativePath.Split('?', '#')[0].Trim('/');
        if (path.Length == 0 || path.Equals("conversations", StringComparison.OrdinalIgnoreCase))
        {
            return lane switch
            {
                ConversationLane.Updates => Updates,
                ConversationLane.Screener => Screener,
                _ => Chats
            };
        }
        return Topics.TryGetValue(path, out var topic) ? topic : General;
    }
}
