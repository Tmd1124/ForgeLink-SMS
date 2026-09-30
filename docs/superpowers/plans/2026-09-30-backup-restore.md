# Backup and Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Back up all texts, picture messages with media, and ForgeLink's data and settings to a `.flbackup` file (manually or weekly, optionally password-encrypted) and restore it without deleting or duplicating anything.

**Architecture:** All file-format, crypto, duplicate, merge, retention and orchestration logic lives in `ForgeLinkSms.Core/Backup` and is unit-tested against fakes. The Android side supplies an `IBackupSource` (reads the SMS/MMS provider and ForgeLink repositories), an `IRestoreTarget` (writes them back), WorkManager workers that run the jobs in the foreground with a progress notification, and a small activity-result bridge for the system file/folder pickers.

**Tech Stack:** .NET 10 MAUI Blazor Hybrid (Android), `System.IO.Compression` (zip), `System.Security.Cryptography` (AES-GCM, PBKDF2), `System.Text.Json`, xUnit + Moq, new dependency `Xamarin.AndroidX.Work.Runtime` 2.11.2.1.

**Spec:** `docs/superpowers/specs/2026-09-30-backup-restore-design.md`

## Global Constraints

- Backup contents: SMS, MMS with media, and ForgeLink data and settings.
- File name `ForgeLink-backup-YYYY-MM-DD-HHmm.flbackup`; weekly retention keeps the newest 4 and deletes only files matching that pattern.
- Encryption: optional password; `FLBK` magic, version 1, 16-byte salt, PBKDF2-HMAC-SHA256 600,000 iterations, AES-256-GCM in 1 MiB chunks with 12-byte nonce and 16-byte tag per chunk; the last chunk is flagged; chunk index and last-flag are the AAD.
- Restore only adds; never deletes or overwrites. Duplicate = same conversation participants, direction, time within 1 second, same body, same attachment count.
- ForgeLink data merges: filters by name (case-insensitive); favorites, blocks, allowed senders, quick replies (same text), scheduled messages (same conversation, time and text) added only if missing; archive/trash/snooze/mute/drafts applied only to conversations present in the backup; settings replaced only when "Also restore my settings" is ticked.
- Conversations are identified by `ConversationKey.From(participants)`, never Android thread ids.
- Media is streamed; never hold a whole attachment or the whole backup in memory.
- Weekly job: every 7 days, requires charging and battery-not-low.
- Errors shown to the user: "That password doesn't open this backup." / "This backup is protected. Enter its password." / "This backup file is damaged or incomplete." / "This backup was made by a newer version of ForgeLink — update the app first."
- Never commit until the user asks; commit steps are staged as `git add` only.

## Review Focus

1. **Restore run twice** — the second run must add 0 messages and create no duplicate filters, favorites, quick replies or scheduled texts (tests in Task 7 and Task 5).
2. **Group MMS** — a received group picture message must restore into the same group conversation with the right sender and its photo intact (Task 7 fake-target test covers the data path; Task 11 device check).
3. **Truncated or tampered encrypted file** — must be refused as damaged with nothing restored, never partially applied (tests in Task 2 and Task 3).
4. **Cancelling or failing mid-backup** — the partial file is deleted and the last-backup status says why (Task 9 worker code + Task 11 device check).
5. **Alphanumeric senders** (e.g. "CarelonRx") — must keep a stable, non-empty conversation key so their data merges correctly (test in Task 1).

## Rulings recorded in this plan

- **Restore summary before confirming is dropped:** reading the manifest of an encrypted backup needs the whole file decrypted first. Restore instead shows a confirm sheet, then reports counts in its finished notification and the Settings status line. Spec updated in Task 11.
- **Forwarded-message markers are not backed up:** they are keyed by message ids that don't exist on another phone. Spec updated in Task 11.
- **Group participants:** each message's `Addresses` are its thread's participants (from `IThreadService`), and incoming MMS store their sender in `From`, so conversation keys for messages and ForgeLink data always agree.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/ForgeLinkSms.Core/Backup/BackupModels.cs` | Manifest, message, attachment, app-data records; `ConversationKey` |
| `src/ForgeLinkSms.Core/Backup/BackupJson.cs` | JSON options + helpers |
| `src/ForgeLinkSms.Core/Backup/BackupExceptions.cs` | Password / damaged / too-new exceptions |
| `src/ForgeLinkSms.Core/Backup/BackupCrypto.cs` | Chunked AES-GCM encrypt/decrypt streams |
| `src/ForgeLinkSms.Core/Backup/BackupWriter.cs` / `BackupReader.cs` | Zip layout, temp-file based reading |
| `src/ForgeLinkSms.Core/Backup/DuplicateIndex.cs` | "Already on the phone" rule |
| `src/ForgeLinkSms.Core/Backup/AppDataMerger.cs` | Merge plan for ForgeLink data |
| `src/ForgeLinkSms.Core/Backup/BackupRetention.cs` | File naming + which weekly files to delete |
| `src/ForgeLinkSms.Core/Backup/BackupRunner.cs` / `RestoreRunner.cs` | Orchestration against `IBackupSource` / `IRestoreTarget` |
| `src/ForgeLinkSms.Core/Services/IBackupService.cs` | What the Settings page calls |
| `src/ForgeLinkSms.Core/Data/IMuteRepository.cs` + `MuteRepository.cs` | + `GetAllAsync()` |
| `src/ForgeLinkSms/Platforms/Android/Backup/*.cs` | Source, target, snapshot, workers, SAF bridge, service |
| `src/ForgeLinkSms/Platforms/Android/MainActivity.cs` | Forward activity results to the bridge |
| `src/ForgeLinkSms/Platforms/Android/AndroidManifest.xml` | Foreground-service permission + WorkManager service type |
| `src/ForgeLinkSms/Pages/Settings/SettingsPage.razor` | Backup section |
| `tests/ForgeLinkSms.Core.Tests/Backup/*.cs` | Tests |

---

### Task 1: Backup models, JSON, conversation keys

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/BackupModels.cs`, `BackupJson.cs`, `BackupExceptions.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/BackupModelsTests.cs`

**Interfaces:**
- Produces: `BackupManifest`, `BackupAttachment`, `BackupMessage`, `BackupFilter`, `BackupScheduled`, `BackupTimed`, `BackupDraft`, `BackupSettings`, `AppData`, `ConversationKey.From(IEnumerable<string>)`, `ConversationKey.Normalize(string)`, `BackupJson.Serialize<T>`, `BackupJson.Deserialize<T>`, `BackupException`, `BackupPasswordException(bool required)`, `BackupDamagedException`, `BackupTooNewException`.

- [ ] **Step 1: Write the failing test**

`tests/ForgeLinkSms.Core.Tests/Backup/BackupModelsTests.cs`:

```csharp
using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupModelsTests
{
    [Fact]
    public void Conversation_key_ignores_order_formatting_and_country_code()
    {
        Assert.Equal(ConversationKey.From(new[] { "+1 (404) 555-0199", "770.555.0101" }), ConversationKey.From(new[] { "7705550101", "4045550199" }));
        Assert.Equal("4045550199", ConversationKey.From(new[] { "+14045550199" }));
    }

    [Fact]
    public void Alphanumeric_senders_keep_a_stable_non_empty_key()
    {
        Assert.Equal("carelonrx pharmacy", ConversationKey.From(new[] { " CarelonRx Pharmacy " }));
        Assert.NotEqual(ConversationKey.From(new[] { "AMAZON" }), ConversationKey.From(new[] { "CHASE" }));
    }

    [Fact]
    public void App_data_round_trips_through_json_including_settings()
    {
        var data = new AppData
        {
            Favorites = new[] { "4045550199" },
            Filters = new[] { new BackupFilter("Softball", "#16a34a", new[] { "4045550199", "7705550101" }) },
            QuickReplies = new[] { "On my way" },
            Scheduled = new[] { new BackupScheduled("4045550199", "4045550199", "Happy birthday!", new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero), "") },
            Muted = new[] { new BackupTimed("7705550101", null) },
            Blocked = new[] { "8005550000" },
            Drafts = new[] { new BackupDraft("4045550199", "half-written") },
            Settings = new BackupSettings(new DisplaySettings { TextScale = 115 }, new NotificationSettings { QuietHoursEnabled = true }, "Dark", "#16a34a")
        };

        var back = BackupJson.Deserialize<AppData>(BackupJson.Serialize(data));

        Assert.Equal(data.Favorites, back.Favorites);
        Assert.Equal("Softball", back.Filters[0].Name);
        Assert.Equal(data.Filters[0].Members, back.Filters[0].Members);
        Assert.Equal(data.Scheduled[0], back.Scheduled[0]);
        Assert.Null(back.Muted[0].UntilUtc);
        Assert.Equal(115, back.Settings!.Display.TextScale);
        Assert.True(back.Settings.Notifications.QuietHoursEnabled);
    }

    [Fact]
    public void Message_round_trips_with_sender_and_attachments()
    {
        var m = new BackupMessage(true, new[] { "4045550199", "7705550101" }, "4045550199", 1_790_000_000_000, 1_790_000_000_000, false, true, 0, "Look!", null,
            new[] { new BackupAttachment("media/1.jpg", "image/jpeg", "IMG_1.jpg") });

        var back = BackupJson.Deserialize<BackupMessage>(BackupJson.Serialize(m));

        Assert.Equal(m.Addresses, back.Addresses);
        Assert.Equal("4045550199", back.From);
        Assert.Equal("media/1.jpg", back.Attachments[0].Media);
    }

    [Fact]
    public void Bad_json_is_reported_as_a_damaged_backup()
    {
        Assert.Throws<BackupDamagedException>(() => BackupJson.Deserialize<AppData>("{not json"));
        Assert.Throws<BackupDamagedException>(() => BackupJson.Deserialize<AppData>("null"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter FullyQualifiedName~BackupModelsTests`
Expected: build error "The type or namespace name 'Backup' does not exist".

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Backup/BackupExceptions.cs`:

```csharp
namespace ForgeLinkSms.Core.Backup;

// Messages are shown to the user as-is.
public class BackupException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class BackupPasswordException(bool required)
    : BackupException(required ? "This backup is protected. Enter its password." : "That password doesn't open this backup.")
{
    public bool Required { get; } = required;
}

public sealed class BackupDamagedException(Exception? inner = null)
    : BackupException("This backup file is damaged or incomplete.", inner);

public sealed class BackupTooNewException()
    : BackupException("This backup was made by a newer version of ForgeLink — update the app first.");
```

`src/ForgeLinkSms.Core/Backup/BackupModels.cs`:

```csharp
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Backup;

public sealed record BackupManifest(int Format, string AppVersion, DateTimeOffset CreatedUtc, int SmsCount, int MmsCount, int MediaFiles, int Conversations);

public sealed record BackupAttachment(string Media, string ContentType, string? FileName);

// Addresses are the conversation's participants (so every message in a group shares one key);
// From is the sender of an incoming picture message, which a group needs to restore correctly.
public sealed record BackupMessage(
    bool IsMms,
    IReadOnlyList<string> Addresses,
    string? From,
    long TimestampMs,
    long DateSentMs,
    bool Outgoing,
    bool Read,
    int Status,
    string? Body,
    string? Subject,
    IReadOnlyList<BackupAttachment> Attachments);

public sealed record BackupFilter(string Name, string ColorHex, IReadOnlyList<string> Members);

public sealed record BackupScheduled(string Conversation, string Address, string Body, DateTimeOffset SendAtUtc, string GroupAddresses);

public sealed record BackupTimed(string Conversation, DateTimeOffset? UntilUtc);

public sealed record BackupDraft(string Conversation, string Text);

public sealed record BackupSettings(DisplaySettings Display, NotificationSettings Notifications, string ThemeMode, string AccentColor);

public sealed record AppData
{
    public IReadOnlyList<string> Favorites { get; init; } = [];
    public IReadOnlyList<BackupFilter> Filters { get; init; } = [];
    public IReadOnlyList<string> QuickReplies { get; init; } = [];
    public IReadOnlyList<BackupScheduled> Scheduled { get; init; } = [];
    public IReadOnlyList<string> Archived { get; init; } = [];
    public IReadOnlyList<string> Trashed { get; init; } = [];
    public IReadOnlyList<BackupTimed> Snoozed { get; init; } = [];
    public IReadOnlyList<BackupTimed> Muted { get; init; } = [];
    public IReadOnlyList<string> Blocked { get; init; } = [];
    public IReadOnlyList<string> Allowed { get; init; } = [];
    public IReadOnlyList<BackupDraft> Drafts { get; init; } = [];
    public BackupSettings? Settings { get; init; }
}

// Android thread ids differ on another phone, so a conversation is identified by who's in it.
public static class ConversationKey
{
    public static string Normalize(string address)
    {
        var digits = PhoneNumberFormatter.ToComparableDigits(address);
        return digits.Length > 0 ? digits : address.Trim().ToLowerInvariant();
    }

    public static string From(IEnumerable<string> addresses) =>
        string.Join(",", addresses.Select(Normalize).Where(a => a.Length > 0).Distinct().OrderBy(a => a, StringComparer.Ordinal));
}
```

`src/ForgeLinkSms.Core/Backup/BackupJson.cs`:

```csharp
using System.Text.Json;

namespace ForgeLinkSms.Core.Backup;

public static class BackupJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new BackupDamagedException();
        }
        catch (JsonException e)
        {
            throw new BackupDamagedException(e);
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter FullyQualifiedName~BackupModelsTests` → all pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms.Core/Backup tests/ForgeLinkSms.Core.Tests/Backup
```

---

### Task 2: Encryption streams

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/BackupCrypto.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/BackupCryptoTests.cs`

**Interfaces:**
- Produces: `BackupCrypto.ChunkSize` (1,048,576), `BackupCrypto.Iterations` (600,000), `BackupCrypto.IsEncrypted(ReadOnlySpan<byte>)`, `BackupCrypto.CreateEncryptingStream(Stream output, string password, int iterations = Iterations)`, `BackupCrypto.CreateDecryptingStream(Stream input, string password)`.

- [ ] **Step 1: Write the failing test**

`tests/ForgeLinkSms.Core.Tests/Backup/BackupCryptoTests.cs`:

```csharp
using System.Security.Cryptography;
using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupCryptoTests
{
    private const int FastIterations = 1_000;

    private static byte[] Encrypt(byte[] plain, string password)
    {
        var output = new MemoryStream();
        using (var crypto = BackupCrypto.CreateEncryptingStream(output, password, FastIterations))
        {
            crypto.Write(plain, 0, plain.Length);
        }
        return output.ToArray();
    }

    private static byte[] Decrypt(byte[] cipher, string password)
    {
        using var crypto = BackupCrypto.CreateDecryptingStream(new MemoryStream(cipher), password);
        var output = new MemoryStream();
        crypto.CopyTo(output);
        return output.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(BackupCrypto.ChunkSize)]
    [InlineData(BackupCrypto.ChunkSize * 2 + 12345)]
    public void Round_trips_any_length(int length)
    {
        var plain = RandomNumberGenerator.GetBytes(length);

        var cipher = Encrypt(plain, "correct horse");

        Assert.True(BackupCrypto.IsEncrypted(cipher));
        Assert.Equal(plain, Decrypt(cipher, "correct horse"));
    }

    [Fact]
    public void Wrong_password_is_reported_as_a_password_problem()
    {
        var cipher = Encrypt(RandomNumberGenerator.GetBytes(5000), "right");

        var e = Assert.Throws<BackupPasswordException>(() => Decrypt(cipher, "wrong"));
        Assert.False(e.Required);
    }

    [Fact]
    public void Truncated_file_is_damaged()
    {
        var cipher = Encrypt(RandomNumberGenerator.GetBytes(BackupCrypto.ChunkSize + 500), "pw");

        Assert.Throws<BackupDamagedException>(() => Decrypt(cipher[..^100], "pw"));
    }

    [Fact]
    public void Dropping_the_whole_last_chunk_is_detected()
    {
        var plain = RandomNumberGenerator.GetBytes(BackupCrypto.ChunkSize + 10);
        var cipher = Encrypt(plain, "pw");
        // Header (25) + first chunk (5 + 12 + 16 + ChunkSize) ends exactly before the last chunk.
        var firstChunkEnd = 25 + 5 + 12 + 16 + BackupCrypto.ChunkSize;

        Assert.Throws<BackupDamagedException>(() => Decrypt(cipher[..firstChunkEnd], "pw"));
    }

    [Fact]
    public void Tampering_after_the_first_chunk_is_damaged_not_a_password_problem()
    {
        var cipher = Encrypt(RandomNumberGenerator.GetBytes(BackupCrypto.ChunkSize + 500), "pw");
        cipher[^10] ^= 0xFF;

        Assert.Throws<BackupDamagedException>(() => Decrypt(cipher, "pw"));
    }

    [Fact]
    public void Plain_data_is_not_mistaken_for_encrypted()
    {
        Assert.False(BackupCrypto.IsEncrypted("PK\u0003\u0004"u8));
        Assert.False(BackupCrypto.IsEncrypted("FL"u8));
    }

    [Fact]
    public void Garbage_header_is_damaged()
    {
        Assert.Throws<BackupDamagedException>(() => Decrypt("FLBK"u8.ToArray(), "pw"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter FullyQualifiedName~BackupCryptoTests`
Expected: build error "'BackupCrypto' does not exist".

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Backup/BackupCrypto.cs`:

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ForgeLinkSms.Core.Backup;

// File: "FLBK" | version (1) | salt (16) | iterations (int32 BE) | chunks...
// Chunk: last flag (1) | length (int32 BE) | nonce (12) | tag (16) | ciphertext.
// The chunk index and last flag are authenticated, so reordering, dropping or truncating chunks fails.
public static class BackupCrypto
{
    public const int ChunkSize = 1024 * 1024;
    public const int Iterations = 600_000;
    internal const byte Version = 1;
    internal const int HeaderSize = 4 + 1 + 16 + 4;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;
    internal static ReadOnlySpan<byte> Magic => "FLBK"u8;

    public static bool IsEncrypted(ReadOnlySpan<byte> header) => header.Length >= 4 && header[..4].SequenceEqual(Magic);

    public static Stream CreateEncryptingStream(Stream output, string password, int iterations = Iterations) =>
        new EncryptingStream(output, password, iterations);

    public static Stream CreateDecryptingStream(Stream input, string password) => new DecryptingStream(input, password);

    internal static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    internal static byte[] Aad(long index, bool last)
    {
        var aad = new byte[9];
        BinaryPrimitives.WriteInt64BigEndian(aad, index);
        aad[8] = last ? (byte)1 : (byte)0;
        return aad;
    }

    private sealed class EncryptingStream : Stream
    {
        private readonly Stream _output;
        private readonly AesGcm _aes;
        private readonly byte[] _buffer = new byte[ChunkSize];
        private int _count;
        private long _index;
        private bool _finished;

        public EncryptingStream(Stream output, string password, int iterations)
        {
            _output = output;
            var salt = RandomNumberGenerator.GetBytes(16);
            var header = new byte[HeaderSize];
            Magic.CopyTo(header);
            header[4] = Version;
            salt.CopyTo(header, 5);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(21), iterations);
            _output.Write(header);
            _aes = new AesGcm(DeriveKey(password, salt, iterations), TagSize);
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            while (data.Length > 0)
            {
                var n = Math.Min(data.Length, _buffer.Length - _count);
                data[..n].CopyTo(_buffer.AsSpan(_count));
                _count += n;
                data = data[n..];
                if (_count == _buffer.Length)
                {
                    WriteChunk(last: false);
                }
            }
        }

        private void WriteChunk(bool last)
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var cipher = new byte[_count];
            _aes.Encrypt(nonce, _buffer.AsSpan(0, _count), cipher, tag, Aad(_index, last));
            Span<byte> head = stackalloc byte[5];
            head[0] = last ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32BigEndian(head[1..], _count);
            _output.Write(head);
            _output.Write(nonce);
            _output.Write(tag);
            _output.Write(cipher);
            _index++;
            _count = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_finished)
            {
                _finished = true;
                WriteChunk(last: true);
                _output.Flush();
                _aes.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class DecryptingStream : Stream
    {
        private readonly Stream _input;
        private readonly AesGcm _aes;
        private byte[] _plain = Array.Empty<byte>();
        private int _position;
        private long _index;
        private bool _sawLast;

        public DecryptingStream(Stream input, string password)
        {
            _input = input;
            var header = new byte[HeaderSize];
            ReadExactly(header);
            if (!IsEncrypted(header))
            {
                throw new BackupDamagedException();
            }
            if (header[4] != Version)
            {
                throw new BackupTooNewException();
            }
            var iterations = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(21));
            if (iterations is < 1 or > 10_000_000)
            {
                throw new BackupDamagedException();
            }
            _aes = new AesGcm(DeriveKey(password, header[5..21], iterations), TagSize);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> destination)
        {
            if (_position == _plain.Length)
            {
                if (_sawLast)
                {
                    return 0;
                }
                LoadNextChunk();
            }
            var n = Math.Min(destination.Length, _plain.Length - _position);
            _plain.AsSpan(_position, n).CopyTo(destination);
            _position += n;
            return n == 0 && !_sawLast ? Read(destination) : n;
        }

        private void LoadNextChunk()
        {
            var head = new byte[5];
            ReadExactly(head);
            var last = head[0] == 1;
            var length = BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(1));
            if (head[0] > 1 || length < 0 || length > ChunkSize)
            {
                throw new BackupDamagedException();
            }
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var cipher = new byte[length];
            ReadExactly(nonce);
            ReadExactly(tag);
            ReadExactly(cipher);
            var plain = new byte[length];
            try
            {
                _aes.Decrypt(nonce, cipher, tag, plain, Aad(_index, last));
            }
            catch (AuthenticationTagMismatchException e)
            {
                // Only the first chunk can tell a wrong password apart from later damage.
                if (_index == 0)
                {
                    throw new BackupPasswordException(required: false);
                }
                throw new BackupDamagedException(e);
            }
            _plain = plain;
            _position = 0;
            _index++;
            _sawLast = last;
        }

        private void ReadExactly(Span<byte> buffer)
        {
            try
            {
                _input.ReadExactly(buffer);
            }
            catch (EndOfStreamException e)
            {
                throw new BackupDamagedException(e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _aes?.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter FullyQualifiedName~BackupCryptoTests` → all pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms.Core/Backup/BackupCrypto.cs tests/ForgeLinkSms.Core.Tests/Backup/BackupCryptoTests.cs
```

---

### Task 3: Backup file writer and reader

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/BackupWriter.cs`, `src/ForgeLinkSms.Core/Backup/BackupReader.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/BackupArchiveTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces:
  - `new BackupWriter(Stream destination, string? password, int iterations = BackupCrypto.Iterations)`; `string AddMedia(Stream content, string extension)`; `int MediaCount`; `void Finish(BackupManifest, IEnumerable<BackupMessage>, AppData)`; `Dispose()`; `const int Format = 1`.
  - `BackupReader.Open(Stream source, string? password, string workDirectory)`; `BackupManifest Manifest`; `AppData AppData`; `IEnumerable<BackupMessage> ReadMessages()`; `Stream OpenMedia(string name)`; `Dispose()` (deletes its temp file).

- [ ] **Step 1: Write the failing test**

`tests/ForgeLinkSms.Core.Tests/Backup/BackupArchiveTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter FullyQualifiedName~BackupArchiveTests` → build error "'BackupWriter' does not exist".

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Backup/BackupWriter.cs`:

```csharp
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
```

`src/ForgeLinkSms.Core/Backup/BackupReader.cs`:

```csharp
using System.IO.Compression;

namespace ForgeLinkSms.Core.Backup;

// Zip reading needs to seek, and picked files arrive as forward-only streams (and encrypted ones must
// be decrypted first), so the archive is copied into a temp file that Dispose removes.
public sealed class BackupReader : IDisposable
{
    private readonly string _tempPath;
    private readonly FileStream _file;
    private readonly ZipArchive _zip;

    public BackupManifest Manifest { get; }
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

    public IEnumerable<BackupMessage> ReadMessages()
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
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests --filter FullyQualifiedName~Backup` → all pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms.Core/Backup/BackupWriter.cs src/ForgeLinkSms.Core/Backup/BackupReader.cs tests/ForgeLinkSms.Core.Tests/Backup/BackupArchiveTests.cs
```

---

### Task 4: Duplicate index

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/DuplicateIndex.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/DuplicateIndexTests.cs`

**Interfaces:**
- Produces: `record ExistingMessage(string Conversation, long TimestampMs, bool Outgoing, string? Body, int AttachmentCount)`; `DuplicateIndex.Add(ExistingMessage)`; `bool DuplicateIndex.Contains(BackupMessage)`.

- [ ] **Step 1: Write the failing test**

`tests/ForgeLinkSms.Core.Tests/Backup/DuplicateIndexTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/ForgeLinkSms.Core.Tests --filter FullyQualifiedName~DuplicateIndexTests` → build error "'DuplicateIndex' does not exist".

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Backup/DuplicateIndex.cs`:

```csharp
namespace ForgeLinkSms.Core.Backup;

public sealed record ExistingMessage(string Conversation, long TimestampMs, bool Outgoing, string? Body, int AttachmentCount);

// Providers round message times differently (MMS stores whole seconds), so a match allows one second either way.
public sealed class DuplicateIndex
{
    private readonly HashSet<string> _keys = new();

    public void Add(ExistingMessage message) =>
        _keys.Add(Key(message.Conversation, Seconds(message.TimestampMs), message.Outgoing, message.Body, message.AttachmentCount));

    public bool Contains(BackupMessage message)
    {
        var conversation = ConversationKey.From(message.Addresses);
        var seconds = Seconds(message.TimestampMs);
        for (var delta = -1; delta <= 1; delta++)
        {
            if (_keys.Contains(Key(conversation, seconds + delta, message.Outgoing, message.Body, message.Attachments.Count)))
            {
                return true;
            }
        }
        return false;
    }

    private static long Seconds(long ms) => (long)Math.Floor(ms / 1000.0);

    private static string Key(string conversation, long seconds, bool outgoing, string? body, int attachments) =>
        $"{conversation}|{seconds}|{(outgoing ? 1 : 0)}|{attachments}|{(body ?? string.Empty).Trim()}";
}
```

- [ ] **Step 4: Run tests** → pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms.Core/Backup/DuplicateIndex.cs tests/ForgeLinkSms.Core.Tests/Backup/DuplicateIndexTests.cs
```

---

### Task 5: App-data merge plan

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/AppDataMerger.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/AppDataMergerTests.cs`

**Interfaces:**
- Produces: `record FilterToCreate(string Name, string ColorHex)`, `record FilterMember(string FilterName, string Conversation)`, `record MergePlan { ... }` (properties below), `AppDataMerger.Plan(AppData current, AppData backup, IReadOnlySet<string> restoredConversations, bool includeSettings, DateTimeOffset now)`.

- [ ] **Step 1: Write the failing test**

`tests/ForgeLinkSms.Core.Tests/Backup/AppDataMergerTests.cs`:

```csharp
using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Backup;

public class AppDataMergerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlySet<string> Restored = new HashSet<string> { "a", "b" };

    private static MergePlan Plan(AppData current, AppData backup, bool settings = false) =>
        AppDataMerger.Plan(current, backup, Restored, settings, Now);

    [Fact]
    public void Adds_only_missing_favorites_blocks_allowed_and_quick_replies()
    {
        var plan = Plan(
            new AppData { Favorites = new[] { "a" }, Blocked = new[] { "900" }, Allowed = new[] { "700" }, QuickReplies = new[] { "On my way" } },
            new AppData { Favorites = new[] { "a", "b" }, Blocked = new[] { "900", "901" }, Allowed = new[] { "700", "701" }, QuickReplies = new[] { "On my way", "Call you soon" } });

        Assert.Equal(new[] { "b" }, plan.FavoritesToAdd);
        Assert.Equal(new[] { "901" }, plan.BlockedToAdd);
        Assert.Equal(new[] { "701" }, plan.AllowedToAdd);
        Assert.Equal(new[] { "Call you soon" }, plan.QuickRepliesToAdd);
    }

    [Fact]
    public void Filters_join_by_name_ignoring_case_and_add_only_new_members()
    {
        var plan = Plan(
            new AppData { Filters = new[] { new BackupFilter("Softball", "#16a34a", new[] { "a" }) } },
            new AppData { Filters = new[] { new BackupFilter("softball", "#e11d48", new[] { "a", "b" }), new BackupFilter("Work", "#0ea5e9", new[] { "b" }) } });

        Assert.Equal(new[] { new FilterToCreate("Work", "#0ea5e9") }, plan.FiltersToCreate);
        Assert.Equal(new[] { new FilterMember("Softball", "b"), new FilterMember("Work", "b") }, plan.MembersToAdd);
    }

    [Fact]
    public void Scheduled_texts_are_added_once_and_only_if_still_in_the_future()
    {
        var future = new BackupScheduled("a", "4045550199", "Happy birthday", Now.AddDays(3), "");
        var past = future with { SendAtUtc = Now.AddDays(-1), Body = "Too late" };

        Assert.Equal(new[] { future }, Plan(new AppData(), new AppData { Scheduled = new[] { future, past } }).ScheduledToAdd);
        Assert.Empty(Plan(new AppData { Scheduled = new[] { future } }, new AppData { Scheduled = new[] { future } }).ScheduledToAdd);
    }

    [Fact]
    public void Conversation_states_apply_only_to_restored_conversations()
    {
        var plan = Plan(new AppData { Archived = new[] { "b" } }, new AppData
        {
            Archived = new[] { "a", "b", "z" },
            Trashed = new[] { "z" },
            Snoozed = new[] { new BackupTimed("a", Now.AddHours(2)), new BackupTimed("b", Now.AddHours(-2)) },
            Muted = new[] { new BackupTimed("a", null), new BackupTimed("z", null) },
            Drafts = new[] { new BackupDraft("a", "hello"), new BackupDraft("z", "nope") }
        });

        Assert.Equal(new[] { "a" }, plan.ArchiveToApply);
        Assert.Empty(plan.TrashToApply);
        Assert.Equal(new[] { new BackupTimed("a", Now.AddHours(2)) }, plan.SnoozesToApply);
        Assert.Equal(new[] { new BackupTimed("a", null) }, plan.MutesToApply);
        Assert.Equal(new[] { new BackupDraft("a", "hello") }, plan.DraftsToAdd);
    }

    [Fact]
    public void Existing_drafts_are_never_overwritten()
    {
        var plan = Plan(new AppData { Drafts = new[] { new BackupDraft("a", "mine") } }, new AppData { Drafts = new[] { new BackupDraft("a", "old") } });

        Assert.Empty(plan.DraftsToAdd);
    }

    [Fact]
    public void Settings_are_restored_only_when_asked()
    {
        var backup = new AppData { Settings = new BackupSettings(new DisplaySettings { TextScale = 130 }, new NotificationSettings(), "Dark", "#16a34a") };

        Assert.Null(Plan(new AppData(), backup).Settings);
        Assert.Equal(130, Plan(new AppData(), backup, settings: true).Settings!.Display.TextScale);
    }

    [Fact]
    public void Merging_the_same_backup_twice_changes_nothing_the_second_time()
    {
        var backup = new AppData { Favorites = new[] { "a" }, Filters = new[] { new BackupFilter("Work", "#0ea5e9", new[] { "a" }) }, QuickReplies = new[] { "Ok" } };

        var plan = Plan(backup, backup);

        Assert.Empty(plan.FavoritesToAdd);
        Assert.Empty(plan.FiltersToCreate);
        Assert.Empty(plan.MembersToAdd);
        Assert.Empty(plan.QuickRepliesToAdd);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — build error "'AppDataMerger' does not exist".

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Backup/AppDataMerger.cs`:

```csharp
namespace ForgeLinkSms.Core.Backup;

public sealed record FilterToCreate(string Name, string ColorHex);

public sealed record FilterMember(string FilterName, string Conversation);

public sealed record MergePlan
{
    public IReadOnlyList<string> FavoritesToAdd { get; init; } = [];
    public IReadOnlyList<FilterToCreate> FiltersToCreate { get; init; } = [];
    public IReadOnlyList<FilterMember> MembersToAdd { get; init; } = [];
    public IReadOnlyList<string> QuickRepliesToAdd { get; init; } = [];
    public IReadOnlyList<BackupScheduled> ScheduledToAdd { get; init; } = [];
    public IReadOnlyList<string> ArchiveToApply { get; init; } = [];
    public IReadOnlyList<string> TrashToApply { get; init; } = [];
    public IReadOnlyList<BackupTimed> SnoozesToApply { get; init; } = [];
    public IReadOnlyList<BackupTimed> MutesToApply { get; init; } = [];
    public IReadOnlyList<string> BlockedToAdd { get; init; } = [];
    public IReadOnlyList<string> AllowedToAdd { get; init; } = [];
    public IReadOnlyList<BackupDraft> DraftsToAdd { get; init; } = [];
    public BackupSettings? Settings { get; init; }
}

// Restore only adds: nothing already on the phone is removed or overwritten.
public static class AppDataMerger
{
    public static MergePlan Plan(AppData current, AppData backup, IReadOnlySet<string> restoredConversations, bool includeSettings, DateTimeOffset now)
    {
        var existingFilters = current.Filters.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var filtersToCreate = new List<FilterToCreate>();
        var members = new List<FilterMember>();
        foreach (var filter in backup.Filters)
        {
            existingFilters.TryGetValue(filter.Name, out var existing);
            var name = existing?.Name ?? filter.Name;
            if (existing is null && filtersToCreate.All(f => !f.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase)))
            {
                filtersToCreate.Add(new FilterToCreate(filter.Name, filter.ColorHex));
            }
            var have = existing?.Members.ToHashSet() ?? new HashSet<string>();
            members.AddRange(filter.Members.Where(m => !have.Contains(m)).Distinct().Select(m => new FilterMember(name, m)));
        }

        bool Restored(string conversation) => restoredConversations.Contains(conversation);
        var snoozed = current.Snoozed.Select(s => s.Conversation).ToHashSet();
        var muted = current.Muted.Select(m => m.Conversation).ToHashSet();
        var drafts = current.Drafts.Select(d => d.Conversation).ToHashSet();

        return new MergePlan
        {
            FavoritesToAdd = Missing(backup.Favorites, current.Favorites),
            FiltersToCreate = filtersToCreate,
            MembersToAdd = members,
            QuickRepliesToAdd = Missing(backup.QuickReplies.Select(q => q.Trim()), current.QuickReplies.Select(q => q.Trim())),
            ScheduledToAdd = backup.Scheduled
                .Where(s => s.SendAtUtc > now && !current.Scheduled.Any(c => c.Conversation == s.Conversation && c.SendAtUtc == s.SendAtUtc && c.Body == s.Body))
                .Distinct()
                .ToList(),
            ArchiveToApply = Missing(backup.Archived.Where(Restored), current.Archived),
            TrashToApply = Missing(backup.Trashed.Where(Restored), current.Trashed),
            SnoozesToApply = backup.Snoozed.Where(s => Restored(s.Conversation) && s.UntilUtc > now && !snoozed.Contains(s.Conversation)).ToList(),
            MutesToApply = backup.Muted.Where(m => Restored(m.Conversation) && (m.UntilUtc is null || m.UntilUtc > now) && !muted.Contains(m.Conversation)).ToList(),
            BlockedToAdd = Missing(backup.Blocked, current.Blocked),
            AllowedToAdd = Missing(backup.Allowed, current.Allowed),
            DraftsToAdd = backup.Drafts.Where(d => Restored(d.Conversation) && !drafts.Contains(d.Conversation)).ToList(),
            Settings = includeSettings ? backup.Settings : null
        };
    }

    private static List<string> Missing(IEnumerable<string> wanted, IEnumerable<string> have)
    {
        var existing = have.ToHashSet(StringComparer.Ordinal);
        return wanted.Where(w => w.Length > 0 && !existing.Contains(w)).Distinct().ToList();
    }
}
```

- [ ] **Step 4: Run tests** → pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms.Core/Backup/AppDataMerger.cs tests/ForgeLinkSms.Core.Tests/Backup/AppDataMergerTests.cs
```

---

### Task 6: File naming and retention

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/BackupRetention.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/BackupRetentionTests.cs`

**Interfaces:**
- Produces: `BackupRetention.FileNameFor(DateTime local)`, `BackupRetention.FilesToDelete(IEnumerable<string> names, int keep = 4)`.

- [ ] **Step 1: Write the failing test**

`tests/ForgeLinkSms.Core.Tests/Backup/BackupRetentionTests.cs`:

```csharp
using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupRetentionTests
{
    [Fact]
    public void Names_backups_by_local_date_and_time()
    {
        Assert.Equal("ForgeLink-backup-2026-11-02-0905.flbackup", BackupRetention.FileNameFor(new DateTime(2026, 11, 2, 9, 5, 0)));
    }

    [Fact]
    public void Keeps_the_newest_four_and_never_touches_other_files()
    {
        var names = new[]
        {
            "ForgeLink-backup-2026-10-01-0300.flbackup",
            "ForgeLink-backup-2026-10-08-0300.flbackup",
            "ForgeLink-backup-2026-10-15-0300.flbackup",
            "ForgeLink-backup-2026-10-22-0300.flbackup",
            "ForgeLink-backup-2026-10-29-0300.flbackup",
            "ForgeLink-backup-2026-11-05-0300.flbackup",
            "tax-return.pdf",
            "ForgeLink-backup-copy.flbackup",
            "ForgeLink-backup-2026-01-01-0000.flbackup.partial"
        };

        Assert.Equal(new[] { "ForgeLink-backup-2026-10-08-0300.flbackup", "ForgeLink-backup-2026-10-01-0300.flbackup" },
            BackupRetention.FilesToDelete(names));
    }

    [Fact]
    public void Four_or_fewer_backups_delete_nothing()
    {
        Assert.Empty(BackupRetention.FilesToDelete(new[] { "ForgeLink-backup-2026-10-01-0300.flbackup" }));
    }
}
```

- [ ] **Step 2: Run to verify it fails** — build error "'BackupRetention' does not exist".

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Backup/BackupRetention.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Backup;

public static partial class BackupRetention
{
    public static string FileNameFor(DateTime local) =>
        $"ForgeLink-backup-{local.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.flbackup";

    // Only files this app named are ever candidates; the date in the name sorts newest-first.
    public static IReadOnlyList<string> FilesToDelete(IEnumerable<string> names, int keep = 4) =>
        names.Where(n => BackupName().IsMatch(n))
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

    [GeneratedRegex(@"^ForgeLink-backup-\d{4}-\d{2}-\d{2}-\d{4}\.flbackup$")]
    private static partial Regex BackupName();
}
```

- [ ] **Step 4: Run tests** → pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms.Core/Backup/BackupRetention.cs tests/ForgeLinkSms.Core.Tests/Backup/BackupRetentionTests.cs
```

---

### Task 7: Backup and restore runners

**Files:**
- Create: `src/ForgeLinkSms.Core/Backup/BackupRunner.cs`, `src/ForgeLinkSms.Core/Backup/RestoreRunner.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Backup/RunnerTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces:
  - `record SourceAttachment(string ContentType, string? FileName, Func<Stream> Open)`, `record SourceMessage(BackupMessage Message, IReadOnlyList<SourceAttachment> Attachments)`
  - `interface IBackupSource { Task<AppData> ReadAppDataAsync(); int CountMessages(); IEnumerable<SourceMessage> ReadMessages(); }`
  - `record BackupProgress(int Done, int Total, int MediaFiles)`
  - `BackupRunner.RunAsync(IBackupSource, Stream destination, string? password, string appVersion, DateTimeOffset now, IProgress<BackupProgress>?, CancellationToken, int iterations = BackupCrypto.Iterations) → Task<BackupManifest>`
  - `interface IRestoreTarget { Task<IReadOnlyList<ExistingMessage>> ReadExistingMessagesAsync(); Task<AppData> ReadAppDataAsync(); Task InsertSmsAsync(BackupMessage); Task InsertMmsAsync(BackupMessage, Func<string, Stream> openMedia); Task ApplyAsync(MergePlan); }`
  - `record RestoreResult(int Added, int Skipped)`
  - `RestoreRunner.RunAsync(BackupReader, IRestoreTarget, bool includeSettings, DateTimeOffset now, IProgress<BackupProgress>?, CancellationToken) → Task<RestoreResult>`

- [ ] **Step 1: Write the failing test**

`tests/ForgeLinkSms.Core.Tests/Backup/RunnerTests.cs`:

```csharp
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

        public Task<IReadOnlyList<ExistingMessage>> ReadExistingMessagesAsync() => Task.FromResult<IReadOnlyList<ExistingMessage>>(Existing.ToList());
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

        var result = await RestoreRunner.RunAsync(reader, target, includeSettings: false, Now, null, CancellationToken.None);

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
            await RestoreRunner.RunAsync(first, target, false, Now, null, CancellationToken.None);
        }
        using var second = BackupReader.Open(new MemoryStream(bytes), null, _work);

        var result = await RestoreRunner.RunAsync(second, target, false, Now, null, CancellationToken.None);

        Assert.Equal(new RestoreResult(0, 3), result);
        Assert.Empty(target.Plans[1].FavoritesToAdd);
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
```

- [ ] **Step 2: Run to verify it fails** — build error "'IBackupSource' does not exist".

- [ ] **Step 3: Implement**

`src/ForgeLinkSms.Core/Backup/BackupRunner.cs`:

```csharp
namespace ForgeLinkSms.Core.Backup;

public sealed record SourceAttachment(string ContentType, string? FileName, Func<Stream> Open);

public sealed record SourceMessage(BackupMessage Message, IReadOnlyList<SourceAttachment> Attachments);

public sealed record BackupProgress(int Done, int Total, int MediaFiles);

public interface IBackupSource
{
    Task<AppData> ReadAppDataAsync();
    int CountMessages();
    IEnumerable<SourceMessage> ReadMessages();
}

public static class BackupRunner
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg", ["image/jpg"] = "jpg", ["image/png"] = "png", ["image/gif"] = "gif", ["image/webp"] = "webp",
        ["image/heic"] = "heic", ["video/mp4"] = "mp4", ["video/3gpp"] = "3gp", ["audio/amr"] = "amr", ["audio/mp4"] = "m4a",
        ["audio/aac"] = "aac", ["audio/mpeg"] = "mp3", ["text/x-vcard"] = "vcf", ["text/vcard"] = "vcf"
    };

    public static async Task<BackupManifest> RunAsync(
        IBackupSource source, Stream destination, string? password, string appVersion, DateTimeOffset now,
        IProgress<BackupProgress>? progress, CancellationToken cancellationToken, int iterations = BackupCrypto.Iterations)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var appData = await source.ReadAppDataAsync();
        var total = source.CountMessages();
        var messages = new List<BackupMessage>();
        var conversations = new HashSet<string>();
        int sms = 0, mms = 0, done = 0;

        using var writer = new BackupWriter(destination, password, iterations);
        foreach (var item in source.ReadMessages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attachments = new List<BackupAttachment>();
            foreach (var attachment in item.Attachments)
            {
                using var content = attachment.Open();
                attachments.Add(new BackupAttachment(writer.AddMedia(content, ExtensionFor(attachment)), attachment.ContentType, attachment.FileName));
            }
            var message = item.Message with { Attachments = attachments };
            messages.Add(message);
            conversations.Add(ConversationKey.From(message.Addresses));
            if (message.IsMms) mms++; else sms++;
            done++;
            if (done % 100 == 0 || done == total)
            {
                progress?.Report(new BackupProgress(done, total, writer.MediaCount));
            }
        }

        var manifest = new BackupManifest(BackupWriter.Format, appVersion, now, sms, mms, writer.MediaCount, conversations.Count);
        writer.Finish(manifest, messages, appData);
        return manifest;
    }

    private static string ExtensionFor(SourceAttachment attachment)
    {
        var fromName = Path.GetExtension(attachment.FileName ?? string.Empty).TrimStart('.');
        if (fromName.Length is > 0 and <= 5)
        {
            return fromName;
        }
        return Extensions.TryGetValue(attachment.ContentType, out var ext) ? ext : "bin";
    }
}
```

`src/ForgeLinkSms.Core/Backup/RestoreRunner.cs`:

```csharp
namespace ForgeLinkSms.Core.Backup;

public interface IRestoreTarget
{
    Task<IReadOnlyList<ExistingMessage>> ReadExistingMessagesAsync();
    Task<AppData> ReadAppDataAsync();
    Task InsertSmsAsync(BackupMessage message);
    Task InsertMmsAsync(BackupMessage message, Func<string, Stream> openMedia);
    Task ApplyAsync(MergePlan plan);
}

public sealed record RestoreResult(int Added, int Skipped);

public static class RestoreRunner
{
    public static async Task<RestoreResult> RunAsync(
        BackupReader reader, IRestoreTarget target, bool includeSettings, DateTimeOffset now,
        IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        var index = new DuplicateIndex();
        foreach (var existing in await target.ReadExistingMessagesAsync())
        {
            index.Add(existing);
        }

        var total = reader.Manifest.SmsCount + reader.Manifest.MmsCount;
        var restored = new HashSet<string>();
        int added = 0, skipped = 0, done = 0;
        foreach (var message in reader.ReadMessages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var conversation = ConversationKey.From(message.Addresses);
            restored.Add(conversation);
            if (index.Contains(message))
            {
                skipped++;
            }
            else
            {
                if (message.IsMms)
                {
                    await target.InsertMmsAsync(message, reader.OpenMedia);
                }
                else
                {
                    await target.InsertSmsAsync(message);
                }
                // Also guards against the same message appearing twice inside one backup.
                index.Add(new ExistingMessage(conversation, message.TimestampMs, message.Outgoing, message.Body, message.Attachments.Count));
                added++;
            }
            done++;
            if (done % 100 == 0 || done == total)
            {
                progress?.Report(new BackupProgress(done, total, 0));
            }
        }

        var plan = AppDataMerger.Plan(await target.ReadAppDataAsync(), reader.AppData, restored, includeSettings, now);
        await target.ApplyAsync(plan);
        return new RestoreResult(added, skipped);
    }
}
```

- [ ] **Step 4: Run all Core tests**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests` → all pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms.Core/Backup/BackupRunner.cs src/ForgeLinkSms.Core/Backup/RestoreRunner.cs tests/ForgeLinkSms.Core.Tests/Backup/RunnerTests.cs
```

---

### Task 8: Mute list access and the backup service interface

**Files:**
- Modify: `src/ForgeLinkSms.Core/Data/IMuteRepository.cs`, `src/ForgeLinkSms.Core/Data/MuteRepository.cs`
- Create: `src/ForgeLinkSms.Core/Services/IBackupService.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Data/MuteRepositoryTests.cs`

**Interfaces:**
- Produces: `IMuteRepository.GetAllAsync() → Task<IReadOnlyList<MutedThread>>`; `IBackupService` (below); `record BackupStatus(...)`; `record RestoreFile(...)`.

- [ ] **Step 1: Write the failing test** — append to `MuteRepositoryTests`:

```csharp
    [Fact]
    public async Task GetAll_returns_every_mute_with_its_end_time()
    {
        await _repository.MuteAsync(4, _now.AddHours(1));
        await _repository.MuteAsync(5, null);

        var all = (await _repository.GetAllAsync()).OrderBy(m => m.ThreadId).ToList();

        Assert.Equal(new long[] { 4, 5 }, all.Select(m => m.ThreadId));
        Assert.NotNull(all[0].UntilUtc);
        Assert.Null(all[1].UntilUtc);
    }
```

- [ ] **Step 2: Run to verify it fails** — build error "'MuteRepository' does not contain a definition for 'GetAllAsync'".

- [ ] **Step 3: Implement**

`IMuteRepository.cs` — add:

```csharp
    Task<IReadOnlyList<MutedThread>> GetAllAsync();
```

(with `using ForgeLinkSms.Core.Models;` if not present). `MuteRepository.cs` — add:

```csharp
    public async Task<IReadOnlyList<MutedThread>> GetAllAsync() =>
        await _db.Table<MutedThread>().ToListAsync().ConfigureAwait(false);
```

Update any test doubles implementing `IMuteRepository` (search `class .* : IMuteRepository` under `tests/`) to add the method.

`src/ForgeLinkSms.Core/Services/IBackupService.cs`:

```csharp
namespace ForgeLinkSms.Core.Services;

public sealed record BackupStatus(DateTimeOffset? LastRunUtc, bool? LastSucceeded, string? LastMessage, bool WeeklyEnabled, string? WeeklyFolderName, bool HasPassword, bool IsRunning);

public sealed record RestoreFile(string Uri, string FileName, bool IsEncrypted);

public interface IBackupService
{
    BackupStatus GetStatus();

    // False when the user closes the system "Save as" / folder picker without choosing.
    Task<bool> BackUpNowAsync();
    Task<bool> EnableWeeklyAsync();
    void DisableWeekly();

    Task SetPasswordAsync(string? password);

    Task<RestoreFile?> PickRestoreFileAsync();
    Task StartRestoreAsync(RestoreFile file, string? password, bool includeSettings);
}
```

- [ ] **Step 4: Run all Core tests** → pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms.Core/Data/IMuteRepository.cs src/ForgeLinkSms.Core/Data/MuteRepository.cs src/ForgeLinkSms.Core/Services/IBackupService.cs tests/ForgeLinkSms.Core.Tests
```

---

### Task 9: Android source, target and snapshot

**Files:**
- Create: `src/ForgeLinkSms/Platforms/Android/Backup/AppDataSnapshot.cs`, `AndroidBackupSource.cs`, `AndroidRestoreTarget.cs`

**Interfaces:**
- Consumes: Tasks 1, 5, 7, 8; `IThreadService`, repositories, `IContactBlockService`, `ISnoozeService`, `IMessageSchedulerService`, `IDisplayStyleService`, `INotificationSettingsStore`, `IThemeService`.
- Produces: `AppDataSnapshot.ReadAsync(IServiceProvider) → Task<(AppData Data, IReadOnlyDictionary<long, IReadOnlyList<string>> Participants, IReadOnlyDictionary<string, long> ThreadOf)>`; `new AndroidBackupSource(Context, IServiceProvider)`; `new AndroidRestoreTarget(Context, IServiceProvider)`.

There is no local unit test for this task (it talks to Android content providers); it is verified on the phone in Task 11. Build must succeed.

- [ ] **Step 1: Implement the snapshot**

`src/ForgeLinkSms/Platforms/Android/Backup/AppDataSnapshot.cs`:

```csharp
using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeLinkSms.Platforms.Android.Backup;

// ForgeLink data is stored by Android thread id; backups store it by conversation (participants).
internal static class AppDataSnapshot
{
    public static async Task<(AppData Data, IReadOnlyDictionary<long, IReadOnlyList<string>> Participants, IReadOnlyDictionary<string, long> ThreadOf)> ReadAsync(IServiceProvider services)
    {
        var threads = await services.GetRequiredService<IThreadService>().GetThreadsAsync();
        var participants = threads.ToDictionary(t => t.Id, t => (IReadOnlyList<string>)(t.Participants.Count > 0 ? t.Participants : new[] { t.Address }));
        var keyOf = participants.ToDictionary(p => p.Key, p => ConversationKey.From(p.Value));
        var threadOf = new Dictionary<string, long>();
        foreach (var (id, key) in keyOf)
        {
            threadOf.TryAdd(key, id);
        }
        string? Key(long threadId) => keyOf.TryGetValue(threadId, out var k) ? k : null;
        List<string> Keys(IEnumerable<long> ids) => ids.Select(Key).OfType<string>().Distinct().ToList();

        var filterRepo = services.GetRequiredService<IFilterRepository>();
        var filters = await filterRepo.GetAllFiltersAsync();
        var assignments = await filterRepo.GetAllAssignmentsAsync();
        var scheduled = await services.GetRequiredService<IScheduledMessageRepository>().GetAllAsync();
        var snoozed = await services.GetRequiredService<ISnoozeRepository>().GetAllAsync();
        var muted = await services.GetRequiredService<IMuteRepository>().GetAllAsync();
        var drafts = await services.GetRequiredService<IDraftRepository>().GetAllAsync();
        var theme = services.GetRequiredService<IThemeService>();

        var data = new AppData
        {
            Favorites = Keys(await services.GetRequiredService<IFavoriteRepository>().GetFavoriteThreadIdsAsync()),
            Filters = filters.Select(f => new BackupFilter(f.Name, f.ColorHex,
                Keys(assignments.Where(a => a.Value.Contains(f.Id)).Select(a => a.Key)))).ToList(),
            QuickReplies = (await services.GetRequiredService<IQuickReplyRepository>().GetAllAsync()).OrderBy(q => q.SortOrder).Select(q => q.Text).ToList(),
            Scheduled = scheduled.Select(s => new BackupScheduled(
                Key(s.ThreadId) ?? ConversationKey.From(string.IsNullOrEmpty(s.GroupAddresses) ? new[] { s.Address } : s.GroupAddresses.Split(',')),
                s.Address, s.Body, s.SendAtUtc, s.GroupAddresses)).ToList(),
            Archived = Keys(await services.GetRequiredService<IArchiveRepository>().GetArchivedThreadIdsAsync()),
            Trashed = Keys(await services.GetRequiredService<ITrashRepository>().GetTrashedThreadIdsAsync()),
            Snoozed = snoozed.Where(s => Key(s.ThreadId) is not null).Select(s => new BackupTimed(Key(s.ThreadId)!, s.UntilUtc)).ToList(),
            Muted = muted.Where(m => Key(m.ThreadId) is not null).Select(m => new BackupTimed(Key(m.ThreadId)!, m.UntilUtc)).ToList(),
            Blocked = (await services.GetRequiredService<IContactBlockService>().GetBlockedNumbersAsync()).ToList(),
            Allowed = (await services.GetRequiredService<IAllowedSenderRepository>().GetAllAsync()).ToList(),
            Drafts = drafts.Where(d => Key(d.Key) is not null && !string.IsNullOrWhiteSpace(d.Value)).Select(d => new BackupDraft(Key(d.Key)!, d.Value)).ToList(),
            Settings = new BackupSettings(
                services.GetRequiredService<IDisplayStyleService>().GetDisplaySettings(),
                services.GetRequiredService<INotificationSettingsStore>().Get(),
                theme.GetThemeMode().ToString(),
                theme.GetAccentColor())
        };
        return (data, participants, threadOf);
    }
}
```

- [ ] **Step 2: Implement the backup source**

`src/ForgeLinkSms/Platforms/Android/Backup/AndroidBackupSource.cs`:

```csharp
using Android.Content;
using ForgeLinkSms.Core.Backup;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// Reads the SMS/MMS provider directly by raw column name (see MmsReader for why). Only the inbox
// and sent boxes are backed up — drafts, outbox and failed sends aren't real history.
internal sealed class AndroidBackupSource(Context context, IServiceProvider services) : IBackupSource
{
    private IReadOnlyDictionary<long, IReadOnlyList<string>> _participants = new Dictionary<long, IReadOnlyList<string>>();

    public async Task<AppData> ReadAppDataAsync()
    {
        var (data, participants, _) = await AppDataSnapshot.ReadAsync(services);
        _participants = participants;
        return data;
    }

    public int CountMessages() =>
        Count("content://sms", "type IN (1,2)") + Count("content://mms", "msg_box IN (1,2)");

    public IEnumerable<SourceMessage> ReadMessages() => ReadSms().Concat(ReadMms());

    private IEnumerable<SourceMessage> ReadSms()
    {
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse("content://sms")!,
            new[] { "thread_id", "address", "body", "date", "date_sent", "type", "read", "status" }, "type IN (1,2)", null, "date ASC");
        if (cursor is null)
        {
            yield break;
        }
        while (cursor.MoveToNext())
        {
            var threadId = cursor.GetLong(0);
            var address = cursor.GetString(1) ?? string.Empty;
            var message = new BackupMessage(
                IsMms: false,
                Addresses: ParticipantsOf(threadId, address),
                From: null,
                TimestampMs: cursor.GetLong(3),
                DateSentMs: cursor.GetLong(4),
                Outgoing: cursor.GetInt(5) == 2,
                Read: cursor.GetInt(6) == 1,
                Status: cursor.GetInt(7),
                Body: cursor.GetString(2),
                Subject: null,
                Attachments: Array.Empty<BackupAttachment>());
            yield return new SourceMessage(message, Array.Empty<SourceAttachment>());
        }
    }

    private IEnumerable<SourceMessage> ReadMms()
    {
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse("content://mms")!,
            new[] { "_id", "thread_id", "date", "date_sent", "msg_box", "read", "sub" }, "msg_box IN (1,2)", null, "date ASC");
        if (cursor is null)
        {
            yield break;
        }
        while (cursor.MoveToNext())
        {
            var id = cursor.GetLong(0);
            var threadId = cursor.GetLong(1);
            var outgoing = cursor.GetInt(4) == 2;
            var (from, recipients) = ReadMmsAddresses(id);
            var (body, attachments) = ReadMmsParts(id);
            var message = new BackupMessage(
                IsMms: true,
                Addresses: ParticipantsOf(threadId, from ?? recipients.FirstOrDefault() ?? string.Empty, recipients),
                From: outgoing ? null : from,
                TimestampMs: cursor.GetLong(2) * 1000,
                DateSentMs: cursor.GetLong(3) * 1000,
                Outgoing: outgoing,
                Read: cursor.GetInt(5) == 1,
                Status: 0,
                Body: body,
                Subject: cursor.GetString(6),
                Attachments: Array.Empty<BackupAttachment>());
            yield return new SourceMessage(message, attachments);
        }
    }

    private IReadOnlyList<string> ParticipantsOf(long threadId, string fallback, IReadOnlyList<string>? more = null) =>
        _participants.TryGetValue(threadId, out var p) && p.Count > 0
            ? p
            : (more is { Count: > 0 } ? more.Append(fallback).Where(a => a.Length > 0).Distinct().ToList() : new[] { fallback });

    private (string? From, IReadOnlyList<string> Recipients) ReadMmsAddresses(long mmsId)
    {
        const int PduFrom = 137;
        string? from = null;
        var recipients = new List<string>();
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse($"content://mms/{mmsId}/addr")!, new[] { "address", "type" }, null, null, null);
        while (cursor is not null && cursor.MoveToNext())
        {
            var address = cursor.GetString(0);
            if (string.IsNullOrEmpty(address) || address == "insert-address-token")
            {
                continue;
            }
            if (cursor.GetInt(1) == PduFrom)
            {
                from = address;
            }
            else
            {
                recipients.Add(address);
            }
        }
        return (from, recipients);
    }

    private (string? Body, IReadOnlyList<SourceAttachment> Attachments) ReadMmsParts(long mmsId)
    {
        var text = new List<string>();
        var attachments = new List<SourceAttachment>();
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse("content://mms/part")!,
            new[] { "_id", "ct", "text", "name", "cl" }, "mid = ?", new[] { mmsId.ToString() }, "_id ASC");
        while (cursor is not null && cursor.MoveToNext())
        {
            var partId = cursor.GetLong(0);
            var contentType = cursor.GetString(1) ?? "application/octet-stream";
            if (contentType == "application/smil")
            {
                continue;
            }
            if (contentType == "text/plain")
            {
                var partText = cursor.GetString(2);
                if (!string.IsNullOrEmpty(partText))
                {
                    text.Add(partText);
                }
                continue;
            }
            var name = cursor.GetString(3) ?? cursor.GetString(4);
            var partUri = AndroidUri.Parse($"content://mms/part/{partId}")!;
            attachments.Add(new SourceAttachment(contentType, name,
                () => context.ContentResolver!.OpenInputStream(partUri) ?? throw new IOException($"Could not read attachment {partId}.")));
        }
        return (text.Count == 0 ? null : string.Join("\n", text), attachments);
    }

    private int Count(string uri, string selection)
    {
        using var cursor = context.ContentResolver!.Query(AndroidUri.Parse(uri)!, new[] { "_id" }, selection, null, null);
        return cursor?.Count ?? 0;
    }
}
```

- [ ] **Step 3: Implement the restore target**

`src/ForgeLinkSms/Platforms/Android/Backup/AndroidRestoreTarget.cs`:

```csharp
using Android.Content;
using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using AndroidTelephony = Android.Provider.Telephony;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// Writes to the SMS/MMS provider, which only the default SMS app may do.
internal sealed class AndroidRestoreTarget(Context context, IServiceProvider services) : IRestoreTarget
{
    private const int PduFrom = 137;
    private const int PduTo = 151;
    private const int MessageTypeSendReq = 128;
    private const int MessageTypeRetrieveConf = 132;

    public Task<IReadOnlyList<ExistingMessage>> ReadExistingMessagesAsync()
    {
        var source = new AndroidBackupSource(context, services);
        source.ReadAppDataAsync().GetAwaiter().GetResult();
        var existing = source.ReadMessages()
            .Select(m => new ExistingMessage(ConversationKey.From(m.Message.Addresses), m.Message.TimestampMs, m.Message.Outgoing, m.Message.Body, m.Attachments.Count))
            .ToList();
        return Task.FromResult<IReadOnlyList<ExistingMessage>>(existing);
    }

    public async Task<AppData> ReadAppDataAsync() => (await AppDataSnapshot.ReadAsync(services)).Data;

    public Task InsertSmsAsync(BackupMessage message)
    {
        var values = new ContentValues();
        values.Put("address", message.Addresses.FirstOrDefault() ?? string.Empty);
        values.Put("body", message.Body ?? string.Empty);
        values.Put("date", message.TimestampMs);
        values.Put("date_sent", message.DateSentMs);
        values.Put("type", message.Outgoing ? 2 : 1);
        values.Put("read", message.Read ? 1 : 0);
        values.Put("seen", 1);
        values.Put("status", message.Status);
        context.ContentResolver!.Insert(AndroidTelephony.Sms.ContentUri!, values);
        return Task.CompletedTask;
    }

    public Task InsertMmsAsync(BackupMessage message, Func<string, Stream> openMedia)
    {
        var resolver = context.ContentResolver!;
        var threadId = AndroidTelephony.Threads.GetOrCreateThreadId(context, message.Addresses.ToHashSet());
        var values = new ContentValues();
        values.Put("thread_id", threadId);
        values.Put("date", message.TimestampMs / 1000);
        values.Put("date_sent", message.DateSentMs / 1000);
        values.Put("msg_box", message.Outgoing ? 2 : 1);
        values.Put("read", message.Read ? 1 : 0);
        values.Put("seen", 1);
        values.Put("m_type", message.Outgoing ? MessageTypeSendReq : MessageTypeRetrieveConf);
        values.Put("ct_t", "application/vnd.wap.multipart.related");
        if (!string.IsNullOrEmpty(message.Subject))
        {
            values.Put("sub", message.Subject);
        }
        var mmsUri = resolver.Insert(AndroidUri.Parse("content://mms")!, values) ?? throw new IOException("The message store refused a picture message.");
        var mmsId = mmsUri.LastPathSegment!;

        void AddAddress(string address, int type)
        {
            var addr = new ContentValues();
            addr.Put("address", address);
            addr.Put("type", type);
            addr.Put("charset", 106);
            resolver.Insert(AndroidUri.Parse($"content://mms/{mmsId}/addr")!, addr);
        }
        if (message.Outgoing)
        {
            AddAddress("insert-address-token", PduFrom);
            foreach (var to in message.Addresses)
            {
                AddAddress(to, PduTo);
            }
        }
        else
        {
            var from = message.From ?? message.Addresses.FirstOrDefault() ?? string.Empty;
            AddAddress(from, PduFrom);
            foreach (var to in message.Addresses.Where(a => a != from))
            {
                AddAddress(to, PduTo);
            }
        }

        var partsUri = AndroidUri.Parse($"content://mms/{mmsId}/part")!;
        if (!string.IsNullOrEmpty(message.Body))
        {
            var text = new ContentValues();
            text.Put("mid", mmsId);
            text.Put("ct", "text/plain");
            text.Put("chset", 106);
            text.Put("cl", "text.txt");
            text.Put("text", message.Body);
            resolver.Insert(partsUri, text);
        }
        foreach (var attachment in message.Attachments)
        {
            var part = new ContentValues();
            part.Put("mid", mmsId);
            part.Put("ct", attachment.ContentType);
            var name = attachment.FileName ?? Path.GetFileName(attachment.Media);
            part.Put("cl", name);
            part.Put("name", name);
            var partUri = resolver.Insert(partsUri, part) ?? throw new IOException("The message store refused an attachment.");
            using var output = resolver.OpenOutputStream(partUri) ?? throw new IOException("Could not write an attachment.");
            using var input = openMedia(attachment.Media);
            input.CopyTo(output);
        }
        return Task.CompletedTask;
    }

    public async Task ApplyAsync(MergePlan plan)
    {
        var (_, _, threadOf) = await AppDataSnapshot.ReadAsync(services);
        long ThreadFor(string conversation) => threadOf.TryGetValue(conversation, out var id)
            ? id
            : AndroidTelephony.Threads.GetOrCreateThreadId(context, conversation.Split(',').ToHashSet());

        foreach (var conversation in plan.FavoritesToAdd)
        {
            await services.GetRequiredService<IFavoriteRepository>().FavoriteThreadAsync(ThreadFor(conversation));
        }

        var filterRepo = services.GetRequiredService<IFilterRepository>();
        foreach (var filter in plan.FiltersToCreate)
        {
            await filterRepo.CreateFilterAsync(filter.Name, filter.ColorHex);
        }
        var filtersByName = (await filterRepo.GetAllFiltersAsync()).GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        foreach (var member in plan.MembersToAdd)
        {
            if (filtersByName.TryGetValue(member.FilterName, out var filterId))
            {
                await filterRepo.AssignFilterAsync(ThreadFor(member.Conversation), filterId);
            }
        }

        var quickReplies = services.GetRequiredService<IQuickReplyRepository>();
        foreach (var text in plan.QuickRepliesToAdd)
        {
            await quickReplies.AddAsync(text);
        }

        var scheduler = services.GetRequiredService<IMessageSchedulerService>();
        foreach (var s in plan.ScheduledToAdd)
        {
            if (string.IsNullOrEmpty(s.GroupAddresses))
            {
                await scheduler.ScheduleAsync(s.Address, s.Body, s.SendAtUtc);
            }
            else
            {
                await scheduler.ScheduleGroupAsync(ThreadFor(s.Conversation), s.GroupAddresses.Split(','), s.Body, s.SendAtUtc);
            }
        }

        foreach (var conversation in plan.ArchiveToApply)
        {
            await services.GetRequiredService<IArchiveRepository>().ArchiveThreadAsync(ThreadFor(conversation));
        }
        foreach (var conversation in plan.TrashToApply)
        {
            await services.GetRequiredService<ITrashRepository>().TrashThreadAsync(ThreadFor(conversation));
        }
        foreach (var snooze in plan.SnoozesToApply)
        {
            await services.GetRequiredService<ISnoozeService>().SnoozeAsync(ThreadFor(snooze.Conversation), snooze.UntilUtc!.Value);
        }
        foreach (var mute in plan.MutesToApply)
        {
            await services.GetRequiredService<IMuteRepository>().MuteAsync(ThreadFor(mute.Conversation), mute.UntilUtc);
        }
        foreach (var number in plan.BlockedToAdd)
        {
            await services.GetRequiredService<IContactBlockService>().BlockAsync(number);
        }
        foreach (var address in plan.AllowedToAdd)
        {
            await services.GetRequiredService<IAllowedSenderRepository>().AllowAsync(address);
        }
        foreach (var draft in plan.DraftsToAdd)
        {
            await services.GetRequiredService<IDraftRepository>().SaveAsync(ThreadFor(draft.Conversation), draft.Text);
        }

        if (plan.Settings is { } settings)
        {
            services.GetRequiredService<IDisplayStyleService>().SaveDisplaySettings(settings.Display);
            services.GetRequiredService<INotificationSettingsStore>().Save(settings.Notifications);
            var theme = services.GetRequiredService<IThemeService>();
            if (Enum.TryParse<ThemeMode>(settings.ThemeMode, out var mode))
            {
                theme.SetThemeMode(mode);
            }
            theme.SetAccentColor(settings.AccentColor);
        }
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ForgeLinkSms -c Release -f net10.0-android -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false` → Build succeeded. Run all Core tests → pass.

- [ ] **Step 5: Stage**

```bash
git add src/ForgeLinkSms/Platforms/Android/Backup
```

---

### Task 10: Workers, pickers, service, Settings UI

**Files:**
- Modify: `src/ForgeLinkSms/ForgeLinkSms.csproj` (WorkManager package), `src/ForgeLinkSms/Platforms/Android/AndroidManifest.xml`, `src/ForgeLinkSms/Platforms/Android/MainActivity.cs`, `src/ForgeLinkSms/MauiProgram.cs`, `src/ForgeLinkSms/Pages/Settings/SettingsPage.razor`
- Create: `src/ForgeLinkSms/Platforms/Android/Backup/ActivityResultBridge.cs`, `BackupFiles.cs`, `BackupNotifier.cs`, `BackupWorker.cs`, `RestoreWorker.cs`, `BackupService.cs`

**Interfaces:**
- Consumes: Tasks 6–9, `IBackupService` (Task 8).
- Produces: working Settings → Backup section.

- [ ] **Step 1: Add WorkManager and manifest entries**

Run: `dotnet add src/ForgeLinkSms package Xamarin.AndroidX.Work.Runtime --version 2.11.2.1`. Build; if R8 reports duplicate classes, pin the conflicting AndroidX `*.Ktx` package to the version MAUI already uses (as done for `Fragment.Ktx`) with a one-line XML comment explaining why.

`AndroidManifest.xml` — add `xmlns:tools="http://schemas.android.com/tools"` to `<manifest>`, these permissions next to the others:

```xml
    <uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
    <uses-permission android:name="android.permission.FOREGROUND_SERVICE_DATA_SYNC" />
```

and inside `<application>`:

```xml
        <!-- Backups and restores run as WorkManager foreground work (a long data copy); Android 14+
             requires the foreground service type to be declared. -->
        <service
            android:name="androidx.work.impl.foreground.SystemForegroundService"
            android:foregroundServiceType="dataSync"
            tools:node="merge" />
```

- [ ] **Step 2: Activity-result bridge**

`src/ForgeLinkSms/Platforms/Android/Backup/ActivityResultBridge.cs`:

```csharp
using Android.App;
using Android.Content;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// MAUI has no API for the system "Save as" and folder pickers, so results come back through MainActivity.
internal static class ActivityResultBridge
{
    private static int _nextRequestCode = 7300;
    private static readonly Dictionary<int, TaskCompletionSource<AndroidUri?>> Pending = new();

    public static Task<AndroidUri?> StartAsync(Intent intent)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No activity to show the picker.");
        var code = Interlocked.Increment(ref _nextRequestCode);
        var tcs = new TaskCompletionSource<AndroidUri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Pending)
        {
            Pending[code] = tcs;
        }
        activity.StartActivityForResult(intent, code);
        return tcs.Task;
    }

    public static bool Complete(int requestCode, Result resultCode, Intent? data)
    {
        TaskCompletionSource<AndroidUri?>? tcs;
        lock (Pending)
        {
            if (!Pending.Remove(requestCode, out tcs))
            {
                return false;
            }
        }
        tcs.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
        return true;
    }
}
```

`MainActivity.cs` — add:

```csharp
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (!Backup.ActivityResultBridge.Complete(requestCode, resultCode, data))
        {
            base.OnActivityResult(requestCode, resultCode, data);
        }
    }
```

- [ ] **Step 3: Files, notifications, workers**

`src/ForgeLinkSms/Platforms/Android/Backup/BackupFiles.cs`:

```csharp
using Android.Content;
using Android.Provider;
using ForgeLinkSms.Core.Backup;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

internal static class BackupFiles
{
    public const string WeeklyFolderKey = "backup_weekly_folder";
    public const string StatusKey = "backup_last_status";
    public const string PasswordKey = "backup_password";
    public const string MimeType = "application/octet-stream";

    public static AndroidUri CreateInFolder(Context context, AndroidUri tree, string fileName)
    {
        var parent = DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!;
        return DocumentsContract.CreateDocument(context.ContentResolver!, parent, MimeType, fileName)
            ?? throw new IOException("Could not create the backup file in the chosen folder.");
    }

    public static void DeleteOldWeekly(Context context, AndroidUri tree)
    {
        var treeId = DocumentsContract.GetTreeDocumentId(tree);
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, treeId)!;
        var byName = new Dictionary<string, string>();
        using (var cursor = context.ContentResolver!.Query(children, new[] { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName }, null, null, null))
        {
            while (cursor is not null && cursor.MoveToNext())
            {
                byName[cursor.GetString(1) ?? string.Empty] = cursor.GetString(0)!;
            }
        }
        foreach (var name in BackupRetention.FilesToDelete(byName.Keys))
        {
            DocumentsContract.DeleteDocument(context.ContentResolver!, DocumentsContract.BuildDocumentUriUsingTree(tree, byName[name])!);
        }
    }

    public static void TryDelete(Context context, AndroidUri uri)
    {
        try
        {
            DocumentsContract.DeleteDocument(context.ContentResolver!, uri);
        }
        catch (Exception)
        {
        }
    }

    public static string DisplayName(Context context, AndroidUri uri)
    {
        using var cursor = context.ContentResolver!.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) ?? "backup" : "backup";
    }
}
```

`src/ForgeLinkSms/Platforms/Android/Backup/BackupNotifier.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Work;

namespace ForgeLinkSms.Platforms.Android.Backup;

internal static class BackupNotifier
{
    private const string ChannelId = "backup";
    public const int ProgressId = 7301;
    public const int ResultId = 7302;

    private static void EnsureChannel(Context context)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            if (manager.GetNotificationChannel(ChannelId) is null)
            {
                manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Backup and restore", NotificationImportance.Low));
            }
        }
    }

    public static ForegroundInfo Progress(Context context, string title, int done, int total, Java.Util.UUID workId)
    {
        EnsureChannel(context);
        var cancel = WorkManager.GetInstance(context).CreateCancelPendingIntent(workId);
        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(total > 0 ? $"{done:N0} of {total:N0} messages" : "Getting ready…")
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownload)
            .SetOngoing(true)
            .SetProgress(Math.Max(total, 1), done, total == 0)
            .AddAction(0, "Cancel", cancel)
            .Build()!;
        return Build.VERSION.SdkInt >= BuildVersionCodes.Q
            ? new ForegroundInfo(ProgressId, notification, (int)global::Android.Content.PM.ForegroundService.TypeDataSync)
            : new ForegroundInfo(ProgressId, notification);
    }

    public static void Result(Context context, string title, string text)
    {
        EnsureChannel(context);
        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(text))
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownloadDone)
            .SetAutoCancel(true)
            .Build()!;
        NotificationManagerCompat.From(context).Notify(ResultId, notification);
    }
}
```

`src/ForgeLinkSms/Platforms/Android/Backup/BackupWorker.cs`:

```csharp
using System.Text.Json;
using Android.Content;
using AndroidX.Work;
using ForgeLinkSms.Core.Backup;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

// Input "uri" = a file from the Save-as picker; without it, writes a new file into the weekly folder.
public sealed class BackupWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    public override Result DoWork()
    {
        var context = ApplicationContext;
        var targetUri = InputData.GetString("uri");
        var weekly = string.IsNullOrEmpty(targetUri);
        AndroidUri? file = null;
        try
        {
            SetForegroundAsync(BackupNotifier.Progress(context, "Backing up messages", 0, 0, Id)).Get();
            if (weekly)
            {
                var folder = Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty);
                if (string.IsNullOrEmpty(folder))
                {
                    return Result.InvokeSuccess();
                }
                file = BackupFiles.CreateInFolder(context, AndroidUri.Parse(folder)!, BackupRetention.FileNameFor(DateTime.Now));
            }
            else
            {
                file = AndroidUri.Parse(targetUri)!;
            }

            var password = SecureStorage.GetAsync(BackupFiles.PasswordKey).GetAwaiter().GetResult();
            var source = new AndroidBackupSource(context, MauiApplication.Current.Services);
            var progress = new InlineProgress(p =>
            {
                if (IsStopped)
                {
                    throw new OperationCanceledException();
                }
                SetForegroundAsync(BackupNotifier.Progress(context, "Backing up messages", p.Done, p.Total, Id));
            });
            BackupManifest manifest;
            using (var output = context.ContentResolver!.OpenOutputStream(file, "wt") ?? throw new IOException("Could not open the backup file."))
            {
                manifest = BackupRunner.RunAsync(source, output, password, AppInfo.Current.VersionString, DateTimeOffset.UtcNow,
                    progress, CancellationToken.None).GetAwaiter().GetResult();
            }
            if (weekly)
            {
                BackupFiles.DeleteOldWeekly(context, AndroidUri.Parse(Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty))!);
            }
            var summary = $"{manifest.SmsCount + manifest.MmsCount:N0} messages and {manifest.MediaFiles:N0} photos/videos saved.";
            SaveStatus(true, summary);
            BackupNotifier.Result(context, "Backup complete", summary);
            return Result.InvokeSuccess();
        }
        catch (Exception) when (IsStopped)
        {
            CleanUp(context, file, weekly, "Backup cancelled.");
            return Result.InvokeFailure();
        }
        catch (Exception e)
        {
            var reason = e is BackupException ? e.Message : $"Backup failed: {e.Message}";
            CleanUp(context, file, weekly, reason);
            BackupNotifier.Result(context, "Backup didn't finish", reason);
            return Result.InvokeFailure();
        }
    }

    private static void CleanUp(Context context, AndroidUri? file, bool weekly, string reason)
    {
        if (file is not null)
        {
            BackupFiles.TryDelete(context, file);
        }
        SaveStatus(false, reason);
    }

    internal static void SaveStatus(bool ok, string message) =>
        Preferences.Set(BackupFiles.StatusKey, JsonSerializer.Serialize(new StoredStatus(DateTimeOffset.UtcNow, ok, message)));

    internal sealed record StoredStatus(DateTimeOffset When, bool Ok, string Message);
}

// Progress<T> posts reports to another thread, where a cancel exception would crash the app; this
// reports on the job's own thread so throwing OperationCanceledException stops the runner cleanly.
internal sealed class InlineProgress(Action<BackupProgress> report) : IProgress<BackupProgress>
{
    public void Report(BackupProgress value) => report(value);
}
```

Cancellation arrives through `IsStopped` (WorkManager stops a worker rather than passing a token); the progress callback turns it into an `OperationCanceledException` on the job's own thread.

`src/ForgeLinkSms/Platforms/Android/Backup/RestoreWorker.cs`:

```csharp
using Android.Content;
using AndroidX.Work;
using ForgeLinkSms.Core.Backup;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

public sealed class RestoreWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    public const string PendingPasswordKey = "restore_pending_password";

    public override Result DoWork()
    {
        var context = ApplicationContext;
        var uri = AndroidUri.Parse(InputData.GetString("uri"))!;
        var includeSettings = InputData.GetBoolean("settings", false);
        try
        {
            SetForegroundAsync(BackupNotifier.Progress(context, "Restoring messages", 0, 0, Id)).Get();
            var password = SecureStorage.GetAsync(PendingPasswordKey).GetAwaiter().GetResult();
            SecureStorage.Remove(PendingPasswordKey);
            using var input = context.ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Could not open the backup file.");
            using var reader = BackupReader.Open(input, password, Path.Combine(FileSystem.CacheDirectory, "restore"));
            var target = new AndroidRestoreTarget(context, MauiApplication.Current.Services);
            var progress = new InlineProgress(p =>
            {
                if (IsStopped)
                {
                    throw new OperationCanceledException();
                }
                SetForegroundAsync(BackupNotifier.Progress(context, "Restoring messages", p.Done, p.Total, Id));
            });
            var result = RestoreRunner.RunAsync(reader, target, includeSettings, DateTimeOffset.UtcNow, progress, CancellationToken.None).GetAwaiter().GetResult();
            var summary = $"Added {result.Added:N0} messages. {result.Skipped:N0} were already on this phone.";
            BackupWorker.SaveStatus(true, "Restore: " + summary);
            BackupNotifier.Result(context, "Restore complete", summary);
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            SecureStorage.Remove(PendingPasswordKey);
            var reason = e is BackupException ? e.Message : IsStopped ? "Restore cancelled. Running it again is safe." : $"Restore failed: {e.Message}";
            BackupWorker.SaveStatus(false, reason);
            BackupNotifier.Result(context, "Restore didn't finish", reason);
            return Result.InvokeFailure();
        }
    }
}
```

- [ ] **Step 4: Backup service**

`src/ForgeLinkSms/Platforms/Android/Backup/BackupService.cs`:

```csharp
using System.Text.Json;
using Android.Content;
using AndroidX.Work;
using ForgeLinkSms.Core.Backup;
using ForgeLinkSms.Core.Services;
using AndroidUri = Android.Net.Uri;

namespace ForgeLinkSms.Platforms.Android.Backup;

public sealed class BackupService : IBackupService
{
    private const string WeeklyWorkName = "forgelink-weekly-backup";
    private const string ManualWorkName = "forgelink-backup";
    private const string RestoreWorkName = "forgelink-restore";
    private static Context Context => global::Android.App.Application.Context;

    public BackupStatus GetStatus()
    {
        BackupWorker.StoredStatus? last = null;
        try
        {
            var json = Preferences.Get(BackupFiles.StatusKey, string.Empty);
            last = json.Length > 0 ? JsonSerializer.Deserialize<BackupWorker.StoredStatus>(json) : null;
        }
        catch (JsonException)
        {
        }
        var folder = Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty);
        var hasPassword = !string.IsNullOrEmpty(SecureStorage.GetAsync(BackupFiles.PasswordKey).GetAwaiter().GetResult());
        return new BackupStatus(last?.When, last?.Ok, last?.Message, folder.Length > 0,
            folder.Length > 0 ? BackupFiles.DisplayName(Context, DocumentsTreeRoot(folder)) : null, hasPassword, IsRunning());
    }

    public async Task<bool> BackUpNowAsync()
    {
        var folder = Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty);
        var data = new Data.Builder();
        if (folder.Length == 0)
        {
            var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType(BackupFiles.MimeType);
            intent.PutExtra(Intent.ExtraTitle, BackupRetention.FileNameFor(DateTime.Now));
            var uri = await ActivityResultBridge.StartAsync(intent);
            if (uri is null)
            {
                return false;
            }
            data.PutString("uri", uri.ToString());
        }
        Enqueue<BackupWorker>(ManualWorkName, data.Build());
        return true;
    }

    public async Task<bool> EnableWeeklyAsync()
    {
        var uri = await ActivityResultBridge.StartAsync(new Intent(Intent.ActionOpenDocumentTree));
        if (uri is null)
        {
            return false;
        }
        Context.ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        Preferences.Set(BackupFiles.WeeklyFolderKey, uri.ToString());
        var constraints = new Constraints.Builder().SetRequiresCharging(true).SetRequiresBatteryNotLow(true).Build();
        var request = new PeriodicWorkRequest.Builder(Java.Lang.Class.FromType(typeof(BackupWorker)), 7, Java.Util.Concurrent.TimeUnit.Days!)
            .SetConstraints(constraints)
            .Build();
        WorkManager.GetInstance(Context).EnqueueUniquePeriodicWork(WeeklyWorkName, ExistingPeriodicWorkPolicy.Update!, request);
        return true;
    }

    public void DisableWeekly()
    {
        WorkManager.GetInstance(Context).CancelUniqueWork(WeeklyWorkName);
        var folder = Preferences.Get(BackupFiles.WeeklyFolderKey, string.Empty);
        if (folder.Length > 0)
        {
            try
            {
                Context.ContentResolver!.ReleasePersistableUriPermission(AndroidUri.Parse(folder)!, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            }
            catch (Java.Lang.SecurityException)
            {
            }
        }
        Preferences.Remove(BackupFiles.WeeklyFolderKey);
    }

    public async Task SetPasswordAsync(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            SecureStorage.Remove(BackupFiles.PasswordKey);
        }
        else
        {
            await SecureStorage.SetAsync(BackupFiles.PasswordKey, password);
        }
    }

    public async Task<RestoreFile?> PickRestoreFileAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        var uri = await ActivityResultBridge.StartAsync(intent);
        if (uri is null)
        {
            return null;
        }
        var header = new byte[4];
        using (var input = Context.ContentResolver!.OpenInputStream(uri))
        {
            var read = input?.Read(header, 0, 4) ?? 0;
            header = header[..Math.Max(read, 0)];
        }
        return new RestoreFile(uri.ToString()!, BackupFiles.DisplayName(Context, uri), BackupCrypto.IsEncrypted(header));
    }

    public async Task StartRestoreAsync(RestoreFile file, string? password, bool includeSettings)
    {
        SecureStorage.Remove(RestoreWorker.PendingPasswordKey);
        if (!string.IsNullOrEmpty(password))
        {
            await SecureStorage.SetAsync(RestoreWorker.PendingPasswordKey, password);
        }
        var data = new Data.Builder().PutString("uri", file.Uri).PutBoolean("settings", includeSettings).Build();
        Enqueue<RestoreWorker>(RestoreWorkName, data);
    }

    private static void Enqueue<TWorker>(string name, Data data) where TWorker : Worker
    {
        var request = new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(TWorker)))
            .SetInputData(data)
            .SetExpedited(OutOfQuotaPolicy.RunAsNonExpeditedWorkRequest!)
            .Build();
        WorkManager.GetInstance(Context).EnqueueUniqueWork(name, ExistingWorkPolicy.Keep!, request);
    }

    // GetWorkInfosForUniqueWork(...).Get() returns a Java list; if the binding exposes it as a plain
    // Java.Lang.Object, wrap it with Android.Runtime.JavaList<WorkInfo>.FromJniHandle before iterating.
    private static bool IsRunning()
    {
        foreach (var name in new[] { ManualWorkName, RestoreWorkName })
        {
            var infos = (System.Collections.IList)WorkManager.GetInstance(Context).GetWorkInfosForUniqueWork(name).Get()!;
            foreach (WorkInfo info in infos)
            {
                if (info.GetState() == WorkInfo.State.Running || info.GetState() == WorkInfo.State.Enqueued)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static AndroidUri DocumentsTreeRoot(string tree) =>
        global::Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(AndroidUri.Parse(tree)!, global::Android.Provider.DocumentsContract.GetTreeDocumentId(AndroidUri.Parse(tree)!))!;
}
```

`MauiProgram.cs` — add `builder.Services.AddSingleton<IBackupService, Platforms.Android.Backup.BackupService>();` next to the other `AddSingleton<I…Service>` lines.

- [ ] **Step 5: Settings → Backup section**

In `SettingsPage.razor` add `@inject ForgeLinkSms.Core.Services.IBackupService Backups`, then after the Privacy section:

```razor
    <h4 style="margin:24px 0 4px;">Backup</h4>
    <div style="@HintStyle">Saves all texts, photos and videos, plus favorites, filters, quick replies, scheduled texts and settings, to a file you choose. Restoring only adds what's missing — nothing is deleted.</div>
    <div style="margin:8px 4px;">
        @if (_backup.LastRunUtc is { } when)
        {
            <span>@(_backup.LastSucceeded == true ? "✅" : "⚠️") Last: @when.ToLocalTime().ToString("MMM d, h:mm tt") — @_backup.LastMessage</span>
        }
        else
        {
            <span style="@HintStyle">No backups yet.</span>
        }
        @if (_backup.IsRunning)
        {
            <div style="@HintStyle">A backup or restore is running — see the notification.</div>
        }
    </div>
    <div style="display:flex;flex-direction:column;gap:6px;">
        <button @onclick="BackUpNow" disabled="@_backup.IsRunning" style="@PresetStyle">💾 Back up now</button>
        <label style="display:flex;align-items:center;gap:10px;padding:10px 4px;">
            <input type="checkbox" checked="@_backup.WeeklyEnabled" @onchange="ToggleWeekly" style="width:20px;height:20px;" />
            <span>Back up automatically every week (while charging)@(_backup.WeeklyFolderName is { } f ? $" to {f}" : "")<br /><span style="@HintStyle">Keeps the newest 4 backups in that folder.</span></span>
        </label>
        <button @onclick="() => _passwordSheet = true" style="@PresetStyle">🔒 @(_backup.HasPassword ? "Change or remove backup password" : "Protect backups with a password")</button>
        <button @onclick="PickRestore" disabled="@_backup.IsRunning" style="@PresetStyle">♻️ Restore from a backup</button>
    </div>

    @if (_passwordSheet)
    {
        <div style="position:fixed;inset:0;background:rgba(0,0,0,0.4);display:flex;align-items:flex-end;z-index:20;" @onclick="() => _passwordSheet = false">
            <div style="background:var(--card-color);width:100%;padding:16px;border-radius:16px 16px 0 0;" @onclick:stopPropagation="true">
                <div style="font-weight:600;">Backup password</div>
                <div style="@HintStyle;margin:6px 0 10px;">If you forget it, backups made with it can't be opened — there's no way to recover it.</div>
                <input type="password" @bind="_newPassword" placeholder="New password" style="width:100%;padding:10px;margin-bottom:8px;font:inherit;" />
                <input type="password" @bind="_confirmPassword" placeholder="Type it again" style="width:100%;padding:10px;font:inherit;" />
                @if (_passwordError is not null) { <div style="color:#b91c1c;margin-top:6px;">@_passwordError</div> }
                <div style="display:flex;gap:10px;justify-content:flex-end;margin-top:12px;flex-wrap:wrap;">
                    @if (_backup.HasPassword) { <button @onclick="RemovePassword" style="@PresetStyle">Remove password</button> }
                    <button @onclick="SavePassword" style="@PresetStyle">Save</button>
                </div>
            </div>
        </div>
    }

    @if (_restoreFile is { } restoreFile)
    {
        <div style="position:fixed;inset:0;background:rgba(0,0,0,0.4);display:flex;align-items:flex-end;z-index:20;" @onclick="() => _restoreFile = null">
            <div style="background:var(--card-color);width:100%;padding:16px;border-radius:16px 16px 0 0;" @onclick:stopPropagation="true">
                <div style="font-weight:600;">Restore @restoreFile.FileName?</div>
                <div style="@HintStyle;margin:6px 0 10px;">Adds messages and ForgeLink data that aren't already on this phone. Nothing is deleted, and running it again is safe.</div>
                @if (restoreFile.IsEncrypted)
                {
                    <input type="password" @bind="_restorePassword" placeholder="Backup password" style="width:100%;padding:10px;font:inherit;" />
                }
                <label style="display:flex;align-items:center;gap:10px;padding:10px 0;">
                    <input type="checkbox" @bind="_restoreSettings" style="width:20px;height:20px;" /> Also restore my settings
                </label>
                <div style="display:flex;gap:10px;justify-content:flex-end;">
                    <button @onclick="() => _restoreFile = null" style="@PresetStyle">Cancel</button>
                    <button @onclick="StartRestore" disabled="@(restoreFile.IsEncrypted && string.IsNullOrEmpty(_restorePassword))" style="@PresetStyle">Restore</button>
                </div>
            </div>
        </div>
    }
```

and in `@code`:

```csharp
    private ForgeLinkSms.Core.Services.BackupStatus _backup = new(null, null, null, false, null, false, false);
    private bool _passwordSheet;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string? _passwordError;
    private ForgeLinkSms.Core.Services.RestoreFile? _restoreFile;
    private string _restorePassword = string.Empty;
    private bool _restoreSettings;

    private void RefreshBackup() => _backup = Backups.GetStatus();

    private async Task BackUpNow()
    {
        await Backups.BackUpNowAsync();
        RefreshBackup();
    }

    private async Task ToggleWeekly(ChangeEventArgs e)
    {
        if (e.Value is true)
        {
            await Backups.EnableWeeklyAsync();
        }
        else
        {
            Backups.DisableWeekly();
        }
        RefreshBackup();
    }

    private async Task SavePassword()
    {
        if (_newPassword.Length < 6)
        {
            _passwordError = "Use at least 6 characters.";
            return;
        }
        if (_newPassword != _confirmPassword)
        {
            _passwordError = "The two passwords don't match.";
            return;
        }
        await Backups.SetPasswordAsync(_newPassword);
        _newPassword = _confirmPassword = string.Empty;
        _passwordError = null;
        _passwordSheet = false;
        RefreshBackup();
    }

    private async Task RemovePassword()
    {
        await Backups.SetPasswordAsync(null);
        _passwordSheet = false;
        RefreshBackup();
    }

    private async Task PickRestore()
    {
        _restorePassword = string.Empty;
        _restoreSettings = false;
        _restoreFile = await Backups.PickRestoreFileAsync();
    }

    private async Task StartRestore()
    {
        if (_restoreFile is { } file)
        {
            await Backups.StartRestoreAsync(file, file.IsEncrypted ? _restorePassword : null, _restoreSettings);
            _restoreFile = null;
            RefreshBackup();
        }
    }
```

Call `RefreshBackup();` at the end of `OnInitializedAsync`.

- [ ] **Step 6: Build and install**

Run: `dotnet test tests/ForgeLinkSms.Core.Tests` → pass. Build the release APK (command in Task 9 Step 4) → Build succeeded. `adb install -r …-Signed.apk` → Success.

- [ ] **Step 7: Stage**

```bash
git add src/ForgeLinkSms
```

---

### Task 11: Device verification and docs

- [ ] **Step 1: Back up to Downloads** — Settings → Backup → Back up now → choose Downloads → notification shows progress, then "Backup complete — N messages and M photos/videos saved." Settings shows ✅ and the summary. The file `ForgeLink-backup-*.flbackup` exists in Downloads.

- [ ] **Step 2: Restore the same file onto the same phone** — Restore from a backup → pick that file → Restore → notification "Added 0 messages. N were already on this phone." Conversation list unchanged; no duplicated messages, filters, quick replies or scheduled texts.

- [ ] **Step 3: Encrypted round trip** — set a password, Back up now, then Restore that file: without a password the Restore button stays disabled; a wrong password ends with "That password doesn't open this backup."; the right password ends with "Added 0 messages…". Then remove the password.

- [ ] **Step 4: Weekly** — turn on weekly backup, pick a folder; confirm the switch shows the folder name; `adb shell dumpsys jobscheduler | grep -i forgelink` lists the periodic job. Turn it off again unless the user wants it on.

- [ ] **Step 5: Cancel** — start Back up now and tap Cancel in the notification; the partial file is removed and Settings shows "Backup cancelled."

- [ ] **Step 6: Docs** — in `docs/superpowers/specs/2026-09-30-backup-restore-design.md`: replace the restore "see a summary (counts and date)" step with "see a confirm sheet; counts are reported when it finishes", and remove "forwarded-message markers" from `forgelink.json` contents, noting why (message ids differ on another phone). Note in `docs/play-store` answers that the app now declares the `dataSync` foreground service type (Play Console → App content → Foreground service permissions: "Backup and restore of the user's messages to a file they choose").

- [ ] **Step 7: Stage**

```bash
git add docs
```
