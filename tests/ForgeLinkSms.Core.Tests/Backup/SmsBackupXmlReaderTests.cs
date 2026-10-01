using System.Text;
using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class SmsBackupXmlReaderTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"smsbr-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    // Layout copied from a file written by SMS Backup & Restore 10.x.
    private const string Sample = """
        <?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
        <!--File Created By SMS Backup & Restore v10.20.002-->
        <smses count="3" backup_set="4f0c" backup_date="1790800000000" type="full">
          <sms protocol="0" address="+14045550199" date="1790700000000" type="1" subject="null" body="Hi &amp; bye" toa="null" sc_toa="null" service_center="null" read="1" status="-1" locked="0" date_sent="1790699999000" sub_id="1" readable_date="Sep 29, 2026 8:40:00 AM" contact_name="Kim" />
          <sms protocol="0" address="+14045550199" date="1790700060000" type="2" subject="null" body="Ok" toa="null" sc_toa="null" service_center="null" read="1" status="-1" locked="0" date_sent="0" sub_id="1" readable_date="…" contact_name="Kim" />
          <mms date="1790700120000" rr="null" sub="null" ct_t="application/vnd.wap.multipart.related" read_status="null" seen="1" msg_box="1" address="+14045550199~+17705550101" sub_cs="null" resp_st="null" retr_st="null" d_tm="null" text_only="0" exp="null" locked="0" m_id="x" st="null" retr_txt_cs="null" retr_txt="null" creator="null" date_sent="1790700119" read="0" m_size="1234" rpt_a="null" ct_cls="null" pri="null" sub_id="1" tr_id="y" resp_txt="null" ct_l="null" m_cls="personal" d_rpt="null" v="18" _id="77" m_type="132" readable_date="…" contact_name="Kim, Sam">
            <parts>
              <part seq="-1" ct="application/smil" name="null" chset="null" cd="null" fn="null" cid="&lt;smil&gt;" cl="smil.xml" ctt_s="null" ctt_t="null" text="&lt;smil&gt;&lt;/smil&gt;" />
              <part seq="0" ct="text/plain" name="null" chset="106" cd="null" fn="null" cid="&lt;text0&gt;" cl="text0.txt" ctt_s="null" ctt_t="null" text="Look" />
              <part seq="1" ct="image/jpeg" name="IMG_7.jpg" chset="null" cd="null" fn="null" cid="&lt;image1&gt;" cl="IMG_7.jpg" ctt_s="null" ctt_t="null" data="AQID+g==" />
            </parts>
            <addrs>
              <addr address="+17705550101" type="137" charset="106" />
              <addr address="+14045550199" type="151" charset="106" />
            </addrs>
          </mms>
        </smses>
        """;

    private SmsBackupXmlReader Open(string xml)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        return SmsBackupXmlReader.Open(() => new MemoryStream(bytes), _work);
    }

    // 36,863 bytes encode to exactly 49,152 base64 characters ending in "=": the padding then lands
    // exactly on the reader's chunk boundary (found in a real export).
    [Theory]
    [InlineData(36_863)]
    [InlineData(36_864)]
    [InlineData(73_727)]
    [InlineData(1)]
    public void Attachments_of_every_size_decode_including_padding_on_a_chunk_boundary(int size)
    {
        var photo = new byte[size];
        new Random(size).NextBytes(photo);
        var xml = Sample.Replace("AQID+g==", Convert.ToBase64String(photo));
        using var reader = Open(xml);
        var bytes = new MemoryStream();

        foreach (var message in reader.ReadMessages(includeMedia: true).Where(m => m.IsMms))
        {
            using var media = reader.OpenMedia(message.Attachments[0].Media);
            media.CopyTo(bytes);
        }

        Assert.Equal(photo, bytes.ToArray());
    }

    private const string Root = "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?><smses count=\"1\" backup_date=\"1\" type=\"full\">";

    // SMS Backup & Restore writes emoji as one character reference per UTF-16 half.
    [Fact]
    public void Emoji_written_as_surrogate_character_references_read_correctly()
    {
        using var reader = Open(Root + "<sms address=\"+14045550199\" date=\"1\" type=\"1\" body=\"Hi &#55357;&#56832; &#128512; &#1114112; &#-5;\" read=\"1\" /></smses>");

        Assert.Equal("Hi 😀 😀 &#1114112; &#-5;", reader.ReadMessages().Single().Body);
    }

    [Fact]
    public void A_part_over_the_size_limit_is_left_out_the_same_way_when_checking_and_restoring()
    {
        var xml = Root + "<mms date=\"1790700120000\" msg_box=\"1\" address=\"+14045550199\" read=\"1\"><parts>"
            + "<part ct=\"video/mp4\" name=\"big.mp4\" data=\"" + Convert.ToBase64String(new byte[30]) + "\" />"
            + "<part ct=\"image/jpeg\" name=\"small.jpg\" data=\"AQID\" /></parts></mms></smses>";
        var bytes = Encoding.UTF8.GetBytes(xml);
        using var reader = SmsBackupXmlReader.Open(() => new MemoryStream(bytes), _work, maxPartBytes: 10);

        var checking = reader.ReadMessages(includeMedia: false).Single();
        var restoring = reader.ReadMessages(includeMedia: true).Single();

        Assert.Equal(new[] { "small.jpg" }, checking.Attachments.Select(a => a.FileName));
        Assert.Equal(new[] { "small.jpg" }, restoring.Attachments.Select(a => a.FileName));
        Assert.Equal(1, reader.Manifest.MediaFiles);
        Assert.Equal(1, reader.SkippedAttachments);
    }

    [Fact]
    public void An_mms_without_an_address_list_uses_its_addr_rows_and_one_with_none_is_left_out()
    {
        using var reader = Open(Root
            + "<mms date=\"1790700120000\" msg_box=\"1\" address=\"null\" read=\"1\"><parts><part ct=\"text/plain\" text=\"Hi\" /></parts>"
            + "<addrs><addr address=\"+17705550101\" type=\"137\" /><addr address=\"insert-address-token\" type=\"151\" /><addr address=\"+14045550199\" type=\"151\" /></addrs></mms>"
            + "<mms date=\"1790700130000\" msg_box=\"1\" address=\"\" read=\"1\"><parts><part ct=\"text/plain\" text=\"Lost\" /></parts></mms></smses>");

        var message = Assert.Single(reader.ReadMessages());

        Assert.Equal(new[] { "+17705550101", "+14045550199" }, message.Addresses);
        Assert.Equal("Hi", message.Body);
    }

    // A real export held a 92 MB video; reading one as a single XML attribute took ~600 MB and froze the phone.
    [Fact]
    public void A_large_video_is_streamed_to_disk_not_held_in_memory()
    {
        var video = new byte[60 * 1024 * 1024];
        new Random(7).NextBytes(video);
        var path = Path.Combine(Path.GetTempPath(), $"big-{Guid.NewGuid():N}.xml");
        try
        {
            using (var file = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                file.Write("<?xml version='1.0' encoding='UTF-8' standalone='yes' ?><smses count=\"1\" backup_date=\"1\" type=\"full\">");
                file.Write("<mms date=\"1790700120000\" msg_box=\"1\" address=\"+14045550199\" date_sent=\"0\" read=\"1\" m_type=\"132\"><parts>");
                file.Write("<part seq=\"0\" ct=\"video/mp4\" name=\"clip.mp4\" data=\"");
                file.Write(Convert.ToBase64String(video));
                file.Write("\" /></parts><addrs><addr address=\"+14045550199\" type=\"137\" charset=\"106\" /></addrs></mms></smses>");
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            using var reader = SmsBackupXmlReader.Open(() => File.OpenRead(path), _work);
            long length = 0;
            foreach (var message in reader.ReadMessages(includeMedia: true))
            {
                using var media = reader.OpenMedia(message.Attachments[0].Media);
                length = media.Length;
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(video.Length, length);
            Assert.True(allocated < 8_000_000, $"reading allocated {allocated:N0} bytes");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Counts_and_date_come_from_a_pre_scan()
    {
        using var reader = Open(Sample);

        Assert.Equal((2, 1, 1), (reader.Manifest.SmsCount, reader.Manifest.MmsCount, reader.Manifest.MediaFiles));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790800000000), reader.Manifest.CreatedUtc);
    }

    [Fact]
    public void Reads_sms_rows_with_direction_times_and_text()
    {
        using var reader = Open(Sample);
        var messages = reader.ReadMessages(includeMedia: true).ToList();

        var received = messages[0];
        Assert.False(received.IsMms);
        Assert.Equal(new[] { "+14045550199" }, received.Addresses);
        Assert.Equal("+14045550199", received.From);
        Assert.Equal((1790700000000L, 1790699999000L, false, true, "Hi & bye"),
            (received.TimestampMs, received.DateSentMs, received.Outgoing, received.Read, received.Body));
        Assert.True(messages[1].Outgoing);
    }

    [Fact]
    public void Reads_a_group_mms_with_its_text_sender_and_photo_ignoring_smil()
    {
        using var reader = Open(Sample);
        var bytes = new MemoryStream();
        BackupMessage? mms = null;
        foreach (var message in reader.ReadMessages(includeMedia: true))
        {
            if (message.IsMms)
            {
                mms = message;
                using var media = reader.OpenMedia(message.Attachments[0].Media);
                media.CopyTo(bytes);
            }
        }
        Assert.NotNull(mms);

        Assert.Equal(new[] { "+14045550199", "+17705550101" }, mms.Addresses);
        Assert.Equal("+17705550101", mms.From);
        Assert.Equal(1790700120000L, mms.TimestampMs);
        Assert.Equal(1790700119000L, mms.DateSentMs);
        Assert.Equal("Look", mms.Body);
        Assert.False(mms.Read);
        var photo = Assert.Single(mms.Attachments);
        Assert.Equal(("image/jpeg", "IMG_7.jpg"), (photo.ContentType, photo.FileName));
        Assert.Equal(new byte[] { 1, 2, 3, 250 }, bytes.ToArray());
    }

    [Fact]
    public void Temp_media_files_are_removed_as_reading_moves_on()
    {
        var twoPhotos = Sample.Replace("</smses>", Sample[Sample.IndexOf("<mms", StringComparison.Ordinal)..Sample.IndexOf("</smses>", StringComparison.Ordinal)] + "</smses>");
        using var reader = Open(twoPhotos);
        var seen = new List<string>();

        foreach (var message in reader.ReadMessages(includeMedia: true).Where(m => m.IsMms))
        {
            seen.Add(message.Attachments[0].Media);
        }

        Assert.Equal(2, seen.Count);
        Assert.Throws<FileNotFoundException>(() => reader.OpenMedia(seen[0]).Dispose());
    }

    private sealed class FakeSource(IReadOnlyList<SourceMessage> messages) : IBackupSource
    {
        public Task<AppData> ReadAppDataAsync() => Task.FromResult(new AppData());
        public int CountMessages() => messages.Count;
        public IEnumerable<SourceMessage> ReadMessages() => messages;
    }

    private static void AssertSame(BackupMessage expected, BackupMessage actual)
    {
        Assert.Equal(expected.Addresses, actual.Addresses);
        Assert.Equal(expected with { Addresses = [], Attachments = [] }, actual with { Addresses = [], Attachments = [] });
    }

    [Fact]
    public async Task What_forgelink_exports_reads_back_the_same()
    {
        var incoming = new BackupMessage(false, new[] { "+14045550199" }, "+14045550199", 1_790_700_000_000, 1_790_699_999_000, false, true, 0, "Hi 🎉 <&>", null, Array.Empty<BackupAttachment>());
        var outgoing = incoming with { Outgoing = true, From = "+14045550199", TimestampMs = 1_790_700_060_000, DateSentMs = 1_790_700_060_000, Body = "Ok" };
        var group = new BackupMessage(true, new[] { "+14045550199", "+17705550101" }, "+17705550101", 1_790_700_120_000, 1_790_700_119_000, false, false, 0, "Look", null, Array.Empty<BackupAttachment>());
        var output = new MemoryStream();
        await SmsBackupXmlWriter.RunAsync(new FakeSource(new[]
        {
            new SourceMessage(incoming, Array.Empty<SourceAttachment>()),
            new SourceMessage(outgoing, Array.Empty<SourceAttachment>()),
            new SourceMessage(group, new[] { new SourceAttachment("image/jpeg", "IMG_7.jpg", () => new MemoryStream(new byte[] { 5, 6, 7 })) })
        }), output, DateTimeOffset.UtcNow, null, null, CancellationToken.None);

        using var reader = SmsBackupXmlReader.Open(() => new MemoryStream(output.ToArray()), _work);
        var read = new List<BackupMessage>();
        byte[]? photo = null;
        foreach (var message in reader.ReadMessages(includeMedia: true))
        {
            read.Add(message);
            if (message.IsMms)
            {
                using var media = reader.OpenMedia(message.Attachments[0].Media);
                var bytes = new MemoryStream();
                media.CopyTo(bytes);
                photo = bytes.ToArray();
            }
        }

        AssertSame(incoming, read[0]);
        AssertSame(outgoing, read[1]);
        AssertSame(group, read[2]);
        Assert.Equal(("image/jpeg", "IMG_7.jpg"), (read[2].Attachments[0].ContentType, read[2].Attachments[0].FileName));
        Assert.Equal(new byte[] { 5, 6, 7 }, photo);
    }

    [Fact]
    public void A_file_that_is_not_an_sms_backup_is_rejected()
    {
        var error = Assert.Throws<NotSmsBackupException>(() => Open("<?xml version='1.0'?><calendar/>"));
        Assert.Equal("This isn't an SMS Backup & Restore file.", error.Message);
    }

    [Fact]
    public void A_truncated_file_is_reported_as_damaged()
    {
        Assert.Throws<BackupDamagedException>(() => Open(Sample[..(Sample.Length / 2)]));
    }

    [Fact]
    public void Bad_base64_is_reported_as_damaged_when_read()
    {
        using var reader = Open(Sample.Replace("AQID+g==", "not base64!"));

        Assert.Throws<BackupDamagedException>(() => reader.ReadMessages(includeMedia: true).ToList());
    }
}
