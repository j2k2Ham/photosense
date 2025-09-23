using PhotoSense.Core.Domain.ValueObjects;

namespace PhotoSense.Core.Domain.Events;

public sealed record PhotoDeletedEvent(Guid EventId, DateTime OccurredUtc, PhotoId PhotoId, string? SourcePath);
