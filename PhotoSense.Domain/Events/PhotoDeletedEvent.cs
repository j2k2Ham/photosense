using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Domain.Events;

public sealed record PhotoDeletedEvent(Guid EventId, DateTime OccurredUtc, PhotoId PhotoId, string? SourcePath);
