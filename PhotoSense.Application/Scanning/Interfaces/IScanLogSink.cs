namespace PhotoSense.Application.Scanning.Interfaces;

/// <summary>Where a scan writes what it is doing, for the page to show.</summary>
public interface IScanLogSink
{
    /// <summary>Writes one line of a scan's log.</summary>
    /// <param name="instanceId">The scan it is about.</param>
    /// <param name="level">"Info", "Warn" or "Error".</param>
    /// <param name="message">The line, in words for the person.</param>
    void Log(string instanceId, string level, string message);
}
