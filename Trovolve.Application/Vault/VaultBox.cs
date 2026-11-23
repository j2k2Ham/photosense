using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Trovolve.Application.Vault;

/// <summary>A part of what an item of the hidden vault holds: one of its files, or a preview made when it was hidden.</summary>
/// <param name="Kind"><see cref="VaultBox.FilePart"/>, <see cref="VaultBox.ThumbnailPart"/> or <see cref="VaultBox.ViewPart"/>.</param>
/// <param name="Name">A file's own name; the preview's is empty.</param>
/// <param name="Offset">Where it starts in what the item holds.</param>
/// <param name="Length">How long it is.</param>
/// <param name="Sha256">A file's SHA-256 as it was in the library; null for a preview.</param>
public sealed record VaultPart(string Kind, string Name, long Offset, long Length, string? Sha256);

/// <summary>What an item of the hidden vault is, sealed with it: so that its file alone, on any phone with the key, says it all.</summary>
/// <param name="Name">The picture or video's name, as Library showed it.</param>
/// <param name="Folder">Where it was in the library, to put it back there.</param>
/// <param name="Taken">When it was taken; null when nothing said.</param>
/// <param name="Latitude">Where it was taken, in degrees north; null for nowhere.</param>
/// <param name="Longitude">Degrees east.</param>
/// <param name="IsVideo">It is a video.</param>
/// <param name="HiddenUtc">When it was hidden.</param>
/// <param name="Parts">Its files, the picture or video first, then its previews.</param>
public sealed record VaultMeta(string Name, string Folder, DateTime? Taken, double? Latitude, double? Longitude, bool IsVideo, DateTime HiddenUtc, IReadOnlyList<VaultPart> Parts)
{
    /// <summary>Its files, the picture or video first.</summary>
    public IEnumerable<VaultPart> Files => Parts.Where(p => p.Kind == VaultBox.FilePart);

    /// <summary>What its files take.</summary>
    public long Bytes => Files.Sum(f => f.Length);

    /// <summary>One of its previews; null when none was made, as for a video.</summary>
    public VaultPart? Preview(string kind) => Parts.FirstOrDefault(p => p.Kind == kind);
}

/// <summary>
/// How an item of the hidden vault is sealed in its file. A header says which key sealed it; then what it holds, in
/// pieces of <see cref="PieceSize"/> bytes, each sealed with AES-GCM under a nonce of its own and marked as the last
/// or not, so that a piece can be read alone and a file cut short is found out. What it holds is its files, its
/// previews, and last what it is (<see cref="VaultMeta"/>) with that part's length.
/// </summary>
public static class VaultBox
{
    /// <summary>How long each sealed piece is, before its tag.</summary>
    public const int PieceSize = 64 * 1024;

    /// <summary>What the header of a vault file takes.</summary>
    public const int HeaderSize = 32;

    /// <summary>A part that is one of the item's files.</summary>
    public const string FilePart = "file";

    /// <summary>A part that is its small preview.</summary>
    public const string ThumbnailPart = "thumb";

    /// <summary>A part that is the picture as the viewer shows it.</summary>
    public const string ViewPart = "view";

    /// <summary>The ending of a vault file's name.</summary>
    public const string Extension = ".psv";

    private const int Tag = 16;

    private static ReadOnlySpan<byte> Magic => "PSV1"u8;

    /// <summary>The id of the key that sealed a vault file, from its header; null for what is not a vault file.</summary>
    public static async Task<string?> KeyIdOfAsync(Stream file, CancellationToken ct)
    {
        var header = new byte[HeaderSize];
        if (await file.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, ct) < HeaderSize || !header.AsSpan(0, 4).SequenceEqual(Magic)) return null;
        return Convert.ToHexStringLower(header, 4, 16);
    }

    /// <summary>Writes a vault file, sealing what is written as it goes.</summary>
    public sealed class Writer : IDisposable
    {
        private readonly Stream _output;
        private readonly AesGcm _aes;
        private readonly byte[] _header = new byte[HeaderSize];
        private readonly byte[] _piece = new byte[PieceSize];
        private int _filled;
        private uint _count;
        private bool _started;

        /// <summary>A vault file written to <paramref name="output"/>, sealed with <paramref name="key"/>, known by <paramref name="keyId"/>.</summary>
        public Writer(Stream output, byte[] key, byte[] keyId)
        {
            _output = output;
            _aes = new AesGcm(key, Tag);
            Magic.CopyTo(_header);
            keyId.CopyTo(_header, 4);
            RandomNumberGenerator.Fill(_header.AsSpan(20, 8));
        }

        /// <summary>How much has been written so far, before sealing.</summary>
        public long Position { get; private set; }

        /// <summary>Writes a file, or a preview, as the next part. Returns the SHA-256 of what was written.</summary>
        public async Task<string> CopyAsync(Stream from, CancellationToken ct)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[PieceSize];
            for (int n; (n = await from.ReadAsync(buffer, ct)) > 0;)
            {
                hash.AppendData(buffer, 0, n);
                await WriteAsync(buffer.AsMemory(0, n), ct);
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        /// <summary>Writes these bytes as the next part.</summary>
        public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            if (!_started)
            {
                _started = true;
                await _output.WriteAsync(_header, ct);
            }
            while (data.Length > 0)
            {
                // A full piece is sealed only once more comes, so that the last can be marked as the last.
                if (_filled == PieceSize) await SealAsync(last: false, ct);
                var n = Math.Min(data.Length, PieceSize - _filled);
                data[..n].CopyTo(_piece.AsMemory(_filled));
                _filled += n;
                Position += n;
                data = data[n..];
            }
        }

        /// <summary>Writes what the item is, last, and seals the last piece.</summary>
        public async Task CompleteAsync(VaultMeta meta, CancellationToken ct)
        {
            var said = JsonSerializer.SerializeToUtf8Bytes(meta);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, said.Length);
            await WriteAsync(said, ct);
            await WriteAsync(length, ct);
            await SealAsync(last: true, ct);
            await _output.FlushAsync(ct);
        }

        private async Task SealAsync(bool last, CancellationToken ct)
        {
            var sealedPiece = new byte[_filled + Tag];
            _aes.Encrypt(Nonce(_header, _count), _piece.AsSpan(0, _filled), sealedPiece.AsSpan(0, _filled), sealedPiece.AsSpan(_filled), Associated(_header, last));
            await _output.WriteAsync(sealedPiece, ct);
            _count++;
            _filled = 0;
        }

        /// <inheritdoc />
        public void Dispose() => _aes.Dispose();
    }

    /// <summary>Reads a vault file, any part of it, opening only the pieces that part is in.</summary>
    public sealed class Reader : IDisposable
    {
        private readonly Stream _input;
        private readonly AesGcm _aes;
        private readonly byte[] _header;
        private readonly long _pieces;

        private Reader(Stream input, byte[] key, byte[] header)
        {
            _input = input;
            _aes = new AesGcm(key, Tag);
            _header = header;
            var body = input.Length - HeaderSize;
            _pieces = (body + PieceSize + Tag - 1) / (PieceSize + Tag);
            Length = (_pieces - 1) * PieceSize + (body - (_pieces - 1) * (PieceSize + Tag) - Tag);
        }

        /// <summary>How much the file holds, opened.</summary>
        public long Length { get; }

        /// <summary>Opens a vault file sealed with <paramref name="key"/>, known by <paramref name="keyId"/>.</summary>
        /// <exception cref="InvalidDataException">It is not a vault file, was sealed with another key, or was cut short.</exception>
        public static async Task<Reader> OpenAsync(Stream input, byte[] key, byte[] keyId, CancellationToken ct)
        {
            var header = new byte[HeaderSize];
            input.Position = 0;
            if (input.Length < HeaderSize + Tag + 4 || await input.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, ct) < HeaderSize
                || !header.AsSpan(0, 4).SequenceEqual(Magic))
                throw new InvalidDataException("This is not a file of the hidden vault.");
            if (!header.AsSpan(4, 16).SequenceEqual(keyId)) throw new InvalidDataException("This file of the hidden vault was sealed with another recovery key.");
            return new Reader(input, key, header);
        }

        /// <summary>What the item is.</summary>
        /// <exception cref="InvalidDataException">The file is damaged.</exception>
        public async Task<VaultMeta> MetaAsync(CancellationToken ct)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(await ReadAsync(Length - 4, 4, ct));
            if (length <= 0 || length > Length - 4) throw new InvalidDataException("This file of the hidden vault is damaged.");
            return JsonSerializer.Deserialize<VaultMeta>(await ReadAsync(Length - 4 - length, length, ct))
                ?? throw new InvalidDataException("This file of the hidden vault is damaged.");
        }

        /// <summary>A part of what the file holds.</summary>
        /// <exception cref="InvalidDataException">The file is damaged, or the part is not in it.</exception>
        public async Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct)
        {
            if (offset < 0 || count < 0 || offset + count > Length) throw new InvalidDataException("This file of the hidden vault is damaged.");
            var result = new byte[count];
            var done = 0;
            while (done < count)
            {
                var at = offset + done;
                var piece = await PieceAsync(at / PieceSize, ct);
                var from = (int)(at % PieceSize);
                var n = Math.Min(count - done, piece.Length - from);
                piece.AsSpan(from, n).CopyTo(result.AsSpan(done));
                done += n;
            }
            return result;
        }

        /// <summary>Writes a part of what the file holds to <paramref name="to"/>, a piece at a time. Returns its SHA-256.</summary>
        /// <exception cref="InvalidDataException">The file is damaged.</exception>
        public async Task<string> CopyToAsync(VaultPart part, Stream to, CancellationToken ct)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (var done = 0L; done < part.Length;)
            {
                var piece = await ReadAsync(part.Offset + done, (int)Math.Min(PieceSize, part.Length - done), ct);
                hash.AppendData(piece);
                await to.WriteAsync(piece, ct);
                done += piece.Length;
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        /// <summary>One of the item's files, to read from its start, opened a piece at a time as it is read.</summary>
        public Stream OpenPart(VaultPart part) => new PartStream(this, part);

        private async Task<byte[]> PieceAsync(long index, CancellationToken ct)
        {
            var sealedPiece = new byte[SealedSize(index)];
            _input.Position = HeaderSize + index * (PieceSize + Tag);
            await _input.ReadExactlyAsync(sealedPiece, ct);
            return Open(sealedPiece, index);
        }

        private byte[] Piece(long index)
        {
            var sealedPiece = new byte[SealedSize(index)];
            _input.Position = HeaderSize + index * (PieceSize + Tag);
            _input.ReadExactly(sealedPiece);
            return Open(sealedPiece, index);
        }

        private int SealedSize(long index) => (index == _pieces - 1 ? (int)(Length - index * PieceSize) : PieceSize) + Tag;

        private byte[] Open(byte[] sealedPiece, long index)
        {
            var size = sealedPiece.Length - Tag;
            var piece = new byte[size];
            try { _aes.Decrypt(Nonce(_header, (uint)index), sealedPiece.AsSpan(0, size), sealedPiece.AsSpan(size), piece, Associated(_header, index == _pieces - 1)); }
            catch (AuthenticationTagMismatchException) { throw new InvalidDataException("This file of the hidden vault is damaged."); }
            return piece;
        }

        // A part read from its start to its end, as a phone's library takes a file.
        private sealed class PartStream(Reader reader, VaultPart part) : Stream
        {
            private long _at;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => part.Length;
            public override long Position { get => _at; set => throw new NotSupportedException(); }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (Next(buffer.Length) is not (var index, var from, var n)) return 0;
                (await reader.PieceAsync(index, cancellationToken)).AsMemory(from, n).CopyTo(buffer);
                _at += n;
                return n;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Next(count) is not (var index, var from, var n)) return 0;
                reader.Piece(index).AsSpan(from, n).CopyTo(buffer.AsSpan(offset));
                _at += n;
                return n;
            }

            // The piece the next bytes are in, where in it they start, and how many of them to give; null at the end.
            private (long Index, int From, int Count)? Next(int wanted)
            {
                if (_at >= part.Length || wanted == 0) return null;
                var at = part.Offset + _at;
                var from = (int)(at % PieceSize);
                return (at / PieceSize, from, (int)Math.Min(wanted, Math.Min(PieceSize - from, part.Length - _at)));
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public void Dispose() => _aes.Dispose();
    }

    // The header's eight random bytes, then the piece's number: never the same twice under one key.
    private static byte[] Nonce(byte[] header, uint index)
    {
        var nonce = new byte[12];
        header.AsSpan(20, 8).CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), index);
        return nonce;
    }

    // The header and whether the piece is the last, so that neither can be changed, nor the file cut short unseen.
    private static byte[] Associated(byte[] header, bool last) => [.. header, last ? (byte)1 : (byte)0];
}
