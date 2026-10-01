using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace ForgeLinkSms.Core.Backup;

public sealed class NotSmsBackupException() : BackupException("This isn't an SMS Backup & Restore file.");

// Reads SMS Backup & Restore's XML. The file is opened twice — once to count messages for the
// Check-backup preview and progress, once to read them — and never copied.
public sealed class SmsBackupXmlReader : IRestoreSource
{
    public const long MaxPartBytes = 100L * 1024 * 1024;

    private readonly Func<Stream> _open;
    private readonly long _maxPartBytes;
    private readonly string _mediaDirectory;
    private readonly List<string> _currentMedia = new();

    public BackupManifest Manifest { get; }
    public AppData AppData { get; } = new();
    /// Attachments over the size limit, which are left out of the restore (counted when the file is opened).
    public int SkippedAttachments { get; }

    private SmsBackupXmlReader(Func<Stream> open, long maxPartBytes, string mediaDirectory, BackupManifest manifest, int skipped)
    {
        _open = open;
        _maxPartBytes = maxPartBytes;
        _mediaDirectory = mediaDirectory;
        Manifest = manifest;
        SkippedAttachments = skipped;
    }

    public static SmsBackupXmlReader Open(Func<Stream> open, string workDirectory, long maxPartBytes = MaxPartBytes)
    {
        var (manifest, skipped) = Scan(open, maxPartBytes);
        var media = Path.Combine(workDirectory, $"import-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(media);
        return new SmsBackupXmlReader(open, maxPartBytes, media, manifest, skipped);
    }

    // The same rule ReadMms uses, so the preview's photo count matches what a restore brings in.
    private static bool IsMediaPart(SmsXmlTag tag) =>
        tag.HadData && tag.Get("ct") is { } ct && ct != "text/plain" && ct != "application/smil";

    private static (BackupManifest, int Skipped) Scan(Func<Stream> open, long maxPartBytes)
    {
        using var stream = open();
        using var xml = new SmsXmlTokenizer(stream, maxPartBytes);
        var root = xml.ReadRoot();
        var created = long.TryParse(root.Get("backup_date"), out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.MinValue;
        int sms = 0, mms = 0, media = 0, skipped = 0;
        while (xml.Next() is { } tag)
        {
            if (tag.IsEnd)
            {
                continue;
            }
            switch (tag.Name)
            {
                case "sms": sms++; break;
                case "mms": mms++; break;
                case "part" when IsMediaPart(tag) && tag.DataTooLarge: skipped++; break;
                case "part" when IsMediaPart(tag): media++; break;
            }
        }
        return (new BackupManifest(0, "SMS Backup & Restore", created, sms, mms, media, 0), skipped);
    }

    public IEnumerable<BackupMessage> ReadMessages(bool includeMedia = true)
    {
        using var stream = _open();
        using var xml = new SmsXmlTokenizer(stream, _maxPartBytes);
        xml.ReadRoot();
        var index = 0;
        while (xml.Next() is { } tag)
        {
            if (tag.IsEnd || (tag.Name != "sms" && tag.Name != "mms"))
            {
                continue;
            }
            DeleteCurrentMedia();
            var message = tag.Name == "sms" ? ReadSms(tag) : ReadMms(xml, tag, includeMedia, ref index);
            if (message is not null)
            {
                yield return message;
            }
        }
        DeleteCurrentMedia();
    }

    private static BackupMessage ReadSms(SmsXmlTag tag)
    {
        var address = tag.Get("address") ?? string.Empty;
        var date = tag.Long("date");
        var sent = tag.Long("date_sent");
        return new BackupMessage(false, new[] { address }, address, date, sent == 0 ? date : sent,
            tag.Get("type") == "2", tag.Get("read") != "0", (int)tag.Long("status"),
            tag.Get("body") ?? string.Empty, null, Array.Empty<BackupAttachment>());
    }

    // Null for an MMS with no participants at all: there's no conversation to restore it into.
    private BackupMessage? ReadMms(SmsXmlTokenizer xml, SmsXmlTag mms, bool includeMedia, ref int index)
    {
        var addresses = (mms.Get("address") ?? string.Empty).Split('~', StringSplitOptions.RemoveEmptyEntries);
        var date = mms.Long("date");
        // Some writers store MMS dates in seconds; anything before 2001 in ms is really seconds.
        if (date is > 0 and < 1_000_000_000_000)
        {
            date *= 1000;
        }
        var dateSent = mms.Long("date_sent") * 1000;
        var outgoing = mms.Get("msg_box") == "2";
        string? from = null;
        var addrRows = new List<string>();
        var body = new List<string>();
        var attachments = new List<BackupAttachment>();

        if (!mms.SelfClosing)
        {
            string? pendingFile = null;
            var nextIndex = index;
            xml.OpenDataSink = includeMedia
                ? () =>
                {
                    pendingFile = Path.Combine(_mediaDirectory, $"m{nextIndex}");
                    return File.Create(pendingFile);
                }
                : null;
            try
            {
                while (xml.Next() is { } tag && !(tag.IsEnd && tag.Name == "mms"))
                {
                    if (tag.IsEnd)
                    {
                        continue;
                    }
                    if (tag.Name == "part")
                    {
                        var ct = tag.Get("ct");
                        if (ct == "text/plain")
                        {
                            if (tag.Get("text") is { } text)
                            {
                                body.Add(text);
                            }
                        }
                        else if (tag.HadData && ct is not null && ct != "application/smil")
                        {
                            if (tag.DataTooLarge)
                            {
                                if (pendingFile is not null)
                                {
                                    File.Delete(pendingFile);
                                }
                            }
                            else
                            {
                                // Listed even when the file wasn't unpacked: duplicate matching compares attachment counts.
                                if (pendingFile is not null)
                                {
                                    _currentMedia.Add(pendingFile);
                                }
                                attachments.Add(new BackupAttachment($"m{nextIndex}", ct, tag.Get("name") ?? tag.Get("cl")));
                                nextIndex++;
                            }
                        }
                        else if (pendingFile is not null)
                        {
                            File.Delete(pendingFile);
                        }
                        pendingFile = null;
                    }
                    else if (tag.Name == "addr" && tag.Get("address") is { } address && address != "insert-address-token")
                    {
                        addrRows.Add(address);
                        if (tag.Get("type") == "137")
                        {
                            from = address;
                        }
                    }
                }
            }
            finally
            {
                xml.OpenDataSink = null;
            }
            index = nextIndex;
        }
        if (addresses.Length == 0)
        {
            addresses = addrRows.Distinct().ToArray();
        }
        if (addresses.Length == 0)
        {
            return null;
        }
        return new BackupMessage(true, addresses, outgoing ? null : from, date, dateSent == 0 ? date : dateSent,
            outgoing, mms.Get("read") != "0", 0, body.Count == 0 ? null : string.Join("\n", body), mms.Get("sub"), attachments);
    }

    // A message's media can be opened only while that message is current: its temp files are
    // deleted as soon as the next message is read, so an import never holds the whole file's media on disk.
    public Stream OpenMedia(string name) => File.OpenRead(Path.Combine(_mediaDirectory, name));

    private void DeleteCurrentMedia()
    {
        foreach (var file in _currentMedia)
        {
            File.Delete(file);
        }
        _currentMedia.Clear();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_mediaDirectory))
            {
                Directory.Delete(_mediaDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class SmsXmlTag
{
    public string Name = string.Empty;
    public bool IsEnd;
    public bool SelfClosing;
    public bool HadData;
    public bool DataTooLarge;
    public readonly Dictionary<string, string> Attributes = new();

    // SMS Backup & Restore writes the literal "null" for a missing value.
    public string? Get(string name) => Attributes.TryGetValue(name, out var v) && v != "null" ? v : null;

    public long Long(string name) =>
        long.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}

// A forward-only reader for the machine-written XML SMS Backup & Restore produces. .NET's XmlReader
// holds each attribute value as one string, and media sits in a base64 "data" attribute — a 92 MB
// video cost ~600 MB and froze the phone. Here "data" is decoded in small chunks straight to a
// sink (or skipped), so memory stays flat however large the attachments are.
internal sealed class SmsXmlTokenizer(Stream stream, long maxPartBytes) : IDisposable
{
    private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
    private int _position;
    private int _length;
    private int _depth;
    private bool _rootClosed;

    public Func<Stream?>? OpenDataSink { get; set; }

    public SmsXmlTag ReadRoot()
    {
        var root = Next();
        if (root is null || root.IsEnd || root.Name != "smses")
        {
            throw new NotSmsBackupException();
        }
        return root;
    }

    public SmsXmlTag? Next()
    {
        while (true)
        {
            int b;
            while ((b = ReadByte()) != '<')
            {
                if (b < 0)
                {
                    if (!_rootClosed)
                    {
                        throw new BackupDamagedException();
                    }
                    return null;
                }
            }
            var next = Peek();
            if (next == '?')
            {
                SkipPast("?>");
                continue;
            }
            if (next == '!')
            {
                ReadByte();
                if (Peek() == '-')
                {
                    SkipPast("-->");
                }
                else
                {
                    SkipPast(">");
                }
                continue;
            }
            if (next == '/')
            {
                ReadByte();
                var tag = new SmsXmlTag { IsEnd = true, Name = ReadName() };
                SkipPast(">");
                if (--_depth == 0)
                {
                    _rootClosed = true;
                }
                return tag;
            }
            return ReadStartTag();
        }
    }

    private SmsXmlTag ReadStartTag()
    {
        var tag = new SmsXmlTag { Name = ReadName() };
        while (true)
        {
            var b = SkipWhitespace();
            if (b == '>')
            {
                _depth++;
                return tag;
            }
            if (b == '/')
            {
                Expect('>');
                tag.SelfClosing = true;
                return tag;
            }
            _position--;
            var name = ReadName('=');
            Expect('=', skipWhitespace: true);
            var quote = SkipWhitespace();
            if (quote != '"' && quote != '\'')
            {
                throw new BackupDamagedException();
            }
            if (name == "data")
            {
                tag.HadData = true;
                tag.DataTooLarge = StreamBase64(quote);
            }
            else
            {
                tag.Attributes[name] = ReadValue(quote);
            }
        }
    }

    private string ReadName(char alsoStopAt = '>')
    {
        var sb = new StringBuilder();
        while (true)
        {
            var b = Peek();
            if (b < 0)
            {
                throw new BackupDamagedException();
            }
            if (b is ' ' or '\t' or '\r' or '\n' or '/' or '>' || b == alsoStopAt)
            {
                return sb.ToString();
            }
            sb.Append((char)ReadByte());
        }
    }

    private string ReadValue(int quote)
    {
        var bytes = new ArrayBufferWriter<byte>(64);
        while (true)
        {
            var b = ReadByte();
            if (b < 0)
            {
                throw new BackupDamagedException();
            }
            if (b == quote)
            {
                return Unescape(Encoding.UTF8.GetString(bytes.WrittenSpan));
            }
            bytes.GetSpan(1)[0] = (byte)b;
            bytes.Advance(1);
        }
    }

    // Returns true when the decoded part is larger than the size limit. Counted from the characters
    // even when nothing is unpacked, so checking and restoring agree on which parts are left out.
    private bool StreamBase64(int quote)
    {
        using var sink = OpenDataSink?.Invoke();
        var chunk = ArrayPool<byte>.Shared.Rent(48 * 1024);
        var decoded = ArrayPool<byte>.Shared.Rent(36 * 1024 + 16);
        long chars = 0;
        var tooLarge = false;
        var count = 0;
        try
        {
            while (true)
            {
                var b = ReadByte();
                if (b < 0)
                {
                    throw new BackupDamagedException();
                }
                if (b == quote)
                {
                    break;
                }
                if (b is ' ' or '\t' or '\r' or '\n')
                {
                    continue;
                }
                chars++;
                tooLarge = tooLarge || chars / 4 * 3 > maxPartBytes;
                if (sink is null || tooLarge)
                {
                    continue;
                }
                chunk[count++] = (byte)b;
                if (count == 48 * 1024)
                {
                    // The last 4 characters are held back: if the data ends exactly here they may carry
                    // "=" padding, which is only valid in the final block.
                    Decode(chunk, count - 4, decoded, sink, isFinal: false, out var leftover);
                    leftover += 4;
                    Array.Copy(chunk, count - leftover, chunk, 0, leftover);
                    count = leftover;
                }
            }
            if (sink is not null && !tooLarge && count > 0)
            {
                Decode(chunk, count, decoded, sink, isFinal: true, out _);
            }
            return tooLarge;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
            ArrayPool<byte>.Shared.Return(decoded);
        }
    }

    private static int Decode(byte[] chunk, int count, byte[] decoded, Stream sink, bool isFinal, out int leftover)
    {
        var status = Base64.DecodeFromUtf8(chunk.AsSpan(0, count), decoded, out var consumed, out var written, isFinal);
        if (status == OperationStatus.InvalidData || (isFinal && status != OperationStatus.Done))
        {
            throw new BackupDamagedException(new FormatException("Invalid base64 in an attachment."));
        }
        sink.Write(decoded, 0, written);
        leftover = count - consumed;
        return written;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('&'))
        {
            return value;
        }
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var end = c == '&' ? value.IndexOf(';', i) : -1;
            if (end < 0)
            {
                sb.Append(c);
                continue;
            }
            var entity = value[(i + 1)..end];
            string? replacement = entity switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" => "'",
                _ when entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(entity[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) => FromCodePoint(hex),
                _ when entity.StartsWith('#')
                    && int.TryParse(entity[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var dec) => FromCodePoint(dec),
                _ => null
            };
            if (replacement is null)
            {
                sb.Append(c);
                continue;
            }
            sb.Append(replacement);
            i = end;
        }
        return sb.ToString();
    }

    // SMS Backup & Restore writes an emoji as two references, one per UTF-16 half (&#55357;&#56832;);
    // each half is appended as-is so the pair forms naturally. Anything out of range stays as written.
    private static string? FromCodePoint(int code) => code switch
    {
        >= 0xD800 and <= 0xDFFF => ((char)code).ToString(),
        >= 0 and <= 0x10FFFF => char.ConvertFromUtf32(code),
        _ => null
    };

    private int SkipWhitespace()
    {
        int b;
        do
        {
            b = ReadByte();
        }
        while (b is ' ' or '\t' or '\r' or '\n');
        if (b < 0)
        {
            throw new BackupDamagedException();
        }
        return b;
    }

    private void Expect(char expected, bool skipWhitespace = false)
    {
        var b = skipWhitespace ? SkipWhitespace() : ReadByte();
        if (b != expected)
        {
            throw new BackupDamagedException();
        }
    }

    private void SkipPast(string terminator)
    {
        var matched = 0;
        while (matched < terminator.Length)
        {
            var b = ReadByte();
            if (b < 0)
            {
                throw new BackupDamagedException();
            }
            matched = b == terminator[matched] ? matched + 1 : (b == terminator[0] ? 1 : 0);
        }
    }

    private int Peek()
    {
        if (_position == _length && !Fill())
        {
            return -1;
        }
        return _buffer[_position];
    }

    private int ReadByte()
    {
        if (_position == _length && !Fill())
        {
            return -1;
        }
        return _buffer[_position++];
    }

    private bool Fill()
    {
        _length = stream.Read(_buffer, 0, _buffer.Length);
        _position = 0;
        return _length > 0;
    }

    public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);
}
