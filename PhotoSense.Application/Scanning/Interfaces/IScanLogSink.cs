namespace PhotoSense.Application.Scanning.Interfaces;

public interface IScanLogSink
{
    void Log(string instanceId, string level, string message);
    IReadOnlyList<(DateTime ts,string level,string message)> GetRecent(string instanceId, int max = 200);
}
