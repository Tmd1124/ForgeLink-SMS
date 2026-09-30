using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupArchiveTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"flbk-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    private static readonly BackupManifest Manifest = new(BackupWriter.Format, "1.0", DateTimeOffset.UnixEpoch, 1, 1, 1, 2);

    private static byte[] Build(string? password, BackupManifest? manifest = null)
    {
        var output = new MemoryStream();
        using (var writer = new BackupWriter(output, password, iterations: 1_000))
        {
            var media = writer.AddMedia(new MemoryStream(new byte[] { 1, 2, 3, 4 }), "JPG");
            writer.Finish(manifest ?? Manifest, new[]
            {
                new BackupMessage(false, new[] { "4045550199" }, null, 1000, 1000, false, true, 0, "hi", null, Array.Empty<BackupAttachment>()),
                new BackupMessage(true, new[] { "4045550199" }, "4045550199", 2000, 2000, false, true, 0, null, null, new[] { new BackupAttachment(media, "image/jpeg", "a.jpg") })
            }, new AppData { QuickReplies = new[] { "On my way" } });
        }
        return output.ToArray();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pw")]
    public void Round_trips_messages_media_and_app_data(string? password)
    {
        using var reader = BackupReader.Open(new MemoryStream(Build(password)), password, _work);

        Assert.Equal(2, reader.Manifest.Conversations);
        var messages = reader.ReadMessages().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("media/1.jpg", messages[1].Attachments[0].Media);
        using var media = reader.OpenMedia(messages[1].Attachments[0].Media);
        var bytes = new MemoryStream();
        media.CopyTo(bytes);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes.ToArray());
        Assert.Equal(new[] { "On my way" }, reader.AppData.QuickReplies);
    }

    [Fact]
    public void Encrypted_backup_without_a_password_asks_for_one()
    {
        var e = Assert.Throws<BackupPasswordException>(() => BackupReader.Open(new MemoryStream(Build("pw")), null, _work));
        Assert.True(e.Required);
    }

    [Fact]
    public void Newer_format_is_refused()
    {
        var bytes = Build(null, Manifest with { Format = BackupWriter.Format + 1 });

        Assert.Throws<BackupTooNewException>(() => BackupReader.Open(new MemoryStream(bytes), null, _work));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(200)]
    public void Empty_short_or_garbage_files_are_damaged(int length)
    {
        var garbage = Enumerable.Repeat((byte)'x', length).ToArray();

        Assert.Throws<BackupDamagedException>(() => BackupReader.Open(new MemoryStream(garbage), null, _work));
    }

    [Fact]
    public void Truncated_plain_backup_is_damaged()
    {
        var bytes = Build(null);

        Assert.Throws<BackupDamagedException>(() => BackupReader.Open(new MemoryStream(bytes[..(bytes.Length / 2)]), null, _work));
    }

    [Fact]
    public void Temp_files_are_removed_after_use_and_after_failure()
    {
        using (BackupReader.Open(new MemoryStream(Build(null)), null, _work)) { }
        Assert.Throws<BackupPasswordException>(() => BackupReader.Open(new MemoryStream(Build("pw")), "wrong", _work));

        Assert.Empty(Directory.GetFiles(_work));
    }

    [Fact]
    public void Media_names_outside_the_media_folder_are_refused()
    {
        using var reader = BackupReader.Open(new MemoryStream(Build(null)), null, _work);

        Assert.Throws<BackupDamagedException>(() => reader.OpenMedia("forgelink.json"));
        Assert.Throws<BackupDamagedException>(() => reader.OpenMedia("media/999.jpg"));
    }
}
