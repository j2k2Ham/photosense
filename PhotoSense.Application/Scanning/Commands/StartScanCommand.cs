namespace PhotoSense.Application.Scanning.Commands;

public sealed record StartScanCommand(string PrimaryLocation, string SecondaryLocation, bool Recursive = true);
