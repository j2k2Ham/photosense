using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Transfer;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Library;
using PhotoSense.Infrastructure.Transfer;

namespace PhotoSense.Tests.Application;

/// <summary>
/// The home screen widgets on an iPhone, through its whole stack: how full the phone is, what Free up space can free,
/// how backing up stands, and today's set to swipe with the week and the streak, as the widgets are handed them.
/// </summary>
public sealed class WidgetsTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-widgets-");
    private readonly FakePhotoLibrary _library = new();
    private readonly FakeDevice _device = new();
    private readonly FakeWidgets _widgets = new();
    private readonly FakeComputerFinder _computers = new();
    private readonly ServiceProvider _phone;

    public WidgetsTests()
    {
        _phone = new ServiceCollection()
            .AddPhotoSensePhone(_library, new FakePhotoAccess(), new MagickImageAnalyzer(), In("Documents"), In("Caches"), new FakeAdvertiser(), new FakeAppStore(), new FakeCodeScanner(),
                _computers, _device, new FakeNotifier(), new FakeShareSheet(), new FakeVaultKeys(), new FakeUnlocker(), _widgets, "Jamie's iPhone", In("Photo Library"), new TcpTransferListener(0))
            .AddPhotoSenseApi()
            .AddSingleton(new ProgramInfo("iphone", "1.2.0.57", () => "notices"))
            .AddStandInStores()
            .AddSingleton<TimeProvider>(new TestClock())
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _phone.GetRequiredService<PhoneBackup>().Pause();
        _phone.Dispose();
        TestFiles.Remove(_data);
    }

    private string In(string folder) => Path.Combine(_data.FullName, folder);

    private PhoneWidgets Widgets => _phone.GetRequiredService<PhoneWidgets>();

    private Task PairedAsync(CancellationToken ct)
        => _phone.GetRequiredService<IBackupLedger>().SaveComputerAsync(new BackupComputer("computer-1", "Studio iMac", "Pictures/PhotoSense", null, null), ct);

    [Test]
    public async Task Before_A_Computer_Or_A_Swipe_The_Widgets_Say_How_Full_The_Phone_Is_And_What_To_Do_First(CancellationToken ct)
    {
        _library.Add("A", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture()));
        _library.Add("B", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture("B")));
        await Widgets.RefreshAsync(ct);
        var shown = _widgets.Shown!;
        await Assert.That((shown.PercentFull, shown.Freeable, shown.BackupLine, shown.BackupButton)).IsEqualTo((88, "0 KB", "Pair your computer to back up", "Pair"));
        await Assert.That((shown.Ready, shown.ReadyLine, shown.StreakLine)).IsEqualTo((2, "photos ready today", "Swipe today to start a streak"));
        await Assert.That(shown.Week).IsEquivalentTo([false, false, false, false, false, false, false], CollectionOrdering.Matching);

        // A screenshot the scan found: Free up space can free it.
        var path = _phone.GetRequiredService<IPhotoFileSystem>().FilesIn(In("Photo Library")).First();
        var stored = _phone.GetRequiredService<IPhotoFileSystem>().Describe(path)!;
        await _phone.GetRequiredService<IPhotoRepository>().AddOrUpdateAsync(new PhotoSense.Domain.Entities.Photo
        {
            SourcePath = path, FileName = Path.GetFileName(path), FileSizeBytes = 3 * 1024 * 1024, FileModifiedUtc = stored.ModifiedUtc, IsScreenshot = true,
        }, ct);
        await Assert.That((await Widgets.SnapshotAsync(ct)).Freeable).IsEqualTo("3.0 MB");

        // A phone that says nothing of its storage is said to be empty.
        _device.Storage = new DeviceStorage(0, 0);
        await Assert.That((await Widgets.SnapshotAsync(ct)).PercentFull).IsEqualTo(0);
    }

    [Test]
    public async Task Swiping_Counts_Down_Todays_Set_Fills_Todays_Dot_And_Starts_A_Streak(CancellationToken ct)
    {
        _library.Add("A", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture()));
        _library.Add("B", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture("B")));
        var swipe = _phone.GetRequiredService<SwipeToClean>();
        var set = (await swipe.StateAsync(ct)).Set;
        await swipe.DecideAsync(set[0].Id, delete: false, ct);
        var one = await Widgets.SnapshotAsync(ct);
        await Assert.That((one.Ready, one.ReadyLine, one.StreakLine, one.Week[^1])).IsEqualTo((1, "photo ready today", "1-day streak", true));
        await swipe.DecideAsync(set[1].Id, delete: false, ct);
        var done = await Widgets.SnapshotAsync(ct);
        await Assert.That((done.Ready, done.ReadyLine)).IsEqualTo((0, "Done for today"));
    }

    [Test]
    public async Task A_Library_With_Nothing_To_Swipe_Says_Every_Photo_Was_Swiped(CancellationToken ct)
    {
        var shown = await Widgets.SnapshotAsync(ct);
        await Assert.That((shown.Ready, shown.ReadyLine)).IsEqualTo((0, "Every photo swiped"));
    }

    [Test]
    public async Task Once_Paired_The_Storage_Widget_Says_What_Waits_Or_That_All_Is_On_The_Computer_Or_That_A_Backup_Runs(CancellationToken ct)
    {
        await PairedAsync(ct);
        var empty = await Widgets.SnapshotAsync(ct);
        await Assert.That((empty.BackupLine, empty.BackupButton)).IsEqualTo(("All on Studio iMac", "Back up now"));
        _library.Add("A", (LibraryFileKind.Photo, "IMG_0001.JPG", MediaSamples.Picture()));
        await Assert.That((await Widgets.SnapshotAsync(ct)).BackupLine).IsEqualTo("1 item waiting");
        _library.Add("B", (LibraryFileKind.Photo, "IMG_0002.JPG", MediaSamples.Picture("B")));
        await Assert.That((await Widgets.SnapshotAsync(ct)).BackupLine).IsEqualTo("2 items waiting");

        // A computer that has not answered yet: the backup runs meanwhile.
        var answer = new TaskCompletionSource<Stream>();
        _computers.Computers.Add(new FoundComputer("computer-1", "Studio iMac", _ => answer.Task));
        var pairs = _phone.GetRequiredService<IPairingStore>();
        await pairs.SaveAsync(new PairedDevice("computer-1", "Studio iMac", new byte[32]), ct);
        var backup = _phone.GetRequiredService<PhoneBackup>();
        await backup.StartAsync(null, ct);
        await Assert.That((await Widgets.SnapshotAsync(ct)).BackupLine).IsEqualTo("Backing up to Studio iMac…");
        backup.Pause();
        answer.SetCanceled(ct);
        await backup.Running;
    }
}
