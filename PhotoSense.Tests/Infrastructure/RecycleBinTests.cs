using System.Runtime.InteropServices;
using PhotoSense.Desktop.Removal;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>The computer's Recycle Bin, or Trash: which folders have one, and sending files to it.</summary>
public sealed class RecycleBinTests
{
    // A Windows computer: its own disk, a USB stick and a network share, as Windows names them.
    private static readonly (string, DriveType)[] WindowsDrives = [(@"C:\", DriveType.Fixed), (@"E:\", DriveType.Removable), (@"\\nas\photos\", DriveType.Network)];
    // A Mac: its own disk, an external disk under /Volumes, and a share mounted there too.
    private static readonly (string, DriveType)[] MacDrives = [("/", DriveType.Fixed), ("/Volumes/Card", DriveType.Removable), ("/Volumes/NAS", DriveType.Network)];

    private static ComputerRecycleBin On(OSPlatform system, (string, DriveType)[] drives, List<string>? taken = null)
        => new(p => p == system, () => drives, paths => taken?.AddRange(paths));

    [Test]
    public async Task On_Windows_Only_A_Drive_Of_The_Computers_Own_Has_A_Recycle_Bin(CancellationToken ct)
    {
        var bin = On(OSPlatform.Windows, WindowsDrives);
        await Assert.That(bin.Name).IsEqualTo("Recycle Bin");
        await Assert.That((bin.Holds(@"C:\Users\jamie\Pictures\_PhotoSense_Removed"), bin.Holds(@"c:\photos"), bin.Holds(@"E:\DCIM\_PhotoSense_Removed"), bin.Holds(@"\\nas\photos\_PhotoSense_Removed")))
            .IsEqualTo((true, true, false, false));
        // A drive no longer there has none.
        await Assert.That(bin.Holds(@"F:\PC_backup\_PhotoSense_Removed")).IsFalse();
    }

    [Test]
    public async Task On_A_Mac_The_Computers_Own_Disk_And_An_External_One_Have_A_Trash_And_A_Share_Has_None(CancellationToken ct)
    {
        var bin = On(OSPlatform.OSX, MacDrives);
        await Assert.That(bin.Name).IsEqualTo("Trash");
        // The drive is the one whose start is the longest part of the path: /Volumes/NAS, not /.
        await Assert.That((bin.Holds("/Users/jamie/Pictures/_PhotoSense_Removed"), bin.Holds("/Volumes/Card/DCIM"), bin.Holds("/Volumes/NAS/_PhotoSense_Removed"), bin.Holds("/Volumes/Cards/x"), bin.Holds("/Volumes/Card")))
            .IsEqualTo((true, true, false, true, true));
    }

    [Test]
    public async Task Linux_Has_None_And_Sending_Hands_The_Files_To_The_System(CancellationToken ct)
    {
        await Assert.That(On(OSPlatform.Linux, MacDrives).Holds("/home/jamie")).IsFalse();
        var taken = new List<string>();
        On(OSPlatform.Windows, WindowsDrives, taken).Recycle([@"C:\a.jpg", @"C:\b.jpg"]);
        await Assert.That(taken).IsEquivalentTo([@"C:\a.jpg", @"C:\b.jpg"], CollectionOrdering.Matching);
        // The computer's own, as the program makes it: this one's temporary folder is on a drive of its own, with a bin
        // on Windows and a Mac.
        await Assert.That(new ComputerRecycleBin().Holds(Path.GetTempPath())).IsEqualTo(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }

    [Test]
    public async Task On_GitHubs_Windows_And_Mac_Machines_A_File_Really_Goes_To_The_Bin(CancellationToken ct)
    {
        // Only there: run here, it would leave a file in the person's own Recycle Bin each time.
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null || !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())) return;
        var file = Path.Combine(Path.GetTempPath(), $"photosense-bin-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "PhotoSense's check of the Recycle Bin.", ct);
        new ComputerRecycleBin().Recycle([file]);
        await Assert.That(File.Exists(file)).IsFalse();
    }
}
