namespace PhotoSense.Domain.Entities;

/// <summary>One thing done to photos, kept in the audit record. The store stamps it with the time it is kept.</summary>
public sealed record AuditEntry
{
    /// <summary>Tells this entry from every other.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>When it was kept; set by the store that keeps it.</summary>
    public DateTime UtcTimestamp { get; init; }
    /// <summary>What was done, as one word: "Delete", "Keep", "OrganizeMove".</summary>
    public string Action { get; init; } = string.Empty;
    /// <summary>The photo or change it was done to, when there is one.</summary>
    public string? PhotoId { get; init; }
    /// <summary>What was done, in a few words.</summary>
    public string Details { get; init; } = string.Empty;
}
