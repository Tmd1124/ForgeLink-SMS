using System.IO.Compression;

namespace ForgeLinkSms.Core.Backup;

// Zip reading needs to seek, and picked files arrive as forward-only streams (and encrypted ones must
// be decrypted first), so the archive is copied into a temp file that Dispose removes.
public sealed class BackupReader : IRestoreSource
{
    private readonly string _tempPath;
    private readonly FileStream _file;
    private readonly ZipArchive _zip;

    public BackupManifest Manifest { get; }
    public int SkippedAttachments => 0;
    public AppData AppData { get; }

    private BackupReader(string tempPath, FileStream file, ZipArchive zip, BackupManifest manifest, AppData appData)
    {
        _tempPath = tempPath;
        _file = file;
        _zip = zip;
        Manifest = manifest;
        AppData = appData;
    }

    public static BackupReader Open(Stream source, string? password, string workDirectory)
    {
        Directory.CreateDirectory(workDirectory);
        var tempPath = Path.Combine(workDirectory, $"restore-{Guid.NewGuid():N}.zip");
        FileStream? file = null;
        ZipArchive? zip = null;
        try
        {
            var peek = new byte[4];
            var peeked = ReadUpTo(source, peek);
            Stream input = new PrefixedStream(peek.AsMemory(0, peeked), source);
            if (BackupCrypto.IsEncrypted(peek.AsSpan(0, peeked)))
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new BackupPasswordException(required: true);
                }
                input = BackupCrypto.CreateDecryptingStream(input, password);
            }
            using (var output = File.Create(tempPath))
            {
                input.CopyTo(output);
            }

            file = File.OpenRead(tempPath);
            zip = new ZipArchive(file, ZipArchiveMode.Read);
            var manifest = BackupJson.Deserialize<BackupManifest>(ReadText(zip, "manifest.json"));
            if (manifest.Format > BackupWriter.Format)
            {
                throw new BackupTooNewException();
            }
            var appData = BackupJson.Deserialize<AppData>(ReadText(zip, "forgelink.json"));
            return new BackupReader(tempPath, file, zip, manifest, appData);
        }
        catch (Exception e)
        {
            zip?.Dispose();
            file?.Dispose();
            TryDelete(tempPath);
            if (e is BackupException)
            {
                throw;
            }
            throw new BackupDamagedException(e);
        }
    }

    public IEnumerable<BackupMessage> ReadMessages(bool includeMedia = true)
    {
        using var reader = new StreamReader(Entry(_zip, "messages.jsonl").Open());
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                yield return BackupJson.Deserialize<BackupMessage>(line);
            }
        }
    }

    public Stream OpenMedia(string name)
    {
        if (!name.StartsWith("media/", StringComparison.Ordinal) || name.Contains(".."))
        {
            throw new BackupDamagedException();
        }
        return Entry(_zip, name).Open();
    }

    public void Dispose()
    {
        _zip.Dispose();
        _file.Dispose();
        TryDelete(_tempPath);
    }

    private static ZipArchiveEntry Entry(ZipArchive zip, string name) => zip.GetEntry(name) ?? throw new BackupDamagedException();

    private static string ReadText(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(Entry(zip, name).Open());
        return reader.ReadToEnd();
    }

    private static int ReadUpTo(Stream source, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = source.Read(buffer, total, buffer.Length - total);
            if (n == 0)
            {
                break;
            }
            total += n;
        }
        return total;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    // Puts the peeked bytes back in front of a forward-only stream.
    private sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream rest) : Stream
    {
        private ReadOnlyMemory<byte> _prefix = prefix;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> destination)
        {
            if (_prefix.Length > 0)
            {
                var n = Math.Min(destination.Length, _prefix.Length);
                _prefix.Span[..n].CopyTo(destination);
                _prefix = _prefix[n..];
                return n;
            }
            return rest.Read(destination);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
