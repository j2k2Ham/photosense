using System.Text.Json.Serialization;

namespace Trovolve.Contracts.Organize;

/// <summary>A place the phone backs up to, as the page lists it.</summary>
public sealed class LocationDto
{
    /// <summary>How Trovolve knows it: "computer", "google-drive", or an id of its own for one added.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>What sort of place it is: "computer", "googleDrive", "oneDrive", "dropbox", "amazonS3", "s3Compatible", "azureBlob", "webDav" or "sftp".</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    /// <summary>What it is called: the service's name, or the name the person gave one they added.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>Backups go to it.</summary>
    [JsonPropertyName("on")] public bool On { get; init; }
    /// <summary>Signed in to, paired or added: it can be turned on.</summary>
    [JsonPropertyName("connected")] public bool Connected { get; init; }
    /// <summary>It can be signed in to from this version of Trovolve.</summary>
    [JsonPropertyName("available")] public bool Available { get; init; }
    /// <summary>The account signed in to, or the user name.</summary>
    [JsonPropertyName("account")] public string? Account { get; init; }
    /// <summary>How it is reached, less its secrets.</summary>
    [JsonPropertyName("settings")] public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
    /// <summary>Files it holds, checked.</summary>
    [JsonPropertyName("copies")] public int Copies { get; init; }
    /// <summary>When a backup to it last finished.</summary>
    [JsonPropertyName("lastBackup")] public DateTimeOffset? LastBackup { get; init; }
    /// <summary>When its copies were last checked.</summary>
    [JsonPropertyName("lastChecked")] public DateTimeOffset? LastChecked { get; init; }
    /// <summary>Why the last backup or check of it stopped.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
}

/// <summary>How far one location has got in what the locations are doing.</summary>
public sealed class LocationProgressDto
{
    /// <summary>The location.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>Files done.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    /// <summary>Files to do.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>Bytes sent.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>Bytes to send.</summary>
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    /// <summary>Why it stopped part way.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
}

/// <summary>What sending to a location did.</summary>
public sealed class SendResultDto
{
    /// <summary>Where they went.</summary>
    [JsonPropertyName("locationId")] public string LocationId { get; init; } = string.Empty;
    /// <summary>Pictures and videos sent.</summary>
    [JsonPropertyName("items")] public int Items { get; init; }
    /// <summary>Those every file of which matches the copy there.</summary>
    [JsonPropertyName("matched")] public int Matched { get; init; }
    /// <summary>What those take on this device.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
}

/// <summary>What the locations are doing, or last did.</summary>
public sealed class LocationActivityDto
{
    /// <summary>"backup", "check" or "send".</summary>
    [JsonPropertyName("task")] public string Task { get; init; } = string.Empty;
    /// <summary>"running", "ready" once done, "paused" or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = string.Empty;
    /// <summary>Each location's part.</summary>
    [JsonPropertyName("locations")] public IReadOnlyList<LocationProgressDto> Locations { get; init; } = [];
    /// <summary>What a send did, once done.</summary>
    [JsonPropertyName("sent")] public SendResultDto? Sent { get; init; }
}

/// <summary>The locations, the Safe to delete rule, and what they are doing.</summary>
public sealed class LocationsDto
{
    /// <summary>The computer, the cloud services, then those added, by name.</summary>
    [JsonPropertyName("locations")] public IReadOnlyList<LocationDto> Locations { get; init; } = [];
    /// <summary>On how many locations a copy must be checked before a photo counts as backed up: 1 or 2.</summary>
    [JsonPropertyName("countAt")] public int CountAt { get; init; }
    /// <summary>More than one location may be on, and Connect to server add more.</summary>
    [JsonPropertyName("pro")] public bool Pro { get; init; }
    /// <summary>What they are doing, or last did since Trovolve started.</summary>
    [JsonPropertyName("activity")] public LocationActivityDto? Activity { get; init; }

    /// <summary>On the free plan, the id of the location backups have gone to, which the plan is held to; null on Pro or before the first backup.</summary>
    [JsonPropertyName("held")] public string? Held { get; init; }
}

/// <summary>How much a location holds.</summary>
public sealed class LocationSpaceDto
{
    /// <summary>All it can hold, in bytes.</summary>
    [JsonPropertyName("total")] public long Total { get; init; }
    /// <summary>What is used, in bytes.</summary>
    [JsonPropertyName("used")] public long Used { get; init; }
}

/// <summary>A location in full.</summary>
public sealed class LocationDetailsDto
{
    /// <summary>The location.</summary>
    [JsonPropertyName("location")] public LocationDto Location { get; init; } = new();
    /// <summary>Its Trovolve folder, as the person would recognise it.</summary>
    [JsonPropertyName("where")] public string? Where { get; init; }
    /// <summary>How much it holds, when it says.</summary>
    [JsonPropertyName("space")] public LocationSpaceDto? Space { get; init; }
    /// <summary>Why it could not be reached just now.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
}

/// <summary>A location as Connect to server adds it, or changes one added.</summary>
public sealed class ServerConnectionDto
{
    /// <summary>The location changed; null to add one.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }
    /// <summary>What sort it is: "amazonS3", "s3Compatible", "azureBlob", "webDav" or "sftp".</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    /// <summary>What the person calls it.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>How it is reached, less its secrets.</summary>
    [JsonPropertyName("settings")] public Dictionary<string, string> Settings { get; init; } = [];
    /// <summary>Its keys and passwords.</summary>
    [JsonPropertyName("secrets")] public Dictionary<string, string> Secrets { get; init; } = [];
}

/// <summary>A location's id, once added.</summary>
public sealed class LocationIdDto
{
    /// <summary>The location's id.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
}

/// <summary>A name for a location added by Connect to server.</summary>
public sealed class LocationNameDto
{
    /// <summary>The name.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
}

/// <summary>On how many locations a copy must be checked before a photo counts as backed up.</summary>
public sealed class CountAtDto
{
    /// <summary>1 (any) or 2.</summary>
    [JsonPropertyName("countAt")] public int CountAt { get; init; }
}

/// <summary>What to send to a location: Library's pictures and videos, by their ids.</summary>
public sealed class SendToDto
{
    /// <summary>The ids, as Library knows them.</summary>
    [JsonPropertyName("ids")] public IReadOnlyList<string> Ids { get; init; } = [];
}
