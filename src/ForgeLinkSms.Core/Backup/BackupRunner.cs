namespace ForgeLinkSms.Core.Backup;

public sealed record SourceAttachment(string ContentType, string? FileName, Func<Stream> Open);

public sealed record SourceMessage(BackupMessage Message, IReadOnlyList<SourceAttachment> Attachments);

public sealed record BackupProgress(int Done, int Total, int MediaFiles);

public interface IBackupSource
{
    Task<AppData> ReadAppDataAsync();
    int CountMessages();
    IEnumerable<SourceMessage> ReadMessages();
}

public static class BackupRunner
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg", ["image/jpg"] = "jpg", ["image/png"] = "png", ["image/gif"] = "gif", ["image/webp"] = "webp",
        ["image/heic"] = "heic", ["video/mp4"] = "mp4", ["video/3gpp"] = "3gp", ["audio/amr"] = "amr", ["audio/mp4"] = "m4a",
        ["audio/aac"] = "aac", ["audio/mpeg"] = "mp3", ["text/x-vcard"] = "vcf", ["text/vcard"] = "vcf"
    };

    public static async Task<BackupManifest> RunAsync(
        IBackupSource source, Stream destination, string? password, string appVersion, DateTimeOffset now,
        IProgress<BackupProgress>? progress, CancellationToken cancellationToken, int iterations = BackupCrypto.Iterations)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var appData = await source.ReadAppDataAsync();
        var total = source.CountMessages();
        var messages = new List<BackupMessage>();
        var conversations = new HashSet<string>();
        int sms = 0, mms = 0, done = 0;

        using var writer = new BackupWriter(destination, password, iterations);
        foreach (var item in source.ReadMessages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attachments = new List<BackupAttachment>();
            foreach (var attachment in item.Attachments)
            {
                using var content = attachment.Open();
                attachments.Add(new BackupAttachment(writer.AddMedia(content, ExtensionFor(attachment)), attachment.ContentType, attachment.FileName));
            }
            var message = item.Message with { Attachments = attachments };
            messages.Add(message);
            conversations.Add(ConversationKey.From(message.Addresses));
            if (message.IsMms) mms++; else sms++;
            done++;
            if (done % 100 == 0 || done == total)
            {
                progress?.Report(new BackupProgress(done, total, writer.MediaCount));
            }
        }

        var manifest = new BackupManifest(BackupWriter.Format, appVersion, now, sms, mms, writer.MediaCount, conversations.Count);
        writer.Finish(manifest, messages, appData);
        return manifest;
    }

    private static string ExtensionFor(SourceAttachment attachment)
    {
        var fromName = Path.GetExtension(attachment.FileName ?? string.Empty).TrimStart('.');
        if (fromName.Length is > 0 and <= 5)
        {
            return fromName;
        }
        return Extensions.TryGetValue(attachment.ContentType, out var ext) ? ext : "bin";
    }
}
