namespace PhotoSense.Domain.Services;

public interface ISystemViewer
{
    /// <summary>
    /// Opens a file in the application the operating system uses for its type, on the machine this
    /// service runs on. Throws when the file is missing or nothing could be started.
    /// </summary>
    void Open(string path);
}
