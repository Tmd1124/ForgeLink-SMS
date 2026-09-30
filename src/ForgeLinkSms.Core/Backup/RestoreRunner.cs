namespace ForgeLinkSms.Core.Backup;

public interface IRestoreTarget
{
    Task<IReadOnlyList<ExistingMessage>> ReadExistingMessagesAsync();
    Task<AppData> ReadAppDataAsync();
    Task InsertSmsAsync(BackupMessage message);
    Task InsertMmsAsync(BackupMessage message, Func<string, Stream> openMedia);
    Task ApplyAsync(MergePlan plan);
}

public sealed record RestoreResult(int Added, int Skipped);

public sealed record RestorePreview(DateTimeOffset CreatedUtc, int Messages, int MediaFiles, int WouldAdd, int AlreadyPresent, int ScheduledTexts);

public static class RestoreRunner
{
    public static async Task<RestoreResult> RunAsync(
        BackupReader reader, IRestoreTarget target, bool includeSettings, bool includeScheduled, DateTimeOffset now,
        IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        var (added, skipped, restored) = await WalkAsync(reader, target, write: true, progress, cancellationToken);
        var plan = AppDataMerger.Plan(await target.ReadAppDataAsync(), reader.AppData, restored, includeSettings, includeScheduled, now);
        await target.ApplyAsync(plan);
        return new RestoreResult(added, skipped);
    }

    // Same matching as a real restore, but nothing is written — so the numbers shown before
    // confirming are exactly what the restore will do.
    public static async Task<RestorePreview> PreviewAsync(BackupReader reader, IRestoreTarget target, CancellationToken cancellationToken)
    {
        var (wouldAdd, present, restored) = await WalkAsync(reader, target, write: false, progress: null, cancellationToken);
        // Also exercises the merge, so a data problem shows up here rather than after messages were written.
        var plan = AppDataMerger.Plan(await target.ReadAppDataAsync(), reader.AppData, restored, includeSettings: false, includeScheduled: true, DateTimeOffset.UtcNow);
        var manifest = reader.Manifest;
        return new RestorePreview(manifest.CreatedUtc, manifest.SmsCount + manifest.MmsCount, manifest.MediaFiles, wouldAdd, present, plan.ScheduledToAdd.Count);
    }

    private static async Task<(int Added, int Skipped, HashSet<string> Restored)> WalkAsync(
        BackupReader reader, IRestoreTarget target, bool write, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        var index = new DuplicateIndex();
        foreach (var existing in await target.ReadExistingMessagesAsync())
        {
            index.Add(existing);
        }

        var total = reader.Manifest.SmsCount + reader.Manifest.MmsCount;
        var restored = new HashSet<string>();
        int added = 0, skipped = 0, done = 0;
        foreach (var message in reader.ReadMessages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var conversation = ConversationKey.From(message.Addresses);
            if (index.Contains(message))
            {
                skipped++;
            }
            else
            {
                if (write)
                {
                    if (message.IsMms)
                    {
                        await target.InsertMmsAsync(message, reader.OpenMedia);
                    }
                    else
                    {
                        await target.InsertSmsAsync(message);
                    }
                }
                // Also guards against the same message appearing twice inside one backup.
                index.Add(new ExistingMessage(conversation, message.TimestampMs, message.Outgoing, message.Body, message.Attachments.Count));
                // Only conversations that actually got messages back have their archive/trash/snooze/mute/draft
                // state restored — otherwise a restore would undo changes made since the backup.
                restored.Add(conversation);
                added++;
            }
            done++;
            if (done % 100 == 0 || done == total)
            {
                progress?.Report(new BackupProgress(done, total, 0));
            }
        }
        return (added, skipped, restored);
    }
}
