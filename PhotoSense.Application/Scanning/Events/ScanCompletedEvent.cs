namespace PhotoSense.Application.Scanning.Events;

public sealed record ScanCompletedEvent(Guid CorrelationId, int PhotosScannedPrimary, int PhotosScannedSecondary, int DuplicateGroupsFound, DateTime CompletedAtUtc);
