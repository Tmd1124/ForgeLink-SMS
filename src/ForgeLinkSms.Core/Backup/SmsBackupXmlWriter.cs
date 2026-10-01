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
        // The format has no place for app data, but the phone's source resolves each chat's participants
        // while loading it; without this, MMS carry the user's own number and never match on import.
        await source.ReadAppDataAsync();
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

    private static bool NeedsCleaning(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (!XmlConvert.IsXmlChar(c))
            {
                return true;
            }
        }
        return false;
    }

    // XML 1.0 can't carry most control characters or half an emoji; a stray one in a text must not fail the export.
    private static string Clean(string text)
    {
        if (!NeedsCleaning(text))
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
