using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PhotoSense.Application.Transfer;

/// <summary>
/// Messages and pieces of files between a phone and a computer once they have agreed on keys, each sealed with
/// AES-GCM: no one else on the network can read them, and anything changed or put in on the way is noticed. Each
/// direction has its own key, and each frame its own number, so that none can be sent again or taken out unseen.
/// </summary>
public sealed class SecureChannel : IDisposable
{
    private const int Tag = 16;
    private const byte Message = 0, Data = 1;
    private readonly Stream _stream;
    private readonly AesGcm _send, _receive;
    private ulong _sent, _received;

    public SecureChannel(Stream stream, byte[] sendKey, byte[] receiveKey)
    {
        _stream = stream;
        _send = new AesGcm(sendKey, Tag);
        _receive = new AesGcm(receiveKey, Tag);
    }

    /// <summary>The most of a file one frame carries.</summary>
    public const int PieceSize = 1 << 20;

    public Task SendAsync(JsonObject message, CancellationToken ct) => SendAsync(Message, Encoding.UTF8.GetBytes(message.ToJsonString()), ct);

    public Task SendDataAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => SendAsync(Data, data, ct);

    /// <summary>The next message, which has to be one of those expected.</summary>
    public async Task<JsonObject> ReceiveAsync(CancellationToken ct, params string[] expected)
    {
        var (kind, body) = await ReceiveSealedAsync(ct);
        if (kind != Message) throw new TransferException("The other device sent something PhotoSense does not understand.");
        return Wire.Expect(body, expected);
    }

    /// <summary>The next piece of a file.</summary>
    public async Task<byte[]> ReceiveDataAsync(CancellationToken ct)
    {
        var (kind, body) = await ReceiveSealedAsync(ct);
        return kind == Data ? body : throw new TransferException("The other device sent something PhotoSense does not understand.");
    }

    private async Task SendAsync(byte kind, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var plain = new byte[1 + body.Length];
        plain[0] = kind;
        body.CopyTo(plain.AsMemory(1));
        var frame = new byte[plain.Length + Tag];
        _send.Encrypt(Nonce(_sent++), plain, frame.AsSpan(0, plain.Length), frame.AsSpan(plain.Length));
        await Wire.WriteFrameAsync(_stream, frame, ct);
    }

    private async Task<(byte Kind, byte[] Body)> ReceiveSealedAsync(CancellationToken ct)
    {
        var frame = await Wire.ReadFrameAsync(_stream, ct);
        if (frame.Length <= Tag) throw new TransferException("The other device sent something PhotoSense does not understand.");
        var plain = new byte[frame.Length - Tag];
        try { _receive.Decrypt(Nonce(_received++), frame.AsSpan(0, plain.Length), frame.AsSpan(plain.Length), plain); }
        catch (AuthenticationTagMismatchException) { throw new TransferException("Something on the way changed what was sent, so PhotoSense stopped. Try again."); }
        return (plain[0], plain[1..]);
    }

    private static byte[] Nonce(ulong number)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), number);
        return nonce;
    }

    public void Dispose()
    {
        _send.Dispose();
        _receive.Dispose();
    }
}
