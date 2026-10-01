namespace ForgeLinkSms.Core.Models;

public enum ListColorMode
{
    Person,
    Filter,
    None
}

public class DisplaySettings
{
    public const string Gray = "#6b7280";

    public ConversationDisplayStyle Layout { get; set; } = ConversationDisplayStyle.BubblesAndCards;

    // Who gets a bubble. In the Bubbles layout anyone not picked is left out entirely (reachable
    // through search and the lane tabs); in Bubbles + list they go to the list.
    public bool BubbleEveryone { get; set; }
    public bool BubbleFavorites { get; set; } = true;
    public bool BubbleUnread { get; set; } = true;
    public bool BubbleRecent { get; set; } = true;
    public bool BubbleFiltered { get; set; }

    public bool ShowFilterTabs { get; set; } = true;

    public ListColorMode ColorBy { get; set; } = ListColorMode.Person;

    /// Overrides ColorBy for unread conversations; null means unread rows get no color.
    public string? UnreadColor { get; set; } = Gray;

    // Building a preview fetches the linked page — the app's only use of the internet.
    public bool ShowLinkPreviews { get; set; } = true;

    /// Off: the home screen widget shows names and counts, but "New message" instead of the text.
    public bool ShowWidgetText { get; set; } = true;

    /// Percent of the normal text size; one of DisplayRules.TextScales.
    public int TextScale { get; set; } = 100;

    public DisplaySettings Clone() => (DisplaySettings)MemberwiseClone();
}
