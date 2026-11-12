using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Tests.Server;

/// <summary>Putting back, at start-up, the changes a stop cut short.</summary>
public sealed class PutBackUnfinishedChangesTests
{
    [Test]
    public async Task At_Start_The_Changes_A_Stop_Cut_Short_Are_Put_Back_And_Said_In_The_Log(CancellationToken ct)
    {
        using var f = new ServerFixture("--urls=http://127.0.0.1:0");
        var moved = f.In("There", "IMG_1.JPG");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        await File.WriteAllTextAsync(moved, "data", ct);
        // A move that a stop cut short after its file had gone.
        var batch = new OrganizeBatch { UtcTicks = TestClock.Day.UtcTicks, Label = "There", State = ChangeState.Started };
        f.Services.GetRequiredService<FileChanges>().NeedFolder(batch.CreatedFolders, f.In("There"));
        batch.Items.Add(new OrganizeBatchItem { From = f.In("IMG_1.JPG"), To = moved });
        await f.Services.GetRequiredService<IOrganizeBatchStore>().SaveAsync(batch, ct);

        await f.Program.StartAsync(ct);
        await f.Program.StopAsync(ct);

        await Assert.That(File.Exists(f.In("IMG_1.JPG"))).IsTrue();
        await Assert.That(f.Services.GetRequiredService<IScanLogSink>().TakePending(10).Select(l => (l.InstanceId, l.Level, l.Message)))
            .IsEquivalentTo([("audit", "Warning", "Moving files to There was cut short when PhotoSense stopped. Every file is back where it was.")], CollectionOrdering.Matching);
    }
}
