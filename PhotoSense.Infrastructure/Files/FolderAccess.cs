using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Files;

/// <summary>On a computer PhotoSense sees whatever folders the person can, so there is nothing to choose or allow.</summary>
public sealed class FolderAccess : IPhotoAccess
{
    /// <inheritdoc />
    public PhotoAccess Current => PhotoAccess.Everything;

    /// <summary>Nothing to choose: done at once.</summary>
    public Task ChooseMoreAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>A computer has no such page; nothing happens.</summary>
    public void OpenSettings() { }
}
