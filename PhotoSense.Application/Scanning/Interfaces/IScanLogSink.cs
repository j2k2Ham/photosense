namespace PhotoSense.Application.Scanning.Interfaces;

public interface IScanLogSink
{
    void Log(string instanceId, string level, string message);
}
