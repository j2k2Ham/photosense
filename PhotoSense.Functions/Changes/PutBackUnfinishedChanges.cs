using Microsoft.Extensions.Hosting;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Functions.Changes;

/// <summary>At start-up, puts back the moves, copies and removals that a stop of the service cut short.</summary>
public sealed class PutBackUnfinishedChanges : IHostedService
{
    private readonly FileChanges _changes;
    private readonly IScanLogSink _log;

    public PutBackUnfinishedChanges(FileChanges changes, IScanLogSink log)
    {
        _changes = changes;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var sentence in await _changes.RecoverAsync(cancellationToken)) _log.Log("audit", "Warning", sentence);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
