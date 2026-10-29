namespace PhotoSense.Domain.Services;

/// <summary>Shows a file in the viewer or player of the machine the service runs on.</summary>
public interface ISystemViewer
{
    /// <summary>
    /// Opens a file in the application the operating system uses for its type, on the machine this
    /// service runs on. Throws when the file is missing or nothing could be started.
    /// </summary>
    void Open(string path);
}
