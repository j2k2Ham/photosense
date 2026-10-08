using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning; // Added namespace for InMemoryScanProgressStore

namespace PhotoSense.Tests.Infrastructure.Scanning;

public class InMemoryScanProgressStoreTests
{
    [Test]
    public async Task Progress_Flow_Works()
    {
        IScanProgressStore store = new InMemoryScanProgressStore();
        store.ScanStarted("inst1");
        store.SetTotals("inst1", 10, 5);
        for (int i = 0; i < 3; i++) store.IncrementProcessed("inst1", true);
        for (int i = 0; i < 2; i++) store.IncrementProcessed("inst1", false);
        var snap = store.Get("inst1");
        await Assert.That(snap.PrimaryProcessed).IsEqualTo(3);
        await Assert.That(snap.SecondaryProcessed).IsEqualTo(2);
        store.ScanCompleted("inst1");
        var completed = store.Get("inst1");
        await Assert.That(completed.CompletedUtc).IsNotNull();
        await Assert.That(completed.PrimaryPercent > 0 && completed.PrimaryPercent < 100).IsTrue();
    }

    [Test]
    public async Task What_Is_Said_Of_A_Scan_That_Was_Never_Started_Is_Ignored()
    {
        IScanProgressStore store = new InMemoryScanProgressStore();
        store.SetTotals("never-started", 10, 5);
        store.IncrementProcessed("never-started", primary: true);
        store.ScanCompleted("never-started");

        var snap = store.Get("never-started");
        await Assert.That((snap.PrimaryTotal, snap.PrimaryProcessed, snap.SecondaryTotal, snap.CompletedUtc)).IsEqualTo((0, 0, 0, (DateTime?)null));
        await Assert.That(store.GetLatest().InstanceId).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Latest_Default_When_None()
    {
        IScanProgressStore store = new InMemoryScanProgressStore();
        var snap = store.GetLatest();
        await Assert.That(snap.InstanceId).IsEqualTo(string.Empty);
    }
}
