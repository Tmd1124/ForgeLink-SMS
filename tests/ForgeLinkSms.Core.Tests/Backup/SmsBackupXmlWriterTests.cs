using System.Xml.Linq;
using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class SmsBackupXmlWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeSource(IReadOnlyList<SourceMessage> messages) : IBackupSource
    {
        public Task<AppData> ReadAppDataAsync() => Task.FromResult(new AppData());
        public int CountMessages() => messages.Count;
        public IEnumerable<SourceMessage> ReadMessages() => messages;
    }

    private static SourceMessage Sms(long ms, string body, bool outgoing = false, string address = "+14045550199") =>
        new(new BackupMessage(false, new[] { address }, address, ms, ms + 5, outgoing, true, 0, body, null, Array.Empty<BackupAttachment>()),
            Array.Empty<SourceAttachment>());

    private static SourceMessage GroupPhoto(long ms) =>
        new(new BackupMessage(true, new[] { "+14045550199", "+17705550101" }, "+17705550101", ms, ms, false, false, 0, "Look", null, Array.Empty<BackupAttachment>()),
            new[] { new SourceAttachment("image/jpeg", "IMG_7.jpg", () => new MemoryStream(new byte[] { 1, 2, 3, 250 })) });

    private static async Task<XDocument> Export(params SourceMessage[] messages)
    {
        var output = new MemoryStream();
        await SmsBackupXmlWriter.RunAsync(new FakeSource(messages), output, Now, a => a.Count > 1 ? "Kim, Sam" : "Kim", null, CancellationToken.None);
        output.Position = 0;
        return XDocument.Load(output);
    }

    [Fact]
    public async Task Writes_the_root_and_an_sms_row_like_sms_backup_and_restore()
    {
        var doc = await Export(Sms(1_790_800_000_000, "Hi", outgoing: true));

        var root = doc.Root!;
        Assert.Equal("smses", root.Name.LocalName);
        Assert.Equal("1", (string?)root.Attribute("count"));
        Assert.Equal("full", (string?)root.Attribute("type"));
        var sms = Assert.Single(root.Elements("sms"));
        Assert.Equal("+14045550199", (string?)sms.Attribute("address"));
        Assert.Equal("1790800000000", (string?)sms.Attribute("date"));
        Assert.Equal("1790800000005", (string?)sms.Attribute("date_sent"));
        Assert.Equal("2", (string?)sms.Attribute("type"));
        Assert.Equal("Hi", (string?)sms.Attribute("body"));
        Assert.Equal("1", (string?)sms.Attribute("read"));
        Assert.Equal("null", (string?)sms.Attribute("subject"));
        Assert.Equal("Kim", (string?)sms.Attribute("contact_name"));
    }

    [Fact]
    public async Task Writes_a_group_mms_with_text_and_media_parts_and_sender_addr()
    {
        var doc = await Export(GroupPhoto(1_790_800_123_000));

        var mms = Assert.Single(doc.Root!.Elements("mms"));
        Assert.Equal("1790800123000", (string?)mms.Attribute("date"));
        Assert.Equal("1790800123", (string?)mms.Attribute("date_sent"));
        Assert.Equal("1", (string?)mms.Attribute("msg_box"));
        Assert.Equal("132", (string?)mms.Attribute("m_type"));
        Assert.Equal("+14045550199~+17705550101", (string?)mms.Attribute("address"));
        var parts = mms.Element("parts")!.Elements("part").ToList();
        Assert.Equal("text/plain", (string?)parts[0].Attribute("ct"));
        Assert.Equal("Look", (string?)parts[0].Attribute("text"));
        Assert.Equal("image/jpeg", (string?)parts[1].Attribute("ct"));
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3, 250 }), (string?)parts[1].Attribute("data"));
        var addrs = mms.Element("addrs")!.Elements("addr").ToList();
        Assert.Contains(addrs, a => (string?)a.Attribute("address") == "+17705550101" && (string?)a.Attribute("type") == "137");
        Assert.Contains(addrs, a => (string?)a.Attribute("address") == "+14045550199" && (string?)a.Attribute("type") == "151");
    }

    [Fact]
    public async Task Special_characters_and_emoji_survive()
    {
        var doc = await Export(Sms(1, "5 < 6 & \"quotes\" 🎉"));

        Assert.Equal("5 < 6 & \"quotes\" 🎉", (string?)doc.Root!.Element("sms")!.Attribute("body"));
    }

    [Fact]
    public async Task A_lone_surrogate_in_a_message_is_dropped_not_fatal()
    {
        var doc = await Export(Sms(1, "bad \uD83D here 😀"));

        Assert.Equal("bad  here 😀", (string?)doc.Root!.Element("sms")!.Attribute("body"));
    }

    [Fact]
    public async Task Characters_xml_cannot_hold_are_dropped_not_fatal()
    {
        var doc = await Export(Sms(1, "a\u0001b\u0008c"));

        Assert.Equal("abc", (string?)doc.Root!.Element("sms")!.Attribute("body"));
    }

    // The phone's message source only knows each chat's participants after loading app data; without
    // it, MMS were exported with the user's own number as a participant and never matched on import.
    private sealed class OrderCheckingSource : IBackupSource
    {
        public bool AppDataLoaded;
        public bool ReadBeforeAppData;
        public Task<AppData> ReadAppDataAsync()
        {
            AppDataLoaded = true;
            return Task.FromResult(new AppData());
        }
        public int CountMessages() => 1;
        public IEnumerable<SourceMessage> ReadMessages()
        {
            ReadBeforeAppData = !AppDataLoaded;
            yield return Sms(1, "Hi");
        }
    }

    [Fact]
    public async Task App_data_is_loaded_before_messages_are_read()
    {
        var source = new OrderCheckingSource();

        await SmsBackupXmlWriter.RunAsync(source, new MemoryStream(), Now, null, null, CancellationToken.None);

        Assert.True(source.AppDataLoaded);
        Assert.False(source.ReadBeforeAppData);
    }

    [Fact]
    public void Names_the_file_so_the_other_app_lists_it()
    {
        Assert.Equal("sms-20261001123456.xml", SmsBackupXmlWriter.FileNameFor(new DateTime(2026, 10, 1, 12, 34, 56)));
    }
}
