namespace PhotoSense.Domain.Services;

/// <summary>
/// What a phone's own photo library says of a picture beyond its file: whether it is a screenshot, and the burst it
/// was taken in. An iPhone's Photos knows both; elsewhere a screenshot is told by its name or folder (<see
/// cref="ValueObjects.MediaKinds"/>) and a burst by its time (<see cref="Matching.Bursts"/>).
/// </summary>
public interface IPhotoKinds
{
    /// <summary>Whether the picture at <paramref name="path"/> is a picture of the screen, as the library marks it.</summary>
    bool IsScreenshot(string path);

    /// <summary>The burst the picture at <paramref name="path"/> was taken in, as the library marks it; null for none.</summary>
    string? BurstOf(string path);
}
