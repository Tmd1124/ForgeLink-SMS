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
