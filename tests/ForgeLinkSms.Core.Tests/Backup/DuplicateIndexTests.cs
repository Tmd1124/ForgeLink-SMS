using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class DuplicateIndexTests
{
    private static BackupMessage Sms(long ms, string body = "hi", bool outgoing = false, params string[] addresses) =>
        new(false, addresses.Length == 0 ? new[] { "4045550199" } : addresses, null, ms, ms, outgoing, true, 0, body, null, Array.Empty<BackupAttachment>());

    private static DuplicateIndex IndexWith(params ExistingMessage[] existing)
    {
        var index = new DuplicateIndex();
        foreach (var e in existing)
        {
            index.Add(e);
        }
        return index;
    }

    [Theory]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_900, true)]
    [InlineData(999_200, true)]
    [InlineData(1_002_500, false)]
    public void Matches_within_about_a_second(long backupMs, bool expected)
    {
        var index = IndexWith(new ExistingMessage("4045550199", 1_000_000, false, "hi", 0));

        Assert.Equal(expected, index.Contains(Sms(backupMs)));
    }

    [Fact]
    public void Direction_body_and_conversation_all_matter()
    {
        var index = IndexWith(new ExistingMessage("4045550199", 1_000_000, false, "hi", 0));

        Assert.False(index.Contains(Sms(1_000_000, outgoing: true)));
        Assert.False(index.Contains(Sms(1_000_000, body: "hello")));
        Assert.False(index.Contains(Sms(1_000_000, "hi", false, "7705550101")));
        Assert.True(index.Contains(Sms(1_000_000, " hi ", false, "+1 404 555 0199")));
    }

    [Fact]
    public void Picture_messages_compare_attachment_count()
    {
        var index = IndexWith(new ExistingMessage("4045550199", 1_000_000, false, null, 1));
        var one = new BackupMessage(true, new[] { "4045550199" }, "4045550199", 1_000_000, 1_000_000, false, true, 0, null, null, new[] { new BackupAttachment("media/1.jpg", "image/jpeg", null) });

        Assert.True(index.Contains(one));
        Assert.False(index.Contains(one with { Attachments = Array.Empty<BackupAttachment>() }));
    }

    [Fact]
    public void Group_messages_match_regardless_of_participant_order()
    {
        var index = IndexWith(new ExistingMessage(ConversationKey.From(new[] { "4045550199", "7705550101" }), 1_000_000, true, "all", 0));

        Assert.True(index.Contains(Sms(1_000_000, "all", true, "7705550101", "4045550199")));
    }
}

// Measures retained memory, so it must not run alongside other tests that allocate.
[CollectionDefinition(nameof(DuplicateIndexMemoryTests), DisableParallelization = true)]
public class DuplicateIndexMemoryCollection;

[Collection(nameof(DuplicateIndexMemoryTests))]
public class DuplicateIndexMemoryTests
{
    private static BackupMessage Sms(long ms, string body) =>
        new(false, new[] { "4045550199" }, null, ms, ms, false, true, 0, body, null, Array.Empty<BackupAttachment>());

    // A phone with years of texts would otherwise hold every message body twice while restoring.
    [Fact]
    public void The_index_keeps_no_message_text()
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var index = new DuplicateIndex();
        for (var i = 0; i < 10_000; i++)
        {
            index.Add(new ExistingMessage("4045550199", i * 10_000L, false, i + new string('x', 2_000), 0));
        }
        var grown = GC.GetTotalMemory(forceFullCollection: true) - before;

        Assert.True(grown < 5_000_000, $"index grew by {grown:N0} bytes");
        Assert.True(index.Contains(Sms(70_000, 7 + new string('x', 2_000))));
        Assert.False(index.Contains(Sms(70_000, 8 + new string('x', 2_000))));
        GC.KeepAlive(index);
    }
}
