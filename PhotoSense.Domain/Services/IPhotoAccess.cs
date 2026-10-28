namespace PhotoSense.Domain.Services;

/// <summary>How much the person has let PhotoSense see, where the system asks them (a phone's photo library).</summary>
public enum PhotoAccess { Everything, Selected, Nothing }

/// <summary>What the person has let PhotoSense see, and the system's own ways of changing that.</summary>
public interface IPhotoAccess
{
    PhotoAccess Current { get; }

    /// <summary>Shows the system's picker for choosing more photos for PhotoSense to see; done once it is put away.</summary>
    Task ChooseMoreAsync(CancellationToken ct);

    /// <summary>Opens PhotoSense's own page of the system's settings, where its access to the photos is changed.</summary>
    void OpenSettings();
}
