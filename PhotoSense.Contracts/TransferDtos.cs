using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Transfer;

/// <summary>The files to offer to a computer, by the ids Organize knows them by.</summary>
public sealed class SendStartRequest
{
    [JsonPropertyName("files")] public List<string> Files { get; init; } = [];
}

/// <summary>Whether the person found the numbers on the phone and the computer the same.</summary>
public sealed class TransferConfirmRequest
{
    [JsonPropertyName("same")] public bool Same { get; init; }
}

/// <summary>Where sending photos from this phone has got to.</summary>
public sealed class SendStateDto
{
    /// <summary>"idle", "waiting", "comparing", "sending", "sent" or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = "idle";
    /// <summary>The number to compare with the computer's, while it is being compared: "482 917".</summary>
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("computer")] public string? Computer { get; init; }
    /// <summary>Files the computer has said are safely there.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    /// <summary>The names of the pictures and videos every file of which is safely on the computer, which can be deleted from the phone.</summary>
    [JsonPropertyName("deletable")] public List<string> Deletable { get; init; } = [];
    /// <summary>The folder on the computer the files went to, when they all went to one.</summary>
    [JsonPropertyName("where")] public string? Where { get; init; }
    [JsonPropertyName("phone")] public string Phone { get; init; } = string.Empty;
    [JsonPropertyName("problem")] public string? Problem { get; init; }
}

/// <summary>How many pictures and videos were deleted from the phone.</summary>
public sealed class DeleteSentDto
{
    [JsonPropertyName("deleted")] public int Deleted { get; init; }
}

/// <summary>A phone offering photos, found by this computer.</summary>
public sealed class FoundPhoneDto
{
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>"wifi" or "usb".</summary>
    [JsonPropertyName("via")] public string Via { get; init; } = string.Empty;
}

public sealed class FoundPhonesDto
{
    [JsonPropertyName("phones")] public List<FoundPhoneDto> Phones { get; init; } = [];
}

/// <summary>Receive what a phone found offers, into a folder of this computer.</summary>
public sealed class ReceiveStartRequest
{
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    [JsonPropertyName("folder")] public string? Folder { get; init; }
}

/// <summary>Where receiving photos from a phone has got to.</summary>
public sealed class ReceiveStateDto
{
    /// <summary>"idle", "connecting", "comparing", "receiving", "done" or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = "idle";
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    [JsonPropertyName("done")] public int Done { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    /// <summary>Files of which the very same file was in the folder already.</summary>
    [JsonPropertyName("alreadyThere")] public int AlreadyThere { get; init; }
    [JsonPropertyName("folder")] public string? Folder { get; init; }
    [JsonPropertyName("problem")] public string? Problem { get; init; }
}
