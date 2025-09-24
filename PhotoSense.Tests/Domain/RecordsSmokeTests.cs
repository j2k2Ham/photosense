using PhotoSense.Application.Scanning.Commands;
using PhotoSense.Application.Scanning.Events;
using PhotoSense.Application.Photos.Interfaces;
using Xunit;

namespace PhotoSense.Tests.Domain;

public class RecordsSmokeTests
{
    [Fact]
    public void StartScanCommand_Defaults()
    {
        var cmd = new StartScanCommand("p","s");
        Assert.True(cmd.Recursive);
    }

    [Fact]
    public void ScanCompletedEvent_Holds_Values()
    {
        var now = DateTime.UtcNow;
        var ev = new ScanCompletedEvent(Guid.NewGuid(), 1, 2, 3, now);
        Assert.Equal(3, ev.DuplicateGroupsFound);
    }

    [Fact]
    public void PagedResult_TotalPages_Computed()
    {
        var pr = new PagedResult<int>(new List<int>{1,2,3}, 1, 2, 3);
        Assert.Equal(2, pr.TotalPages);
    }
}
