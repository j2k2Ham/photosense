namespace PhotoSense.Domain.Entities;

/// <summary>Which of the scanned folders a file was found in.</summary>
public enum PhotoSet
{
    /// <summary>Not recorded.</summary>
    Unknown = 0,
    /// <summary>The first folder the scan covered.</summary>
    Primary = 1,
    /// <summary>The second folder the scan covered.</summary>
    Secondary = 2
}
