# SMS Backup & Restore Export/Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Export all texts and MMS media to an SMS Backup & Restore XML file, and import such a file through the existing restore flow.

**Architecture:** Two new Core classes speak the format — `SmsBackupXmlWriter` (from the existing `IBackupSource` message stream) and `SmsBackupXmlReader` (to `BackupMessage`s plus media temp files). A common `IRestoreSource` lets `RestoreRunner` run on either a `.flbackup` `BackupReader` or the XML reader, so duplicate matching, inserting, preview and the WorkManager jobs are reused. Android adds an `ExportWorker` and makes the restore picker/worker recognize XML.

**Tech Stack:** .NET 10 (System.Xml `XmlWriter`/`XmlReader`), MAUI Blazor Hybrid Android, WorkManager, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-30-sms-backup-restore-xml-design.md`

## Global Constraints

- Format exactly as in the spec (root `<smses count backup_set backup_date type="full">`, `<sms>` / `<mms>` / `<parts>` / `<addrs>`, literal `null` for missing values, `~`-joined MMS addresses, addr 137 = sender, 151 = recipient, `mms.date` ms, `mms.date_sent` seconds).
- File name `sms-YYYYMMDDHHmmss.xml`; UTF-8 without BOM.
- Import adds messages/media only; no settings/favorites/schedules; same duplicate rule as Restore.
- Media part over 100 MB (decoded) is skipped and counted.
- Errors: "This isn't an SMS Backup & Restore file." / "This file is damaged or incomplete."
- No commits unless the user asks; no comments unless the WHY is non-obvious.
- Device testing never deletes real messages and sends nothing.

## Review Focus

1. Very large exports (thousands of MMS): writer must stream attachments, never buffer a whole message's media as a string (Task 2 writes base64 via `XmlWriter.WriteBase64` in chunks).
2. Import temp media must not accumulate to the size of the whole file (Task 3 deletes the previous message's temp files when advancing; test `Temp_media_files_are_removed_as_reading_moves_on`).
3. Files written by the real SMS Backup & Restore app: `null` strings, missing optional attributes, `text/plain` parts with `text=`, SMIL parts (`application/smil`) that must be ignored (Task 3 sample test).
4. Characters XML can't carry raw (`<&">`, emoji, control characters like U+0001 in bodies): writer must not throw on invalid XML chars (Task 2 strips them; test `Characters_xml_cannot_hold_are_dropped_not_fatal`).
5. Picking a file that's neither format must fail cleanly before anything is inserted (Task 1 detection + Task 3 root check).

---

### Task 1: Tell backup files apart

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/BackupFileKind.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/BackupFileKindTests.cs`

**Interfaces:**
- Produces: `enum BackupFileKind { Unknown, FlBackup, EncryptedFlBackup, SmsBackupXml }`; `static class BackupFileKinds { BackupFileKind Detect(ReadOnlySpan<byte> head) }` — needs up to the first 64 bytes.

- [ ] **Step 1: Failing tests**

```csharp
using System.Text;
using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupFileKindTests
{
    [Theory]
    [InlineData("FLBK\u0001", BackupFileKind.EncryptedFlBackup)]
    [InlineData("PK\u0003\u0004", BackupFileKind.FlBackup)]
    [InlineData("<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>", BackupFileKind.SmsBackupXml)]
    [InlineData("﻿<?xml version=\"1.0\"?>", BackupFileKind.SmsBackupXml)]
    [InlineData("  \r\n<smses count=\"1\">", BackupFileKind.SmsBackupXml)]
    [InlineData("BEGIN:VCALENDAR", BackupFileKind.Unknown)]
    [InlineData("", BackupFileKind.Unknown)]
    public void Detects_the_kind_from_the_first_bytes(string head, BackupFileKind expected)
    {
        Assert.Equal(expected, BackupFileKinds.Detect(Encoding.UTF8.GetBytes(head)));
    }
}
```

- [ ] **Step 2: RED** — `dotnet test tests/ForgeLinkSms.Core.Tests --filter BackupFileKindTests` → compile error.

- [ ] **Step 3: Implement**

```csharp
using System.Text;

namespace ForgeLinkSms.Core.Backup;

public enum BackupFileKind
{
    Unknown,
    FlBackup,
    EncryptedFlBackup,
    SmsBackupXml
}

public static class BackupFileKinds
{
    public static BackupFileKind Detect(ReadOnlySpan<byte> head)
    {
        if (BackupCrypto.IsEncrypted(head))
        {
            return BackupFileKind.EncryptedFlBackup;
        }
        if (head.Length >= 2 && head[0] == (byte)'P' && head[1] == (byte)'K')
        {
            return BackupFileKind.FlBackup;
        }
        var text = Encoding.UTF8.GetString(head).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith("<?xml", StringComparison.Ordinal) || text.StartsWith("<smses", StringComparison.Ordinal)
            ? BackupFileKind.SmsBackupXml
            : BackupFileKind.Unknown;
    }
}
```

(If `BackupCrypto.IsEncrypted` takes `byte[]`, pass `head.ToArray()`.)

- [ ] **Step 4: GREEN** — same filter passes.

---

### Task 2: `SmsBackupXmlWriter`

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/SmsBackupXmlWriter.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/SmsBackupXmlWriterTests.cs`

**Interfaces:**
- Consumes: `IBackupSource` (`CountMessages()`, `ReadMessages()` → `SourceMessage(BackupMessage, IReadOnlyList<SourceAttachment>)`), `BackupProgress`.
- Produces: `static Task<ExportResult> SmsBackupXmlWriter.RunAsync(IBackupSource source, Stream output, DateTimeOffset now, Func<IReadOnlyList<string>, string?>? contactName, IProgress<BackupProgress>? progress, CancellationToken ct)`; `record ExportResult(int Sms, int Mms, int MediaFiles)`; `static string SmsBackupXmlWriter.FileNameFor(DateTime local)` → `sms-yyyyMMddHHmmss.xml`.

- [ ] **Step 1: Failing tests** (fake source like `RunnerTests.FakeSource`)

```csharp
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
    public async Task Characters_xml_cannot_hold_are_dropped_not_fatal()
    {
        var doc = await Export(Sms(1, "a\u0001b\u0008c"));

        Assert.Equal("abc", (string?)doc.Root!.Element("sms")!.Attribute("body"));
    }

    [Fact]
    public void Names_the_file_so_the_other_app_lists_it()
    {
        Assert.Equal("sms-20261001123456.xml", SmsBackupXmlWriter.FileNameFor(new DateTime(2026, 10, 1, 12, 34, 56)));
    }
}
```

- [ ] **Step 2: RED** — compile error on `SmsBackupXmlWriter`.

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Text;
using System.Xml;

namespace ForgeLinkSms.Core.Backup;

public sealed record ExportResult(int Sms, int Mms, int MediaFiles);

// Writes SMS Backup & Restore's XML so other Android SMS apps can read ForgeLink's messages.
public static class SmsBackupXmlWriter
{
    private const string Null = "null";

    public static string FileNameFor(DateTime local) => $"sms-{local:yyyyMMddHHmmss}.xml";

    public static async Task<ExportResult> RunAsync(IBackupSource source, Stream output, DateTimeOffset now,
        Func<IReadOnlyList<string>, string?>? contactName, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, Async = false, CheckCharacters = false };
        using var xml = XmlWriter.Create(output, settings);
        xml.WriteProcessingInstruction("xml", "version='1.0' encoding='UTF-8' standalone='yes'");
        var total = source.CountMessages();
        xml.WriteStartElement("smses");
        xml.WriteAttributeString("count", total.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("backup_set", Guid.NewGuid().ToString());
        xml.WriteAttributeString("backup_date", now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("type", "full");

        int sms = 0, mms = 0, media = 0, done = 0;
        foreach (var item in source.ReadMessages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var m = item.Message;
            var name = contactName?.Invoke(m.Addresses) ?? Null;
            if (m.IsMms)
            {
                media += WriteMms(xml, item, name);
                mms++;
            }
            else
            {
                WriteSms(xml, m, name);
                sms++;
            }
            done++;
            if (done % 100 == 0 || done == total)
            {
                progress?.Report(new BackupProgress(done, total, media));
            }
        }
        xml.WriteEndElement();
        xml.Flush();
        await output.FlushAsync(cancellationToken);
        return new ExportResult(sms, mms, media);
    }

    private static void WriteSms(XmlWriter xml, BackupMessage m, string contactName)
    {
        xml.WriteStartElement("sms");
        Attr(xml, "protocol", "0");
        Attr(xml, "address", m.From ?? m.Addresses.FirstOrDefault() ?? string.Empty);
        Attr(xml, "date", Ms(m.TimestampMs));
        Attr(xml, "type", m.Outgoing ? "2" : "1");
        Attr(xml, "subject", Null);
        Attr(xml, "body", Clean(m.Body ?? string.Empty));
        Attr(xml, "toa", Null);
        Attr(xml, "sc_toa", Null);
        Attr(xml, "service_center", Null);
        Attr(xml, "read", m.Read ? "1" : "0");
        Attr(xml, "status", m.Status.ToString(CultureInfo.InvariantCulture));
        Attr(xml, "locked", "0");
        Attr(xml, "date_sent", Ms(m.DateSentMs));
        Attr(xml, "sub_id", "-1");
        Attr(xml, "readable_date", Readable(m.TimestampMs));
        Attr(xml, "contact_name", Clean(contactName));
        xml.WriteEndElement();
    }

    private static int WriteMms(XmlWriter xml, SourceMessage item, string contactName)
    {
        var m = item.Message;
        xml.WriteStartElement("mms");
        Attr(xml, "date", Ms(m.TimestampMs));
        Attr(xml, "date_sent", (m.DateSentMs / 1000).ToString(CultureInfo.InvariantCulture));
        Attr(xml, "ct_t", "application/vnd.wap.multipart.related");
        Attr(xml, "msg_box", m.Outgoing ? "2" : "1");
        Attr(xml, "m_type", m.Outgoing ? "128" : "132");
        Attr(xml, "address", string.Join("~", m.Addresses));
        Attr(xml, "read", m.Read ? "1" : "0");
        Attr(xml, "seen", "1");
        Attr(xml, "sub", string.IsNullOrEmpty(m.Subject) ? Null : Clean(m.Subject));
        Attr(xml, "text_only", item.Attachments.Count == 0 ? "1" : "0");
        Attr(xml, "locked", "0");
        Attr(xml, "sub_id", "-1");
        Attr(xml, "readable_date", Readable(m.TimestampMs));
        Attr(xml, "contact_name", Clean(contactName));

        xml.WriteStartElement("parts");
        var seq = 0;
        if (!string.IsNullOrEmpty(m.Body))
        {
            xml.WriteStartElement("part");
            Attr(xml, "seq", (seq++).ToString(CultureInfo.InvariantCulture));
            Attr(xml, "ct", "text/plain");
            Attr(xml, "name", Null);
            Attr(xml, "chset", "106");
            Attr(xml, "cl", "text0.txt");
            Attr(xml, "text", Clean(m.Body));
            xml.WriteEndElement();
        }
        foreach (var attachment in item.Attachments)
        {
            xml.WriteStartElement("part");
            Attr(xml, "seq", (seq++).ToString(CultureInfo.InvariantCulture));
            Attr(xml, "ct", attachment.ContentType);
            Attr(xml, "name", Clean(attachment.FileName ?? Null));
            Attr(xml, "chset", Null);
            Attr(xml, "cl", Clean(attachment.FileName ?? Null));
            Attr(xml, "text", Null);
            // Streamed through base64 in chunks so a large video is never held as one string.
            xml.WriteStartAttribute("data");
            using (var stream = attachment.Open())
            {
                var buffer = new byte[48 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    xml.WriteBase64(buffer, 0, read);
                }
            }
            xml.WriteEndAttribute();
            xml.WriteEndElement();
        }
        xml.WriteEndElement();

        xml.WriteStartElement("addrs");
        foreach (var address in m.Addresses)
        {
            var isSender = m.Outgoing ? false : address == m.From;
            xml.WriteStartElement("addr");
            Attr(xml, "address", address);
            Attr(xml, "type", isSender ? "137" : "151");
            Attr(xml, "charset", "106");
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndElement();
        return item.Attachments.Count;
    }

    private static void Attr(XmlWriter xml, string name, string value) => xml.WriteAttributeString(name, value);

    private static string Ms(long ms) => ms.ToString(CultureInfo.InvariantCulture);

    private static string Readable(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("MMM d, yyyy h:mm:ss tt", CultureInfo.InvariantCulture);

    // XML 1.0 can't carry most control characters; a stray one in a text must not fail the export.
    private static string Clean(string text)
    {
        if (text.All(XmlConvert.IsXmlChar) || !text.Any(c => !char.IsSurrogate(c) && !XmlConvert.IsXmlChar(c)))
        {
            return text;
        }
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[++i]);
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: GREEN** — `--filter SmsBackupXmlWriterTests` passes. If `WriteBase64` inside an attribute is rejected by the writer, fall back to `Convert.ToBase64String` per 48 KiB-multiple chunk written with `WriteString` (base64 of 3-byte-aligned chunks concatenates correctly) and ledger a ruling.

---

### Task 3: `SmsBackupXmlReader`

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/SmsBackupXmlReader.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/SmsBackupXmlReaderTests.cs`

**Interfaces:**
- Consumes: Task 1 kinds; `BackupDamagedException`; `BackupException`.
- Produces: `sealed class SmsBackupXmlReader : IDisposable` with `static SmsBackupXmlReader Open(Stream source, string workDirectory)` (copies to a temp file, pre-scans counts, validates the root), `BackupManifest Manifest` (Format 0, AppVersion "SMS Backup & Restore", CreatedUtc from `backup_date`, SmsCount, MmsCount, MediaFiles, Conversations 0), `AppData AppData` (empty), `IEnumerable<BackupMessage> ReadMessages()`, `Stream OpenMedia(string name)`, `int SkippedAttachments`, `const long MaxPartBytes = 100 * 1024 * 1024`. `class NotSmsBackupException : BackupException("This isn't an SMS Backup & Restore file.")`.

- [ ] **Step 1: Failing tests**

```csharp
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

    private SmsBackupXmlReader Open(string xml) =>
        SmsBackupXmlReader.Open(new MemoryStream(Encoding.UTF8.GetBytes(xml)), _work);

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
        var messages = reader.ReadMessages().ToList();

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
        var mms = reader.ReadMessages().Single(m => m.IsMms);

        Assert.Equal(new[] { "+14045550199", "+17705550101" }, mms.Addresses);
        Assert.Equal("+17705550101", mms.From);
        Assert.Equal(1790700120000L, mms.TimestampMs);
        Assert.Equal(1790700119000L, mms.DateSentMs);
        Assert.Equal("Look", mms.Body);
        Assert.False(mms.Read);
        var photo = Assert.Single(mms.Attachments);
        Assert.Equal(("image/jpeg", "IMG_7.jpg"), (photo.ContentType, photo.FileName));
        using var media = reader.OpenMedia(photo.Media);
        var bytes = new MemoryStream();
        media.CopyTo(bytes);
        Assert.Equal(new byte[] { 1, 2, 3, 250 }, bytes.ToArray());
    }

    [Fact]
    public void Temp_media_files_are_removed_as_reading_moves_on()
    {
        var twoPhotos = Sample.Replace("</smses>", Sample[Sample.IndexOf("<mms", StringComparison.Ordinal)..Sample.IndexOf("</smses>", StringComparison.Ordinal)] + "</smses>");
        using var reader = Open(twoPhotos);
        var seen = new List<string>();

        foreach (var message in reader.ReadMessages().Where(m => m.IsMms))
        {
            seen.Add(message.Attachments[0].Media);
        }

        Assert.Equal(2, seen.Count);
        Assert.Throws<FileNotFoundException>(() => reader.OpenMedia(seen[0]).Dispose());
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

        Assert.Throws<BackupDamagedException>(() => reader.ReadMessages().ToList());
    }
}
```

- [ ] **Step 2: RED** — compile error on `SmsBackupXmlReader`.

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Xml;

namespace ForgeLinkSms.Core.Backup;

public sealed class NotSmsBackupException() : BackupException("This isn't an SMS Backup & Restore file.");

// Reads SMS Backup & Restore's XML. The file is copied to a temp file first so it can be scanned
// once for counts (for the Check-backup preview and progress) and then read again for messages.
public sealed class SmsBackupXmlReader : IDisposable
{
    public const long MaxPartBytes = 100L * 1024 * 1024;

    private readonly string _path;
    private readonly string _mediaDirectory;
    private readonly List<string> _currentMedia = new();

    public BackupManifest Manifest { get; }
    public AppData AppData { get; } = new();
    public int SkippedAttachments { get; private set; }

    private SmsBackupXmlReader(string path, string mediaDirectory, BackupManifest manifest)
    {
        _path = path;
        _mediaDirectory = mediaDirectory;
        Manifest = manifest;
    }

    public static SmsBackupXmlReader Open(Stream source, string workDirectory)
    {
        Directory.CreateDirectory(workDirectory);
        var path = Path.Combine(workDirectory, $"import-{Guid.NewGuid():N}.xml");
        using (var file = File.Create(path))
        {
            source.CopyTo(file);
        }
        try
        {
            var manifest = Scan(path);
            var media = Path.Combine(workDirectory, $"import-media-{Guid.NewGuid():N}");
            Directory.CreateDirectory(media);
            return new SmsBackupXmlReader(path, media, manifest);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private static XmlReader CreateReader(string path) =>
        XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true, CheckCharacters = false });

    private static BackupManifest Scan(string path)
    {
        int sms = 0, mms = 0, media = 0;
        DateTimeOffset created = DateTimeOffset.MinValue;
        try
        {
            using var xml = CreateReader(path);
            if (!xml.ReadToFollowing("smses") && !IsRoot(xml))
            {
                throw new NotSmsBackupException();
            }
            if (long.TryParse(xml.GetAttribute("backup_date"), out var ms))
            {
                created = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                switch (xml.LocalName)
                {
                    case "sms": sms++; break;
                    case "mms": mms++; break;
                    case "part" when IsMedia(xml.GetAttribute("ct")): media++; break;
                }
            }
        }
        catch (XmlException e)
        {
            throw new BackupDamagedException(e);
        }
        return new BackupManifest(0, "SMS Backup & Restore", created, sms, mms, media, 0);
    }

    private static bool IsRoot(XmlReader xml) => xml.NodeType == XmlNodeType.Element && xml.LocalName == "smses";

    private static bool IsMedia(string? contentType) =>
        contentType is not null && contentType != "text/plain" && contentType != "application/smil";

    public IEnumerable<BackupMessage> ReadMessages()
    {
        using var xml = CreateReader(_path);
        var index = 0;
        while (true)
        {
            BackupMessage? next;
            try
            {
                if (!xml.Read())
                {
                    break;
                }
                if (xml.NodeType != XmlNodeType.Element || (xml.LocalName != "sms" && xml.LocalName != "mms"))
                {
                    continue;
                }
                DeleteCurrentMedia();
                next = xml.LocalName == "sms" ? ReadSms(xml) : ReadMms(xml, ref index);
            }
            catch (XmlException e)
            {
                throw new BackupDamagedException(e);
            }
            catch (FormatException e)
            {
                throw new BackupDamagedException(e);
            }
            yield return next;
        }
        DeleteCurrentMedia();
    }

    private static BackupMessage ReadSms(XmlReader xml)
    {
        var address = Value(xml, "address") ?? string.Empty;
        var date = Long(xml, "date");
        var sent = Long(xml, "date_sent");
        return new BackupMessage(false, new[] { address }, address, date, sent == 0 ? date : sent,
            Value(xml, "type") == "2", Value(xml, "read") != "0", (int)Long(xml, "status"),
            Value(xml, "body") ?? string.Empty, null, Array.Empty<BackupAttachment>());
    }

    private BackupMessage ReadMms(XmlReader xml, ref int index)
    {
        var addresses = (Value(xml, "address") ?? string.Empty).Split('~', StringSplitOptions.RemoveEmptyEntries);
        var date = Long(xml, "date");
        // Some writers store MMS dates in seconds; anything before 2001 in ms is really seconds.
        if (date is > 0 and < 1_000_000_000_000)
        {
            date *= 1000;
        }
        var dateSent = Long(xml, "date_sent") * 1000;
        var outgoing = Value(xml, "msg_box") == "2";
        var read = Value(xml, "read") != "0";
        var subject = Value(xml, "sub");
        string? from = null;
        var body = new List<string>();
        var attachments = new List<BackupAttachment>();

        using var inner = xml.ReadSubtree();
        while (inner.Read())
        {
            if (inner.NodeType != XmlNodeType.Element)
            {
                continue;
            }
            if (inner.LocalName == "part")
            {
                var ct = Value(inner, "ct");
                if (ct == "text/plain")
                {
                    if (Value(inner, "text") is { } text)
                    {
                        body.Add(text);
                    }
                }
                else if (IsMedia(ct) && inner.GetAttribute("data") is { } data)
                {
                    if (data.Length / 4L * 3 > MaxPartBytes)
                    {
                        SkippedAttachments++;
                        continue;
                    }
                    var name = $"m{index++}";
                    var file = Path.Combine(_mediaDirectory, name);
                    File.WriteAllBytes(file, Convert.FromBase64String(data));
                    _currentMedia.Add(file);
                    attachments.Add(new BackupAttachment(name, ct!, Value(inner, "name") ?? Value(inner, "cl")));
                }
            }
            else if (inner.LocalName == "addr" && Value(inner, "type") == "137")
            {
                from = Value(inner, "address");
            }
        }
        return new BackupMessage(true, addresses, outgoing ? null : from, date, dateSent == 0 ? date : dateSent,
            outgoing, read, 0, body.Count == 0 ? null : string.Join("\n", body), subject, attachments);
    }

    public Stream OpenMedia(string name) => File.OpenRead(Path.Combine(_mediaDirectory, name));

    private void DeleteCurrentMedia()
    {
        foreach (var file in _currentMedia)
        {
            File.Delete(file);
        }
        _currentMedia.Clear();
    }

    private static string? Value(XmlReader xml, string name) => xml.GetAttribute(name) is { } v && v != "null" ? v : null;

    private static long Long(XmlReader xml, string name) =>
        long.TryParse(Value(xml, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_mediaDirectory))
            {
                Directory.Delete(_mediaDirectory, recursive: true);
            }
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}
```

Note: `ReadToFollowing("smses")` returns false for a wrong root → `NotSmsBackupException`; a file that fails to parse at all throws `XmlException` → damaged. A non-XML file never reaches here (Task 1 detection).

- [ ] **Step 4: GREEN** — `--filter SmsBackupXmlReaderTests` passes.

- [ ] **Step 5: Round trip test** (append to `SmsBackupXmlReaderTests`): export with `SmsBackupXmlWriter.RunAsync` from a fake source (SMS in/out with emoji, group MMS with text + photo), `Open` the output, and assert every `BackupMessage` field (addresses, From for incoming, times, direction, read, body) and the media bytes match. Run: GREEN.

---

### Task 4: `IRestoreSource` and the runner

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/IRestoreSource.cs`
- Modify: `src/ForgeLinkSms.Core/Backup/BackupReader.cs` (implement `IRestoreSource`), `src/ForgeLinkSms.Core/Backup/SmsBackupXmlReader.cs` (implement), `src/ForgeLinkSms.Core/Backup/RestoreRunner.cs` (parameter type `BackupReader reader` → `IRestoreSource reader` in `RunAsync`, `PreviewAsync`, `WalkAsync`)
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/RunnerTests.cs`

**Interfaces:**
- Produces: `interface IRestoreSource : IDisposable { BackupManifest Manifest { get; } AppData AppData { get; } IEnumerable<BackupMessage> ReadMessages(); Stream OpenMedia(string name); }`

- [ ] **Step 1: Failing test** (in `RunnerTests`, using its `FakeTarget`)

```csharp
    [Fact]
    public async Task An_sms_backup_and_restore_file_restores_through_the_same_runner()
    {
        var target = new FakeTarget();
        target.Existing.Add(new ExistingMessage("4045550199", 1_000, false, "one", 0));
        var output = new MemoryStream();
        await SmsBackupXmlWriter.RunAsync(new FakeSource(new[] { Sms(1_000, "one", "4045550199"), Sms(2_000, "two", "4045550199"), GroupPhoto(3_000) }, new AppData()),
            output, Now, null, null, CancellationToken.None);
        using var reader = SmsBackupXmlReader.Open(new MemoryStream(output.ToArray()), _work);

        var preview = await RestoreRunner.PreviewAsync(reader, target, CancellationToken.None);
        var result = await RestoreRunner.RunAsync(reader, target, false, false, Now, null, CancellationToken.None);

        Assert.Equal((2, 1), (preview.WouldAdd, preview.AlreadyPresent));
        Assert.Equal(new RestoreResult(2, 1), result);
        Assert.Equal(new byte[] { 9, 8, 7 }, target.MediaSeen.Single());
    }
```

- [ ] **Step 2: RED** — compile error (`RestoreRunner` takes `BackupReader`).
- [ ] **Step 3: Implement** the interface, add `: IRestoreSource` to both readers, change the three runner signatures.
- [ ] **Step 4: GREEN** — full Core suite passes.

---

### Task 5: Android — export job, XML-aware restore, Settings

**Files:**
- Create: `src/ForgeLinkSms/Platforms/Android/Backup/ExportWorker.cs`
- Modify: `src/ForgeLinkSms.Core/Services/IBackupService.cs`, `src/ForgeLinkSms/Platforms/Android/Backup/BackupService.cs`, `src/ForgeLinkSms/Platforms/Android/Backup/RestoreWorker.cs`, `src/ForgeLinkSms/Pages/Settings/SettingsPage.razor`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: `RestoreFile(string Uri, string FileName, bool IsEncrypted, bool IsSmsBackupXml = false)`; `IBackupService.ExportForOtherAppsAsync() : Task<bool>`.

- [ ] **Step 1: `IBackupService`** — add `bool IsSmsBackupXml = false` to `RestoreFile`; add `Task<bool> ExportForOtherAppsAsync();` with a one-line doc comment.
- [ ] **Step 2: `BackupService.PickRestoreFileAsync`** — read up to 64 bytes (loop until 64 or EOF), `var kind = BackupFileKinds.Detect(head)`; if `Unknown`, return a `RestoreFile` with `IsSmsBackupXml = false, IsEncrypted = false` (the preview then reports the error as today); set `IsEncrypted = kind == EncryptedFlBackup`, `IsSmsBackupXml = kind == SmsBackupXml`.
- [ ] **Step 3: One opener for both kinds** — in `BackupService` add `internal static IRestoreSource OpenRestoreSource(Stream input, bool isXml, string? password) => isXml ? SmsBackupXmlReader.Open(input, BackupFiles.RestoreWorkDirectory) : BackupReader.Open(input, password, BackupFiles.RestoreWorkDirectory);` and use it in `PreviewRestoreAsync` (pass `file.IsSmsBackupXml`) and `RestoreWorker` (new input flag `"xml"` set by `StartRestoreAsync`).
- [ ] **Step 4: `ExportWorker`** — copy `BackupWorker`'s structure (foreground info override, `TryForeground`, throttled progress with title "Exporting messages", cancel/cleanup, `SaveStatus`), but: target is always the input `uri`; run `SmsBackupXmlWriter.RunAsync(new AndroidBackupSource(...), output, DateTimeOffset.UtcNow, ContactNames, progress, CancellationToken.None)` where `ContactNames` looks up each address with `IContactService.LookupAsync(...).GetAwaiter().GetResult()?.DisplayName` joined by ", " and cached per address; success status "Exported N messages and M photos/videos for other apps."; notification title "Export complete".
- [ ] **Step 5: `BackupService.ExportForOtherAppsAsync`** — `ACTION_CREATE_DOCUMENT`, type `text/xml`, title `SmsBackupXmlWriter.FileNameFor(DateTime.Now)`; on a picked uri enqueue `ExportWorker` as unique work `"forgelink-export"` with `data.PutString("uri", …)`; return false if cancelled.
- [ ] **Step 6: Settings** — under "Back up now", add `<button @onclick="ExportForOtherApps" style="@PresetStyle">📤 Export for other apps (SMS Backup &amp; Restore)</button>` with a hint line "Saves texts and photos as a file SMS Backup & Restore and other apps can import." In the restore sheet: hide the password field and the settings/scheduled checkboxes when `restoreFile.IsSmsBackupXml`; title "Import @restoreFile.FileName?" for XML. Handler: `private async Task ExportForOtherApps() { await Backups.ExportForOtherAppsAsync(); RefreshBackup(); }`.
- [ ] **Step 7: Build** — Release build succeeds; full Core suite green.

---

### Task 6: Device verification

- [ ] Install. Settings → Export for other apps → save to Downloads. Wait for "Export complete".
- [ ] `adb shell head -c 600 /sdcard/Download/sms-*.xml` shows the XML declaration and `<smses count="…">` matching the phone's message count; `adb shell ls -l` shows a plausible size.
- [ ] Restore from a backup → pick the exported `.xml` → no password field → Check backup → "Restoring would add 0 messages". Cancel (do not import).
- [ ] Pick a non-backup file (e.g. an `.ics`) → Check backup shows "This isn't an SMS Backup & Restore file." or the damaged message; nothing imported.
- [ ] Leave the exported file in Downloads and tell the user (large; theirs to delete).
