namespace PhotoSense.Application.Scanning.Events;

public sealed record ScanRequestedEvent(Guid CorrelationId, string PrimaryLocation, string SecondaryLocation, DateTime RequestedAtUtc);
