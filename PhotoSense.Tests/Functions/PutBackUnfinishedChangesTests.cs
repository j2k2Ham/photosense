using Moq;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Functions.Changes;
using PhotoSense.Tests.Application;

namespace PhotoSense.Tests.Functions;

public sealed class PutBackUnfinishedChangesTests
{
    [Test]
    public async Task At_Start_The_Changes_A_Stop_Cut_Short_Are_Put_Back_And_Said_In_The_Log()
    {
        using var f = new ChangesFixture();
        f.Put("There/IMG_1.JPG");
        var batch = f.Change(moves: [(f.In("IMG_1.JPG"), f.In("There", "IMG_1.JPG"))]);
        batch.State = ChangeState.Started;
        await f.Batches.SaveAsync(batch);
        var log = new Mock<IScanLogSink>();
        var start = new PutBackUnfinishedChanges(f.Changes, log.Object);

        await start.StartAsync(CancellationToken.None);
        await start.StopAsync(CancellationToken.None);

        await Assert.That(File.Exists(f.In("IMG_1.JPG"))).IsTrue();
        log.Verify(l => l.Log("audit", "Warning", "Moving files to There was cut short when PhotoSense stopped. Every file is back where it was."), Times.Once);
    }
}
