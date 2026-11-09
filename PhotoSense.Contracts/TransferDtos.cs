using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Transfer;

/// <summary>The files to offer to a computer, by the ids Organize knows them by.</summary>
public sealed class SendStartRequest
{
    /// <summary>The files, by their ids.</summary>
    [JsonPropertyName("files")] public List<string> Files { get; init; } = [];
}

/// <summary>Whether the person found the numbers on the phone and the computer the same.</summary>
public sealed class TransferConfirmRequest
{
    /// <summary>The person found them the same.</summary>
    [JsonPropertyName("same")] public bool Same { get; init; }
}

/// <summary>Where sending photos from this phone has got to.</summary>
public sealed class SendStateDto
{
    /// <summary>"idle", "waiting", "comparing", "sending", "sent" or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = "idle";
    /// <summary>The number to compare with the computer's, while it is being compared: "482 917".</summary>
    [JsonPropertyName("code")] public string? Code { get; init; }
    /// <summary>The computer's name, once known.</summary>
    [JsonPropertyName("computer")] public string? Computer { get; init; }
    /// <summary>Files the computer has said are safely there.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    /// <summary>Files offered.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>Bytes sent.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Bytes offered.</summary>
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    /// <summary>The names of the pictures and videos every file of which is safely on the computer, which can be deleted from the phone.</summary>
    [JsonPropertyName("deletable")] public List<string> Deletable { get; init; } = [];
    /// <summary>The folder on the computer the files went to, when they all went to one.</summary>
    [JsonPropertyName("where")] public string? Where { get; init; }
    /// <summary>What this phone is called, as the computer shows it.</summary>
    [JsonPropertyName("phone")] public string Phone { get; init; } = string.Empty;
    /// <summary>Why it stopped, when it failed.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
}

/// <summary>How many pictures and videos were deleted from the phone.</summary>
public sealed class DeleteSentDto
{
    /// <summary>How many went.</summary>
    [JsonPropertyName("deleted")] public int Deleted { get; init; }
}

/// <summary>A phone offering photos, found by this computer.</summary>
public sealed class FoundPhoneDto
{
    /// <summary>Names the phone in the request to receive from it.</summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
    /// <summary>What the phone is called.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>"wifi" or "usb".</summary>
    [JsonPropertyName("via")] public string Via { get; init; } = string.Empty;
}

/// <summary>The phones offering photos now.</summary>
public sealed class FoundPhonesDto
{
    /// <summary>The phones.</summary>
    [JsonPropertyName("phones")] public List<FoundPhoneDto> Phones { get; init; } = [];
}

/// <summary>Receive what a phone found offers, into a folder of this computer.</summary>
public sealed class ReceiveStartRequest
{
    /// <summary>The phone, by the key it was found under.</summary>
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    /// <summary>The folder the photos go to, by its full path.</summary>
    [JsonPropertyName("folder")] public string? Folder { get; init; }
}

/// <summary>Where receiving photos from a phone has got to.</summary>
public sealed class ReceiveStateDto
{
    /// <summary>"idle", "connecting", "comparing", "receiving", "done" or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = "idle";
    /// <summary>The number to compare with the phone's, while it is being compared.</summary>
    [JsonPropertyName("code")] public string? Code { get; init; }
    /// <summary>The phone's name, once known.</summary>
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    /// <summary>Files that have arrived, those already there among them.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    /// <summary>Files the phone offered.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>Bytes that have arrived.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Bytes the phone offered.</summary>
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    /// <summary>Files of which the very same file was in the folder already.</summary>
    [JsonPropertyName("alreadyThere")] public int AlreadyThere { get; init; }
    /// <summary>The folder the files go to.</summary>
    [JsonPropertyName("folder")] public string? Folder { get; init; }
    /// <summary>Why it stopped, when it failed.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
}
