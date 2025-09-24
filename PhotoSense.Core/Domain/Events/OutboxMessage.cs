namespace PhotoSense.Core.Domain.Events;

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public DateTime OccurredUtc { get; init; }
    public DateTime? ProcessedUtc { get; set; }
}