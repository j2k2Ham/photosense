using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trovolve.Infrastructure.Locations;

/// <summary>
/// How a location's keys, passwords and tokens are written before a phone seals them away (the iPhone's Keychain, a file
/// sealed by the Android Keystore): one JSON object of names and values, as they have been written since they were first
/// kept. One format for both phones, written by code made when Trovolve is built, so that a phone's trimmed app keeps
/// every type it needs.
/// </summary>
public static class LocationSecretsFormat
{
    /// <summary>The secrets as the bytes to seal.</summary>
    /// <param name="secrets">The location's names and values.</param>
    public static byte[] Write(IReadOnlyDictionary<string, string> secrets)
        => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>(secrets), SecretsJson.Default.DictionaryStringString);

    /// <summary>The secrets the bytes say, once unsealed; none when they say nothing.</summary>
    /// <param name="bytes">What was sealed.</param>
    /// <exception cref="JsonException">The bytes are not secrets written by <see cref="Write"/>.</exception>
    public static Dictionary<string, string> Read(ReadOnlySpan<byte> bytes)
        => JsonSerializer.Deserialize(bytes, SecretsJson.Default.DictionaryStringString) ?? [];
}

/// <summary>The JSON of a location's secrets, made when Trovolve is built.</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SecretsJson : JsonSerializerContext;
