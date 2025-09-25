namespace PhotoSense.Domain.Entities;

public class AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime UtcTimestamp { get; init; } = DateTime.UtcNow;
    public string Action { get; init; } = string.Empty;
    public string? PhotoId { get; init; }
    public string Details { get; init; } = string.Empty;
}