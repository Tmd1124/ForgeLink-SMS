using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Backup;

// Photos and videos are already compressed, so media is stored as-is; only the JSON is deflated.
public sealed partial class BackupWriter : IDisposable
{
    public const int Format = 1;

    private readonly Stream? _crypto;
    private readonly ZipArchive _zip;

    public int MediaCount { get; private set; }

    public BackupWriter(Stream destination, string? password, int iterations = BackupCrypto.Iterations)
    {
        _crypto = string.IsNullOrEmpty(password) ? null : BackupCrypto.CreateEncryptingStream(destination, password, iterations);
        _zip = new ZipArchive(_crypto ?? destination, ZipArchiveMode.Create, leaveOpen: true);
    }

    public string AddMedia(Stream content, string extension)
    {
        var ext = extension.Trim().TrimStart('.').ToLowerInvariant();
        var name = $"media/{++MediaCount}.{(SafeExtension().IsMatch(ext) ? ext : "bin")}";
        using var entry = _zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
        content.CopyTo(entry);
        return name;
    }

    public void Finish(BackupManifest manifest, IEnumerable<BackupMessage> messages, AppData appData)
    {
        WriteText("manifest.json", BackupJson.Serialize(manifest));
        using (var writer = new StreamWriter(_zip.CreateEntry("messages.jsonl", CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
        {
            foreach (var message in messages)
            {
                writer.WriteLine(BackupJson.Serialize(message));
            }
        }
        WriteText("forgelink.json", BackupJson.Serialize(appData));
    }

    private void WriteText(string name, string text)
    {
        using var writer = new StreamWriter(_zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    public void Dispose()
    {
        _zip.Dispose();
        _crypto?.Dispose();
    }

    [GeneratedRegex("^[a-z0-9]{1,5}$")]
    private static partial Regex SafeExtension();
}
