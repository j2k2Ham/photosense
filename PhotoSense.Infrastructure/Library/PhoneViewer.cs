using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Library;

/// <summary>A phone has no other program PhotoSense can hand a photo to yet.</summary>
public sealed class PhoneViewer : ISystemViewer
{
    public void Open(string path) => throw new InvalidOperationException("Photos cannot be opened in another app from PhotoSense on a phone yet.");
}
