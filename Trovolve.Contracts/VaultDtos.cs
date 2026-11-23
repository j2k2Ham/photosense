using System.Text.Json.Serialization;

namespace Trovolve.Contracts.Organize;

/// <summary>An item of the hidden vault, as its grid shows it once unlocked.</summary>
public sealed class VaultItemDto
{
    /// <summary>What the page knows it by.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>Its name, as Library showed it.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    /// <summary>When it was taken, as the camera's clock had it; null when nothing said.</summary>
    [JsonPropertyName("taken")] public DateTime? Taken { get; init; }
    /// <summary>It is a video.</summary>
    [JsonPropertyName("isVideo")] public bool IsVideo { get; init; }
    /// <summary>What its files take.</summary>
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    /// <summary>The computer holds it, sealed.</summary>
    [JsonPropertyName("onComputer")] public bool OnComputer { get; init; }
}

/// <summary>Hidden items being brought back from the computer.</summary>
public sealed class VaultRestoreDto
{
    /// <summary>"running", "ready" or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = string.Empty;
    /// <summary>Why it stopped.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
    /// <summary>How many items came back, once it is done.</summary>
    [JsonPropertyName("restored")] public int? Restored { get; init; }
}

/// <summary>How the hidden vault stands, and, once unlocked, what it holds.</summary>
public sealed class VaultDto
{
    /// <summary>"off", "locked" or "open".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = string.Empty;
    /// <summary>How many items it holds.</summary>
    [JsonPropertyName("count")] public int Count { get; init; }
    /// <summary>How many the computer does not hold yet, which deleting Trovolve would delete.</summary>
    [JsonPropertyName("notBackedUp")] public int NotBackedUp { get; init; }
    /// <summary>The computer backups go to; null before one is paired.</summary>
    [JsonPropertyName("computer")] public string? Computer { get; init; }
    /// <summary>What it holds, the latest hidden first; empty while locked.</summary>
    [JsonPropertyName("items")] public IReadOnlyList<VaultItemDto> Items { get; init; } = [];
    /// <summary>Hidden items being brought back from the computer, or the last time they were; null when they were not since Trovolve started.</summary>
    [JsonPropertyName("restore")] public VaultRestoreDto? Restore { get; init; }
}

/// <summary>The hidden vault's recovery key, as the person writes it down.</summary>
public sealed class RecoveryKeyDto
{
    /// <summary>"K7QP-2M9X-…".</summary>
    [JsonPropertyName("recoveryKey")] public string RecoveryKey { get; init; } = string.Empty;
}

/// <summary>Pictures and videos, or items of the hidden vault, by their ids.</summary>
public sealed class VaultIdsRequest
{
    /// <summary>The ids: as Library knows them to hide, as the vault knows them otherwise.</summary>
    [JsonPropertyName("ids")] public IReadOnlyList<string> Ids { get; init; } = [];
}

/// <summary>Whether the hidden vault was unlocked.</summary>
public sealed class VaultUnlockDto
{
    /// <summary>The person passed the phone's question.</summary>
    [JsonPropertyName("unlocked")] public bool Unlocked { get; init; }
}
