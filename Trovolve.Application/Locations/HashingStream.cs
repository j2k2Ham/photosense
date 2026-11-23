using System.Security.Cryptography;
using Trovolve.Domain.Services;

namespace Trovolve.Application.Locations;

/// <summary>
/// Reads a file through, working out as it goes every checksum a location may keep of it: SHA-256, MD5 and
/// Dropbox's own. A file is read once, whether it is being put somewhere or read back.
/// </summary>
public sealed class HashingStream : Stream
{
    /// <summary>How much of a file each of Dropbox's block checksums covers.</summary>
    public const int DropboxBlock = 4 * 1024 * 1024;

    private readonly Stream _inner;
    private readonly IncrementalHash _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly IncrementalHash _md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
    private readonly IncrementalHash _block = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly IncrementalHash _blocks = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private int _inBlock;
    private long _read;
    private Dictionary<ChecksumKind, string>? _done;

    /// <summary>Reads <paramref name="inner"/> through, which it closes when it is closed.</summary>
    public HashingStream(Stream inner) => _inner = inner;

    /// <summary>A checksum of everything read, once it has all been read.</summary>
    public string Of(ChecksumKind kind)
    {
        if (_done is null)
        {
            // Dropbox's last block, which may be short; an empty file has none.
            if (_inBlock > 0) _blocks.AppendData(_block.GetHashAndReset());
            _done = new()
            {
                [ChecksumKind.Sha256] = Convert.ToHexStringLower(_sha256.GetHashAndReset()),
                [ChecksumKind.Md5] = Convert.ToHexStringLower(_md5.GetHashAndReset()),
                [ChecksumKind.DropboxContent] = Convert.ToHexStringLower(_blocks.GetHashAndReset()),
            };
        }
        return _done[kind];
    }

    /// <summary>The SHA-256 of everything read, in lower-case hex.</summary>
    public string Sha256 => Of(ChecksumKind.Sha256);

    /// <summary>Whether the location's own checksum is of these very bytes.</summary>
    public bool Matches(RemoteChecksum checksum) => string.Equals(Of(checksum.Kind), checksum.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the whole of <paramref name="content"/> and returns its checksums.</summary>
    public static async Task<HashingStream> OfAsync(Stream content, CancellationToken ct)
    {
        var hashing = new HashingStream(content);
        await hashing.CopyToAsync(Stream.Null, ct);
        return hashing;
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        _sha256.AppendData(bytes);
        _md5.AppendData(bytes);
        _read += bytes.Length;
        while (bytes.Length > 0)
        {
            var take = Math.Min(bytes.Length, DropboxBlock - _inBlock);
            _block.AppendData(bytes[..take]);
            _inBlock += take;
            bytes = bytes[take..];
            if (_inBlock < DropboxBlock) continue;
            _blocks.AppendData(_block.GetHashAndReset());
            _inBlock = 0;
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        Append(buffer.AsSpan(offset, n));
        return n;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer, cancellationToken);
        Append(buffer.Span[..n]);
        return n;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position { get => _read; set => throw new NotSupportedException(); }

    /// <inheritdoc />
    public override void Flush() { }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // A stream has no finalizer, so this is only ever called to dispose.
        _inner.Dispose();
        foreach (var hash in new[] { _sha256, _md5, _block, _blocks }) hash.Dispose();
        base.Dispose(disposing);
    }
}
