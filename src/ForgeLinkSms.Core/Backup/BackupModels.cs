using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Backup;

public sealed record BackupManifest(int Format, string AppVersion, DateTimeOffset CreatedUtc, int SmsCount, int MmsCount, int MediaFiles, int Conversations);

public sealed record BackupAttachment(string Media, string ContentType, string? FileName);

// Addresses are the conversation's participants (so every message in a group shares one key);
// From is the sender of an incoming picture message, which a group needs to restore correctly.
public sealed record BackupMessage(
    bool IsMms,
    IReadOnlyList<string> Addresses,
    string? From,
    long TimestampMs,
    long DateSentMs,
    bool Outgoing,
    bool Read,
    int Status,
    string? Body,
    string? Subject,
    IReadOnlyList<BackupAttachment> Attachments);

public sealed record BackupFilter(string Name, string ColorHex, IReadOnlyList<string> Members);

public sealed record BackupScheduled(string Conversation, string Address, string Body, DateTimeOffset SendAtUtc, string GroupAddresses,
    ScheduleRepeat Repeat = ScheduleRepeat.None, DateTimeOffset? RepeatFromUtc = null);

public sealed record BackupTimed(string Conversation, DateTimeOffset? UntilUtc);

public sealed record BackupDraft(string Conversation, string Text);

public sealed record BackupGroupName(string Conversation, string Name);

// The profile's text details; the photo is a file on the phone and isn't backed up.
public sealed record BackupProfile(string DisplayName, string PhoneNumber, string Email, string Street, string City, string State, string PostalCode);

public sealed record BackupSettings(DisplaySettings Display, NotificationSettings Notifications, string ThemeMode, string AccentColor);

public sealed record AppData
{
    public IReadOnlyList<string> Favorites { get; init; } = [];
    public IReadOnlyList<BackupFilter> Filters { get; init; } = [];
    public IReadOnlyList<string> QuickReplies { get; init; } = [];
    public IReadOnlyList<BackupScheduled> Scheduled { get; init; } = [];
    public IReadOnlyList<string> Archived { get; init; } = [];
    public IReadOnlyList<string> Pinned { get; init; } = [];
    public IReadOnlyList<string> Trashed { get; init; } = [];
    public IReadOnlyList<BackupTimed> Snoozed { get; init; } = [];
    public IReadOnlyList<BackupTimed> Muted { get; init; } = [];
    public IReadOnlyList<string> Blocked { get; init; } = [];
    public IReadOnlyList<string> Allowed { get; init; } = [];
    public IReadOnlyList<BackupDraft> Drafts { get; init; } = [];
    public BackupSettings? Settings { get; init; }
    public IReadOnlyList<BackupGroupName> GroupNames { get; init; } = [];
    public BackupProfile? Profile { get; init; }
}

// Android thread ids differ on another phone, so a conversation is identified by who's in it.
public static class ConversationKey
{
    public static string Normalize(string address)
    {
        var digits = PhoneNumberFormatter.ToComparableDigits(address);
        return digits.Length > 0 ? digits : address.Trim().ToLowerInvariant();
    }

    public static string From(IEnumerable<string> addresses) =>
        string.Join(",", addresses.Select(Normalize).Where(a => a.Length > 0).Distinct().OrderBy(a => a, StringComparer.Ordinal));
}
