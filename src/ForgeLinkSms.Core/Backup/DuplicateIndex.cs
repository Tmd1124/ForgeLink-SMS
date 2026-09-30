namespace ForgeLinkSms.Core.Backup;

public sealed record ExistingMessage(string Conversation, long TimestampMs, bool Outgoing, string? Body, int AttachmentCount);

// Providers round message times differently (MMS stores whole seconds), so a match allows one second either way.
public sealed class DuplicateIndex
{
    private readonly HashSet<Key> _keys = new();

    public void Add(ExistingMessage message) =>
        _keys.Add(KeyFor(message.Conversation, Seconds(message.TimestampMs), message.Outgoing, message.Body, message.AttachmentCount));

    public bool Contains(BackupMessage message)
    {
        var conversation = ConversationKey.From(message.Addresses);
        var seconds = Seconds(message.TimestampMs);
        for (var delta = -1; delta <= 1; delta++)
        {
            if (_keys.Contains(KeyFor(conversation, seconds + delta, message.Outgoing, message.Body, message.Attachments.Count)))
            {
                return true;
            }
        }
        return false;
    }

    private static long Seconds(long ms) => (long)Math.Floor(ms / 1000.0);

    // The text is kept as a 64-bit fingerprint, not a copy: a phone with years of texts would
    // otherwise hold every message body in memory for the whole restore.
    private readonly record struct Key(string Conversation, long Seconds, bool Outgoing, int Attachments, ulong BodyHash);

    private static Key KeyFor(string conversation, long seconds, bool outgoing, string? body, int attachments) =>
        new(conversation, seconds, outgoing, attachments, Fingerprint((body ?? string.Empty).AsSpan().Trim()));

    // FNV-1a: stable across runs, unlike string.GetHashCode.
    private static ulong Fingerprint(ReadOnlySpan<char> text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 1099511628211UL;
        }
        return hash;
    }
}
