using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Transfer;

/// <summary>Where pairing a phone with a computer has got to, on whichever of the two answers.</summary>
public sealed class PairingStateDto
{
    /// <summary>
    /// On a computer: "idle", "waiting", "comparing", "paired" or "failed". On a phone: "idle", "scanning", "finding",
    /// "connecting", "comparing", "paired" or "failed".
    /// </summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = "idle";
    /// <summary>On a computer while waiting: the QR code's text.</summary>
    [JsonPropertyName("link")] public string? Link { get; init; }
    /// <summary>On a computer while waiting: the six digits to type on the phone.</summary>
    [JsonPropertyName("code")] public string? Code { get; init; }
    /// <summary>On a computer while waiting: when the code and the QR code change (ISO 8601).</summary>
    [JsonPropertyName("expires")] public string? Expires { get; init; }
    /// <summary>While comparing: the number to compare with the other's, "482 917".</summary>
    [JsonPropertyName("check")] public string? Check { get; init; }
    /// <summary>The other device: the phone on a computer, the computer on a phone.</summary>
    [JsonPropertyName("other")] public string? Other { get; init; }
    /// <summary>On a phone once paired: where the computer puts what it backs up.</summary>
    [JsonPropertyName("folder")] public string? Folder { get; init; }
    /// <summary>Why pairing stopped.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
    /// <summary>On a computer: what it is called, as phones show it.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
}

/// <summary>A name for this computer, as phones show it.</summary>
public sealed class ComputerNameRequest
{
    /// <summary>The name.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
}

/// <summary>The code typed on the phone.</summary>
public sealed class PairingCodeRequest
{
    /// <summary>The six digits the computer shows.</summary>
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
}
