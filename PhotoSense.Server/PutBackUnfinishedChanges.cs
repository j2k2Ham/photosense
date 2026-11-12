using Microsoft.Extensions.Hosting;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Server;

/// <summary>At start-up, puts back the moves, copies and removals that a stop of the service cut short.</summary>
internal sealed class PutBackUnfinishedChanges : IHostedService
{
    private readonly FileChanges _changes;
    private readonly IScanLogSink _log;

    /// <summary>Puts back through <paramref name="changes"/>, and says what became of each change in <paramref name="log"/>.</summary>
    public PutBackUnfinishedChanges(FileChanges changes, IScanLogSink log)
    {
        _changes = changes;
        _log = log;
    }

    /// <summary>Puts back each change cut short; a stop of the program part way leaves the rest for the next start.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var sentence in await _changes.RecoverAsync(cancellationToken)) _log.Log("audit", "Warning", sentence);
    }

    /// <summary>Nothing to stop: the putting back is done by the time the program has started.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
