using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Files;

/// <summary>On a computer PhotoSense sees whatever folders the person can, so there is nothing to choose or allow.</summary>
public sealed class FolderAccess : IPhotoAccess
{
    public PhotoAccess Current => PhotoAccess.Everything;

    public Task ChooseMoreAsync(CancellationToken ct) => Task.CompletedTask;

    public void OpenSettings() { }
}
