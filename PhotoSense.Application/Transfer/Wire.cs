using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace PhotoSense.Application.Transfer;

/// <summary>Why sending photos between a phone and a computer stopped, in words for the person.</summary>
public sealed class TransferException(string message) : IOException(message);

/// <summary>
/// What a phone and a computer say to each other: frames, each its length in four bytes and then that many bytes.
/// Until the two have agreed on keys a frame holds a message in JSON; after, everything is sealed (<see cref="SecureChannel"/>).
/// </summary>
public static class Wire
{
    /// <summary>The largest frame either side takes: a piece of a file and what seals it.</summary>
    public const int LargestFrame = (1 << 20) + 64;

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        await stream.WriteAsync(length, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            var length = new byte[4];
            await stream.ReadExactlyAsync(length, ct);
            var size = BinaryPrimitives.ReadInt32BigEndian(length);
            if (size is < 0 or > LargestFrame) throw new TransferException("The other device sent something PhotoSense does not understand.");
            var payload = new byte[size];
            await stream.ReadExactlyAsync(payload, ct);
            return payload;
        }
        catch (EndOfStreamException)
        {
            throw new TransferException("The other device stopped answering.");
        }
    }

    /// <summary>A message before the keys are agreed: in plain JSON, with what it is in "t".</summary>
    public static Task SendAsync(Stream stream, JsonObject message, CancellationToken ct) => WriteFrameAsync(stream, Encoding.UTF8.GetBytes(message.ToJsonString()), ct);

    /// <summary>The next message, which has to be the one expected.</summary>
    public static async Task<JsonObject> ReceiveAsync(Stream stream, string expected, CancellationToken ct) => Expect(await ReadFrameAsync(stream, ct), expected);

    /// <summary>A message read from its bytes, which has to be one of those expected; one saying the other side is busy says so.</summary>
    public static JsonObject Expect(byte[] bytes, params string[] expected)
    {
        JsonObject? message;
        try { message = JsonNode.Parse(bytes) as JsonObject; }
        catch (System.Text.Json.JsonException) { message = null; }
        var kind = message is null ? null : TextOrNull(message, "t");
        if (kind == "busy") throw new TransferException("The phone is already sending to another computer.");
        if (kind == "stop") throw new TransferException(TextOrNull(message!, "why") ?? "The other device stopped.");
        if (kind is null || !expected.Contains(kind)) throw new TransferException("The other device sent something PhotoSense does not understand.");
        return message!;
    }

    /// <summary>A text field of a message, which has to be there.</summary>
    public static string Text(JsonObject message, string name)
        => TextOrNull(message, name) ?? throw new TransferException("The other device sent something PhotoSense does not understand.");

    private static string? TextOrNull(JsonObject message, string name) => message[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    /// <summary>A field of a message that holds bytes, in base 64.</summary>
    public static byte[] Bytes(JsonObject message, string name)
    {
        try { return Convert.FromBase64String(Text(message, name)); }
        catch (FormatException) { throw new TransferException("The other device sent something PhotoSense does not understand."); }
    }
}
