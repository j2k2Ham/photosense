using PhotoSense.Application.Scanning.Events;
using PhotoSense.Application.Scanning.Interfaces;
using System.Collections.Concurrent;

namespace PhotoSense.Application.Scanning.Services;

// Simple in-memory queue publisher abstraction for now
public class ScanRequestPublisher : IScanRequestPublisher
{
    private static readonly ConcurrentQueue<ScanRequestedEvent> _queue = new();
    public Task PublishAsync(ScanRequestedEvent evt, CancellationToken ct = default)
    {
        _queue.Enqueue(evt);
        return Task.CompletedTask;
    }

    public static bool TryDequeue(out ScanRequestedEvent evt) => _queue.TryDequeue(out evt);
}
