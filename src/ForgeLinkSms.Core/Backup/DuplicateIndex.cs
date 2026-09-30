namespace ForgeLinkSms.Core.Backup;

public sealed record ExistingMessage(string Conversation, long TimestampMs, bool Outgoing, string? Body, int AttachmentCount);

// Providers round message times differently (MMS stores whole seconds), so a match allows one second either way.
public sealed class DuplicateIndex
{
    private readonly HashSet<string> _keys = new();

    public void Add(ExistingMessage message) =>
        _keys.Add(Key(message.Conversation, Seconds(message.TimestampMs), message.Outgoing, message.Body, message.AttachmentCount));

    public bool Contains(BackupMessage message)
    {
        var conversation = ConversationKey.From(message.Addresses);
        var seconds = Seconds(message.TimestampMs);
        for (var delta = -1; delta <= 1; delta++)
        {
            if (_keys.Contains(Key(conversation, seconds + delta, message.Outgoing, message.Body, message.Attachments.Count)))
            {
                return true;
            }
        }
        return false;
    }

    private static long Seconds(long ms) => (long)Math.Floor(ms / 1000.0);

    private static string Key(string conversation, long seconds, bool outgoing, string? body, int attachments) =>
        $"{conversation}|{seconds}|{(outgoing ? 1 : 0)}|{attachments}|{(body ?? string.Empty).Trim()}";
}
