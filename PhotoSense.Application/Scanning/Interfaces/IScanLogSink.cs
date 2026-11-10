namespace PhotoSense.Application.Scanning.Interfaces;

/// <summary>One line of a scan's log, as the page is sent it.</summary>
/// <param name="InstanceId">The scan it is about.</param>
/// <param name="Timestamp">When it was written.</param>
/// <param name="Level">"Info", "Warn" or "Error".</param>
/// <param name="Message">The line, in words for the person.</param>
public sealed record ScanLogLine(string InstanceId, DateTime Timestamp, string Level, string Message);

/// <summary>Where a scan writes what it is doing, for the page to show.</summary>
public interface IScanLogSink
{
    /// <summary>Writes one line of a scan's log.</summary>
    /// <param name="instanceId">The scan it is about.</param>
    /// <param name="level">"Info", "Warn" or "Error".</param>
    /// <param name="message">The line, in words for the person.</param>
    void Log(string instanceId, string level, string message);

    /// <summary>The oldest lines not yet handed over, oldest first; each line is handed over once.</summary>
    /// <param name="max">The most lines to hand over at once.</param>
    IReadOnlyList<ScanLogLine> TakePending(int max);
}
