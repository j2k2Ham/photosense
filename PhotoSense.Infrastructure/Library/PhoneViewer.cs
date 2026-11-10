using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Library;

/// <summary>A phone has no other program PhotoSense can hand a photo to yet.</summary>
public sealed class PhoneViewer : ISystemViewer
{
    /// <summary>Refuses: there is no other app to open it in yet.</summary>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public void Open(string path) => throw new InvalidOperationException("Photos cannot be opened in another app from PhotoSense on a phone yet.");
}
