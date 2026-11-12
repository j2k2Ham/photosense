namespace PhotoSense.Domain.Services;

/// <summary>How much the person has let PhotoSense see, where the system asks them (a phone's photo library).</summary>
public enum PhotoAccess
{
    /// <summary>Every photo, as on a computer.</summary>
    Everything,
    /// <summary>Only the photos the person chose.</summary>
    Selected,
    /// <summary>None: the person said no, or has not been asked yet.</summary>
    Nothing
}

/// <summary>What the person has let PhotoSense see, and the system's own ways of changing that.</summary>
public interface IPhotoAccess
{
    /// <summary>What PhotoSense may see now.</summary>
    PhotoAccess Current { get; }

    /// <summary>
    /// Asks the system's own question of what PhotoSense may see; done once it is answered. A system that has been
    /// answered already may not ask again, and then nothing is shown.
    /// </summary>
    Task AskAsync(CancellationToken ct);

    /// <summary>Shows the system's picker for choosing more photos for PhotoSense to see; done once it is put away.</summary>
    Task ChooseMoreAsync(CancellationToken ct);

    /// <summary>Opens PhotoSense's own page of the system's settings, where its access to the photos is changed.</summary>
    void OpenSettings();
}
