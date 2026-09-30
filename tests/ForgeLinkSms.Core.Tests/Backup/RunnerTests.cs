using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class RunnerTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"flbk-run-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    private sealed class FakeSource(IReadOnlyList<SourceMessage> messages, AppData data) : IBackupSource
    {
        public Task<AppData> ReadAppDataAsync() => Task.FromResult(data);
        public int CountMessages() => messages.Count;
        public IEnumerable<SourceMessage> ReadMessages() => messages;
    }

    private sealed class FakeTarget : IRestoreTarget
    {
        public List<ExistingMessage> Existing { get; } = new();
        public List<BackupMessage> Inserted { get; } = new();
        public List<byte[]> MediaSeen { get; } = new();
        public List<MergePlan> Plans { get; } = new();
        public AppData Current { get; set; } = new();

        public Task<IEnumerable<ExistingMessage>> ReadExistingMessagesAsync() => Task.FromResult<IEnumerable<ExistingMessage>>(Existing.ToList());
        public Task<AppData> ReadAppDataAsync() => Task.FromResult(Current);

        public Task InsertSmsAsync(BackupMessage message)
        {
            Inserted.Add(message);
            Existing.Add(new ExistingMessage(ConversationKey.From(message.Addresses), message.TimestampMs, message.Outgoing, message.Body, 0));
            return Task.CompletedTask;
        }

        public Task InsertMmsAsync(BackupMessage message, Func<string, Stream> openMedia)
        {
            Inserted.Add(message);
            foreach (var a in message.Attachments)
            {
                using var s = openMedia(a.Media);
                var ms = new MemoryStream();
                s.CopyTo(ms);
                MediaSeen.Add(ms.ToArray());
            }
            Existing.Add(new ExistingMessage(ConversationKey.From(message.Addresses), message.TimestampMs, message.Outgoing, message.Body, message.Attachments.Count));
            return Task.CompletedTask;
        }

        public Task ApplyAsync(MergePlan plan)
        {
            Plans.Add(plan);
            Current = Current with { Favorites = Current.Favorites.Concat(plan.FavoritesToAdd).ToList() };
            return Task.CompletedTask;
        }
    }

    private static SourceMessage Sms(long ms, string body, params string[] addresses) =>
        new(new BackupMessage(false, addresses, null, ms, ms, false, true, 0, body, null, Array.Empty<BackupAttachment>()), Array.Empty<SourceAttachment>());

    private static SourceMessage GroupPhoto(long ms) =>
        new(new BackupMessage(true, new[] { "4045550199", "7705550101" }, "7705550101", ms, ms, false, true, 0, "Look", null, Array.Empty<BackupAttachment>()),
            new[] { new SourceAttachment("image/jpeg", "IMG_7.JPG", () => new MemoryStream(new byte[] { 9, 8, 7 })) });

    private async Task<byte[]> BackUp(string? password, CancellationToken ct = default, List<BackupProgress>? progress = null)
    {
        var source = new FakeSource(new[] { Sms(1_000, "one", "4045550199"), Sms(2_000, "two", "4045550199"), GroupPhoto(3_000) }, new AppData { Favorites = new[] { "4045550199" } });
        var output = new MemoryStream();
        await BackupRunner.RunAsync(source, output, password, "1.0", Now, progress is null ? null : new SyncProgress(progress), ct, iterations: 1_000);
        return output.ToArray();
    }

    private sealed class SyncProgress(List<BackupProgress> sink) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => sink.Add(value);
    }

    [Fact]
    public async Task Backup_records_counts_and_media()
    {
        using var reader = BackupReader.Open(new MemoryStream(await BackUp(null)), null, _work);

        Assert.Equal((2, 1, 1, 2), (reader.Manifest.SmsCount, reader.Manifest.MmsCount, reader.Manifest.MediaFiles, reader.Manifest.Conversations));
        var photo = reader.ReadMessages().Single(m => m.IsMms);
        Assert.Equal("media/1.jpg", photo.Attachments[0].Media);
        Assert.Equal("7705550101", photo.From);
    }

    [Fact]
    public async Task Restore_adds_missing_messages_skips_existing_and_carries_media()
    {
        var target = new FakeTarget();
        target.Existing.Add(new ExistingMessage("4045550199", 1_000, false, "one", 0));
        using var reader = BackupReader.Open(new MemoryStream(await BackUp("pw")), "pw", _work);

        var result = await RestoreRunner.RunAsync(reader, target, includeSettings: false, includeScheduled: false, Now, null, CancellationToken.None);

        Assert.Equal(new RestoreResult(2, 1), result);
        Assert.Equal(new byte[] { 9, 8, 7 }, target.MediaSeen.Single());
        Assert.Equal(new[] { "4045550199" }, target.Plans.Single().FavoritesToAdd);
    }

    [Fact]
    public async Task Restoring_the_same_backup_twice_adds_nothing_the_second_time()
    {
        var target = new FakeTarget();
        var bytes = await BackUp(null);
        using (var first = BackupReader.Open(new MemoryStream(bytes), null, _work))
        {
            await RestoreRunner.RunAsync(first, target, false, false, Now, null, CancellationToken.None);
        }
        using var second = BackupReader.Open(new MemoryStream(bytes), null, _work);

        var result = await RestoreRunner.RunAsync(second, target, false, false, Now, null, CancellationToken.None);

        Assert.Equal(new RestoreResult(0, 3), result);
        Assert.Empty(target.Plans[1].FavoritesToAdd);
    }

    [Fact]
    public async Task Preview_counts_what_a_restore_would_add_without_writing_anything()
    {
        var target = new FakeTarget();
        target.Existing.Add(new ExistingMessage("4045550199", 1_000, false, "one", 0));
        var bytes = await BackUp(null);

        RestorePreview preview;
        using (var reader = BackupReader.Open(new MemoryStream(bytes), null, _work))
        {
            preview = await RestoreRunner.PreviewAsync(reader, target, CancellationToken.None);
        }

        Assert.Equal(new RestorePreview(Now, 3, 1, 2, 1, 0), preview);
        Assert.Empty(target.Inserted);
        Assert.Empty(target.Plans);

        using var again = BackupReader.Open(new MemoryStream(bytes), null, _work);
        Assert.Equal(new RestoreResult(preview.WouldAdd, preview.AlreadyPresent), await RestoreRunner.RunAsync(again, target, false, false, Now, null, CancellationToken.None));
    }

    private async Task<byte[]> BackUpWith(AppData data)
    {
        var source = new FakeSource(new[] { Sms(1_000, "one", "4045550199") }, data);
        var output = new MemoryStream();
        await BackupRunner.RunAsync(source, output, null, "1.0", Now, null, CancellationToken.None, iterations: 1_000);
        return output.ToArray();
    }

    [Fact]
    public async Task Conversation_states_are_not_reapplied_to_conversations_that_gained_no_messages()
    {
        var target = new FakeTarget();
        target.Existing.Add(new ExistingMessage("4045550199", 1_000, false, "one", 0));
        using var reader = BackupReader.Open(new MemoryStream(await BackUpWith(new AppData { Trashed = new[] { "4045550199" }, Archived = new[] { "4045550199" } })), null, _work);

        await RestoreRunner.RunAsync(reader, target, false, false, Now, null, CancellationToken.None);

        Assert.Empty(target.Plans.Single().TrashToApply);
        Assert.Empty(target.Plans.Single().ArchiveToApply);
    }

    [Fact]
    public async Task Preview_counts_scheduled_texts_that_a_restore_could_re_add()
    {
        var scheduled = new BackupScheduled("4045550199", "4045550199", "Practice at 6", Now.AddDays(2), "");
        using var reader = BackupReader.Open(new MemoryStream(await BackUpWith(new AppData { Scheduled = new[] { scheduled } })), null, _work);

        var preview = await RestoreRunner.PreviewAsync(reader, new FakeTarget(), CancellationToken.None);

        Assert.Equal(1, preview.ScheduledTexts);
    }

    [Fact]
    public async Task Backup_reports_progress_to_the_end()
    {
        var progress = new List<BackupProgress>();

        await BackUp(null, progress: progress);

        Assert.Equal(new BackupProgress(3, 3, 1), progress[^1]);
    }

    [Fact]
    public async Task Cancelling_stops_the_backup()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BackUp(null, cts.Token));
    }
}
