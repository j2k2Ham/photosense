using System.Text.Json.Serialization;
using Trovolve.Contracts.Duplicates;

namespace Trovolve.Contracts.Cleaning;

/// <summary>About what a video's copy takes at each quality; null where a copy is not worth making, or the video's frame or length is not known.</summary>
public sealed class CompressionEstimatesDto
{
    /// <summary>At High: the video's own frame.</summary>
    [JsonPropertyName("high")] public long? High { get; init; }
    /// <summary>At Balanced: no more than 1080 lines.</summary>
    [JsonPropertyName("balanced")] public long? Balanced { get; init; }
    /// <summary>At Small: no more than 720 lines.</summary>
    [JsonPropertyName("small")] public long? Small { get; init; }
}

/// <summary>A large video the phone can compress, with about what its copy would take.</summary>
public sealed class CompressibleVideoDto
{
    /// <summary>The video.</summary>
    [JsonPropertyName("video")] public PhotoItemDto Video { get; init; } = new();
    /// <summary>About what its copy takes, by quality.</summary>
    [JsonPropertyName("estimates")] public CompressionEstimatesDto Estimates { get; init; } = new();
}

/// <summary>A video compressed.</summary>
public sealed class CompressedVideoDto
{
    /// <summary>The original.</summary>
    [JsonPropertyName("id")] public Guid Id { get; init; }
    /// <summary>The original's name.</summary>
    [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
    /// <summary>What the original takes.</summary>
    [JsonPropertyName("before")] public long Before { get; init; }
    /// <summary>What the copy takes.</summary>
    [JsonPropertyName("after")] public long After { get; init; }
}

/// <summary>How compressing stands.</summary>
public sealed class CompressionStateDto
{
    /// <summary>"idle", "running", "done", "stopped" or "failed".</summary>
    [JsonPropertyName("stage")] public string Stage { get; init; } = "idle";
    /// <summary>"high", "balanced" or "small".</summary>
    [JsonPropertyName("quality")] public string Quality { get; init; } = "balanced";
    /// <summary>How many videos it was given.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }
    /// <summary>How many are compressed.</summary>
    [JsonPropertyName("done")] public int Done { get; init; }
    /// <summary>The name of the video being compressed now.</summary>
    [JsonPropertyName("current")] public string? Current { get; init; }
    /// <summary>How far through them all, from 0 to 1.</summary>
    [JsonPropertyName("progress")] public double Progress { get; init; }
    /// <summary>Each video compressed.</summary>
    [JsonPropertyName("results")] public IReadOnlyList<CompressedVideoDto> Results { get; init; } = [];
    /// <summary>What the copies saved, all told.</summary>
    [JsonPropertyName("saved")] public long Saved { get; init; }
    /// <summary>Once it has ended, whether the originals went to the phone's bin; null while running, or when none was compressed.</summary>
    [JsonPropertyName("originalsDeleted")] public bool? OriginalsDeleted { get; init; }
    /// <summary>Why it failed.</summary>
    [JsonPropertyName("problem")] public string? Problem { get; init; }
}

/// <summary>The videos to compress, and how small.</summary>
public sealed class CompressStartRequest
{
    /// <summary>The videos, by their ids.</summary>
    [JsonPropertyName("ids")] public IReadOnlyList<Guid> Ids { get; init; } = [];
    /// <summary>"high", "balanced" or "small".</summary>
    [JsonPropertyName("quality")] public string Quality { get; init; } = "balanced";
}

/// <summary>A phone's notifications: which are on, and whether the phone lets Trovolve send any.</summary>
public sealed class NoticeStateDto
{
    /// <summary>Low-storage alerts are on.</summary>
    [JsonPropertyName("lowStorage")] public bool LowStorage { get; init; }
    /// <summary>The trial's reminder is on.</summary>
    [JsonPropertyName("trialReminder")] public bool TrialReminder { get; init; }
    /// <summary>The phone lets Trovolve send notifications.</summary>
    [JsonPropertyName("allowed")] public bool Allowed { get; init; }
}

/// <summary>Turns notifications on or off; one left out stays as it is.</summary>
public sealed class NoticeChangeRequest
{
    /// <summary>Low-storage alerts.</summary>
    [JsonPropertyName("lowStorage")] public bool? LowStorage { get; init; }
    /// <summary>The trial's reminder.</summary>
    [JsonPropertyName("trialReminder")] public bool? TrialReminder { get; init; }
}

/// <summary>What a notification tapped asked to open, once.</summary>
public sealed class NoticeOpenedDto
{
    /// <summary>"free-up-space", "subscription", or null when none was tapped.</summary>
    [JsonPropertyName("opens")] public string? Opens { get; init; }
}
