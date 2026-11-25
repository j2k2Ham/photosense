using Microsoft.Extensions.Logging;

namespace Trovolve.Application;

/// <summary>
/// What Trovolve's own services write to the program's log, each line with an event id of its own. They are written
/// through LoggerMessage, so that nothing is put together for a level that is off. The ids go by area: scans 1000,
/// changes 1100, removed files 1200, the plan 1300, backup and pairing 1400, locations 1500, usage 1600.
/// </summary>
/// <remarks>
/// A line names files and folders only where the person would need them to find what went wrong; the log stays on the
/// device it was written on.
/// </remarks>
internal static partial class TrovolveLog
{
    /// <summary>A scan began.</summary>
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Scan {Scan} started")]
    public static partial void ScanStarted(ILogger log, string scan);

    /// <summary>A scan went through every file it was given.</summary>
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Scan {Scan} finished")]
    public static partial void ScanFinished(ILogger log, string scan);

    /// <summary>A scan was stopped by the person; what it read is kept.</summary>
    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Scan {Scan} was stopped")]
    public static partial void ScanStopped(ILogger log, string scan);

    /// <summary>A scan was ended by an error nothing expected.</summary>
    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Scan {Scan} stopped on an error nothing expected")]
    public static partial void ScanFailed(ILogger log, Exception error, string scan);

    /// <summary>A line of a scan's log, or of what was done, as the page shows it.</summary>
    [LoggerMessage(EventId = 1004, Message = "{Source}: {Line}")]
    public static partial void PageLine(ILogger log, LogLevel level, string source, string line);

    /// <summary>A move, copy or removal was carried out in full.</summary>
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "{Kind} {Change} carried out: {Files} files, {Bytes} bytes")]
    public static partial void ChangeCarriedOut(ILogger log, string kind, Guid change, int files, long bytes);

    /// <summary>A change could not be carried out, and what it had done was put back.</summary>
    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "{Kind} {Change} could not be carried out: {Problem}")]
    public static partial void ChangeFailed(ILogger log, Exception error, string kind, Guid change, string problem);

    /// <summary>A change was undone.</summary>
    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "{Kind} {Change} undone: {Back} files back, {Left} left")]
    public static partial void ChangeUndone(ILogger log, string kind, Guid change, int back, int left);

    /// <summary>An undo could not be carried out, and what it had done was put back.</summary>
    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "Undoing {Kind} {Change} could not be carried out: {Problem}")]
    public static partial void UndoFailed(ILogger log, Exception error, string kind, Guid change, string problem);

    /// <summary>A phone sent a removal's files to its own Recently Deleted or bin.</summary>
    [LoggerMessage(EventId = 1104, Level = LogLevel.Information, Message = "Removal {Change} sent {Files} files ({Bytes} bytes) to the phone's bin")]
    public static partial void ChangeErased(ILogger log, Guid change, int files, long bytes);

    /// <summary>A phone did not let a removal's files go to its bin.</summary>
    [LoggerMessage(EventId = 1105, Level = LogLevel.Warning, Message = "Removal {Change} was refused by the phone")]
    public static partial void EraseRefused(ILogger log, Exception error, Guid change);

    /// <summary>What waited in the folders of removed files went to the bin, or was erased.</summary>
    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Removed files emptied: {Recycled} to the bin, {Erased} erased for good, {Bytes} bytes, {Problems} problems")]
    public static partial void RemovedEmptied(ILogger log, int recycled, int erased, long bytes, int problems);

    /// <summary>A file or folder of removed files could not be emptied.</summary>
    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "A removed file could not be emptied")]
    public static partial void RemovedProblem(ILogger log, Exception error);

    /// <summary>The phone's store could not be reached.</summary>
    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "The store could not be reached; what it last said stands")]
    public static partial void StoreUnreachable(ILogger log, Exception error);

    /// <summary>A backup, a send or a pairing with another device stopped.</summary>
    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "{What} stopped: {Problem}")]
    public static partial void DeviceTalkStopped(ILogger log, Exception error, string what, string problem);

    /// <summary>A backup to a computer finished.</summary>
    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Backup to {Computer} finished: {Files} files sent")]
    public static partial void BackupFinished(ILogger log, string computer, int files);

    /// <summary>A location could not be reached, or refused a file.</summary>
    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning, Message = "Location {Location}: {Problem}")]
    public static partial void LocationFailed(ILogger log, Exception error, string location, string problem);

    /// <summary>Uses of Trovolve's features were sent to the usage server.</summary>
    [LoggerMessage(EventId = 1600, Level = LogLevel.Debug, Message = "Sent {Count} uses to the usage server")]
    public static partial void UsageSent(ILogger log, int count);

    /// <summary>The usage server could not be reached; what was not sent waits for the next time.</summary>
    [LoggerMessage(EventId = 1601, Level = LogLevel.Warning, Message = "The usage server could not be reached; {Waiting} uses wait for the next time")]
    public static partial void UsageNotSent(ILogger log, Exception error, int waiting);
}
