using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning; // Added namespace for InMemoryScanProgressStore
using Xunit;

namespace PhotoSense.Tests.Infrastructure.Scanning;

public class InMemoryScanProgressStoreTests
{
    [Fact]
    public void Progress_Flow_Works()
    {
        IScanProgressStore store = new InMemoryScanProgressStore();
        store.ScanStarted("inst1");
        store.SetTotals("inst1", 10, 5);
        for (int i = 0; i < 3; i++) store.IncrementProcessed("inst1", true);
        for (int i = 0; i < 2; i++) store.IncrementProcessed("inst1", false);
        var snap = store.Get("inst1");
        Assert.Equal(3, snap.PrimaryProcessed);
        Assert.Equal(2, snap.SecondaryProcessed);
        store.ScanCompleted("inst1");
        var completed = store.Get("inst1");
        Assert.NotNull(completed.CompletedUtc);
        Assert.True(completed.PrimaryPercent > 0 && completed.PrimaryPercent < 100);
    }

    [Fact]
    public void Latest_Default_When_None()
    {
        IScanProgressStore store = new InMemoryScanProgressStore();
        var snap = store.GetLatest();
        Assert.Equal(string.Empty, snap.InstanceId);
    }
}
