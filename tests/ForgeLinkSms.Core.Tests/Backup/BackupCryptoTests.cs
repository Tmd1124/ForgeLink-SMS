using System.Security.Cryptography;
using ForgeLinkSms.Core.Backup;

namespace ForgeLinkSms.Core.Tests.Backup;

public class BackupCryptoTests
{
    private const int FastIterations = 1_000;

    // A backup over 4 GiB has more than 4,096 one-MiB chunks; the chunk number must never wrap,
    // or chunks could be swapped without detection.
    [Fact]
    public void Chunk_numbers_past_four_billion_stay_distinct()
    {
        Assert.NotEqual(BackupCrypto.Aad(5, false), BackupCrypto.Aad((1L << 32) + 5, false));
        Assert.NotEqual(BackupCrypto.Aad(int.MaxValue, false), BackupCrypto.Aad((long)int.MaxValue + 1, false));
    }

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
