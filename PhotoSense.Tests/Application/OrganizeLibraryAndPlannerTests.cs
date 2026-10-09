using Moq;
using PhotoSense.Application.Organizing;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests.Application;

public sealed class OrganizeLibraryTests : IDisposable
{
    private readonly OrganizeFixture _f = new();
    public void Dispose() => _f.Dispose();

    private static readonly DateTime Taken = new(2023, 6, 9, 14, 3, 22);

    [Test]
    public async Task Lists_Every_Picture_And_Video_Below_A_Folder_The_Latest_First()
    {
        _f.Put("IMG_1.JPG", year: 2021);
        _f.Put("2022/IMG_2.HEIC", year: 2022);
        _f.Put("2022/clip.MOV", year: 2019);
        _f.Put("notes.txt");
        _f.Put("_PhotoSense_Removed/IMG_9.JPG");
        _f.Reader.Says["clip.MOV"] = new MediaDetails(Taken, 1920, 1080, 84, 35.2886, -75.5161);

        var files = await _f.Library.ListAsync(_f.Root.FullName);

        await Assert.That(files.Select(f => f.Name)).IsEquivalentTo(new[] { "clip.MOV", "IMG_2.HEIC", "IMG_1.JPG" }, CollectionOrdering.Matching);
        var clip = files[0];
        await Assert.That((clip.Folder, clip.IsVideo, clip.DateFromFile, clip.Date, clip.SizeBytes, clip.FromScan, clip.ContentHash, clip.Details.Width, clip.Details.DurationSeconds))
            .IsEqualTo((_f.In("2022"), true, false, Taken, 4L, false, null, 1920, 84.0));
        // Nothing in the picture says when it was taken: the file's own date stands in.
        await Assert.That((files[2].IsVideo, files[2].DateFromFile, files[2].Date)).IsEqualTo((false, true, new DateTime(2021, 1, 1, 12, 0, 0)));
    }

    [Test]
    public async Task A_Folder_That_Is_Not_There_Is_Said_To_Be_Missing()
    {
        foreach (var root in new[] { "", "  ", _f.In("nowhere") })
            await Assert.That(async () => await _f.Library.ListAsync(root)).ThrowsExactly<DirectoryNotFoundException>();
    }

    [Test]
    public async Task Reads_A_File_Once_For_As_Long_As_It_Stays_The_Same()
    {
        var path = _f.Put("IMG_1.JPG");
        _f.Put("IMG_2.JPG");
        await _f.Library.ListAsync(_f.Root.FullName);
        await _f.Library.ListAsync(_f.Root.FullName);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(2);

        // Changed since: read again, and only it.
        File.WriteAllText(path, "more data");
        await _f.Library.ListAsync(_f.Root.FullName);
        await Assert.That(_f.Reader.Reads.Count(r => r == "IMG_1.JPG")).IsEqualTo(2);
        await Assert.That(_f.Reader.Reads.Count(r => r == "IMG_2.JPG")).IsEqualTo(1);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        await _f.Library.ListAsync(_f.Root.FullName);
        await Assert.That(_f.Reader.Reads.Count(r => r == "IMG_1.JPG")).IsEqualTo(3);
    }

    [Test]
    public async Task Uses_What_A_Scan_Recorded_Instead_Of_Reading_The_File()
    {
        var scanned = _f.Put("IMG_1.JPG");
        var changed = _f.Put("IMG_2.JPG");
        var touched = _f.Put("IMG_3.JPG");
        await _f.ScannedAsync(scanned, Taken);
        await _f.ScannedAsync(changed, Taken, size: 999);
        await _f.ScannedAsync(touched, Taken);
        File.SetLastWriteTimeUtc(touched, File.GetLastWriteTimeUtc(touched).AddMinutes(1));
        // A record from before scans noted when a file was last changed says nothing about whether it has.
        var undated = _f.Put("IMG_4.JPG");
        await _f.Repo.AddOrUpdateAsync(new PhotoSense.Domain.Entities.Photo { SourcePath = undated, FileName = "IMG_4.JPG", FileSizeBytes = new FileInfo(undated).Length });

        var files = (await _f.Library.ListAsync(_f.Root.FullName)).ToDictionary(f => f.Name);

        var known = files["IMG_1.JPG"];
        await Assert.That((known.FromScan, known.ContentHash, known.Date, known.DateFromFile, known.Details.Width, known.Details.Height, known.Details.Latitude, known.Details.Longitude))
            .IsEqualTo((true, "ABCDEF", Taken, false, 4032, 3024, 46.1283, -112.9423));
        // A record of the file as it no longer is does not hold.
        await Assert.That((files["IMG_2.JPG"].FromScan, files["IMG_3.JPG"].FromScan, files["IMG_4.JPG"].FromScan)).IsEqualTo((false, false, false));
        await Assert.That(_f.Reader.Reads.Order()).IsEquivalentTo(new[] { "IMG_2.JPG", "IMG_3.JPG", "IMG_4.JPG" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Reads_A_Folder_Once_However_Often_It_Is_Asked_For_Meanwhile_And_Says_How_Far_It_Has_Got()
    {
        for (var i = 1; i <= 6; i++) _f.Put($"IMG_{i}.JPG");
        var root = _f.Root.FullName;
        using var hold = new ManualResetEventSlim();
        using var begun = new SemaphoreSlim(0);
        _f.Reader.OnRead = _ => { begun.Release(); hold.Wait(); };
        await Assert.That(_f.Library.ProgressOf(root)).IsNull();

        var first = _f.Library.ListAsync(root);
        // Asked for again, with its name spelled another way, while the first reading is still going.
        var second = _f.Library.ListAsync(root + Path.DirectorySeparatorChar);
        await begun.WaitAsync();
        var early = _f.Library.ProgressOf(root + Path.DirectorySeparatorChar)!;
        await Assert.That((early.Total, early.Done, early.From, early.Found.Count, early.More)).IsEqualTo((6, 0, 0, 0, false));
        hold.Set();

        await Assert.That(ReferenceEquals(await first, await second)).IsTrue();
        await Assert.That(((await first).Count, _f.Reader.Reads.Count)).IsEqualTo((6, 6));
        // Once it is done there is nothing to report, and the next asking is a new reading (of files already known).
        await Assert.That(_f.Library.ProgressOf(root)).IsNull();
        await Assert.That(ReferenceEquals(await _f.Library.ListAsync(root), await first)).IsFalse();
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(6);
    }

    [Test]
    public async Task Hands_Over_The_Files_Found_So_Far_While_A_Folder_Is_Still_Being_Read()
    {
        for (var i = 1; i <= 6; i++) _f.Put($"IMG_{i}.JPG");
        var root = _f.Root.FullName;
        // Five of the six are read; the reading then waits on the last.
        using var hold = new ManualResetEventSlim();
        _f.Reader.OnRead = path => { if (path.EndsWith("IMG_6.JPG", StringComparison.Ordinal)) hold.Wait(); };
        var listing = _f.Library.ListAsync(root);
        for (var tries = 0; (_f.Library.ProgressOf(root)?.Done ?? 0) < 5 && tries < 500; tries++) await Task.Delay(10);

        var all = _f.Library.ProgressOf(root)!;
        await Assert.That((all.Total, all.Done, all.From, all.Found.Count, all.More)).IsEqualTo((6, 5, 0, 5, false));
        await Assert.That(all.Found.Select(f => f.Name).Order()).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG", "IMG_3.JPG", "IMG_4.JPG", "IMG_5.JPG" }, CollectionOrdering.Matching);

        // Asked for from where the asker has got to, a few at a time, in the order they were found.
        var some = _f.Library.ProgressOf(root, from: 2, atMost: 2)!;
        await Assert.That((some.From, some.More)).IsEqualTo((2, true));
        await Assert.That(some.Found.Select(f => f.Id)).IsEquivalentTo(all.Found.Skip(2).Take(2).Select(f => f.Id), CollectionOrdering.Matching);
        var rest = _f.Library.ProgressOf(root, from: 4, atMost: 10)!;
        await Assert.That((rest.From, rest.Found.Count, rest.More)).IsEqualTo((4, 1, false));
        // Asked for from beyond what there is, from before the beginning, or for none at all.
        var beyond = _f.Library.ProgressOf(root, from: 99)!;
        await Assert.That((beyond.From, beyond.Found.Count, beyond.More)).IsEqualTo((5, 0, false));
        var before = _f.Library.ProgressOf(root, from: -3, atMost: -1)!;
        await Assert.That((before.From, before.Found.Count, before.More)).IsEqualTo((0, 0, true));

        // It is one reading throughout, with one place to keep notes about it; the next reading is another.
        await Assert.That((some.Reading, ReferenceEquals(some.Notes, all.Notes))).IsEqualTo((all.Reading, true));
        hold.Set();
        await Assert.That((await listing).Count).IsEqualTo(6);
        _f.Reader.OnRead = null;
        File.WriteAllText(_f.In("IMG_1.JPG"), "changed, so that there is something to read");
        Guid? next = null;
        _f.Reader.OnRead = _ => next = _f.Library.ProgressOf(root)!.Reading;
        await _f.Library.ListAsync(root);
        await Assert.That(next).IsNotNull();
        await Assert.That(next!.Value).IsNotEqualTo(all.Reading);
    }

    [Test]
    public async Task Leaves_Out_A_File_That_Went_Away_While_The_Folder_Was_Being_Read()
    {
        for (var i = 0; i < 300; i++) _f.Put($"IMG_{i:000}.JPG");
        // The first file to be read takes most of the others with it.
        var once = 0;
        _f.Reader.OnRead = _ =>
        {
            if (Interlocked.Exchange(ref once, 1) == 0) foreach (var path in Directory.GetFiles(_f.Root.FullName).Skip(50)) File.Delete(path);
        };

        var files = await _f.Library.ListAsync(_f.Root.FullName);

        await Assert.That(files.Count).IsGreaterThanOrEqualTo(50).And.IsLessThan(300);
        await Assert.That(_f.Library.ProgressOf(_f.Root.FullName)).IsNull();
    }

    [Test]
    public async Task What_Was_Read_From_A_File_Is_Kept_For_The_Next_Run_Of_The_Service()
    {
        var path = _f.Put("IMG_1.JPG");
        _f.Reader.Says["IMG_1.JPG"] = new MediaDetails(Taken, 64, 48, null, 46.1283, -112.9423);
        await _f.Library.ListAsync(_f.Root.FullName);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(1);

        // Another run knows nothing in memory, and still does not read the file again: listing it, or asked about it alone.
        var listed = (await _f.AnotherRun().ListAsync(_f.Root.FullName)).Single();
        await Assert.That((listed.Date, listed.FromScan, listed.Details.Width, listed.Details.Latitude)).IsEqualTo((Taken, false, 64, 46.1283));
        await Assert.That((await _f.AnotherRun().DescribeAsync(path))!.Date).IsEqualTo(Taken);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(1);

        // Changed since, in size or only in when it was last written: what was kept no longer holds.
        File.WriteAllText(path, "more data");
        await _f.AnotherRun().ListAsync(_f.Root.FullName);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(2);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        await _f.AnotherRun().ListAsync(_f.Root.FullName);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(3);
        await _f.AnotherRun().ListAsync(_f.Root.FullName);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(3);

        // A file asked about on its own is kept as well, and so is one that was moved.
        var alone = _f.Put("there/IMG_2.JPG");
        await _f.Library.DescribeAsync(alone);
        await _f.AnotherRun().DescribeAsync(alone);
        var moved = _f.In("there", "IMG_2 moved.JPG");
        File.Move(alone, moved);
        _f.Library.Arrived(alone, moved, copied: false);
        await _f.AnotherRun().DescribeAsync(moved);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(4);
    }

    [Test]
    public async Task A_File_Is_Known_By_An_Id_Only_This_Library_Can_Turn_Back_Into_Its_Path()
    {
        var one = _f.Put("IMG_1.JPG");
        var two = _f.Put("IMG_2.JPG");
        var id = await _f.IdAsync(one);

        await Assert.That(id.Length).IsEqualTo(24);
        await Assert.That(await _f.IdAsync(one)).IsEqualTo(id);
        await Assert.That(await _f.IdAsync(two)).IsNotEqualTo(id);
        await Assert.That((await _f.Library.FindAsync(id))!.Path).IsEqualTo(one);
        await Assert.That(_f.Library.PathOf(id)).IsEqualTo(one);
        await Assert.That(await _f.Library.FindAsync("0123456789abcdef01234567")).IsNull();
        await Assert.That(_f.Library.PathOf("0123456789abcdef01234567")).IsNull();

        // Another run of the service gives the same file another id; one given the same key, the same id.
        byte[] key = [1, 2, 3];
        var first = new OrganizeLibrary(_f.Repo, _f.Reader, _f.Hasher, _f.Details, key);
        var again = new OrganizeLibrary(_f.Repo, _f.Reader, _f.Hasher, _f.Details, key);
        await Assert.That((await first.DescribeAsync(one))!.Id).IsEqualTo((await again.DescribeAsync(one))!.Id);
        await Assert.That((await first.DescribeAsync(one))!.Id).IsNotEqualTo(id);

        // Once the file has gone, the id still says where it was, and finds nothing.
        File.Delete(one);
        await Assert.That(await _f.Library.FindAsync(id)).IsNull();
        await Assert.That(_f.Library.PathOf(id)).IsEqualTo(one);
        await Assert.That(await _f.Library.DescribeAsync(_f.In("never.JPG"))).IsNull();
    }

    [Test]
    public async Task Hashes_A_File_Once_And_Not_At_All_When_A_Scan_Already_Did()
    {
        var plain = _f.Put("IMG_1.JPG", "the bytes");
        var scanned = _f.Put("IMG_2.JPG");
        await _f.ScannedAsync(scanned, contentHash: "FROMSCAN");

        var file = (await _f.Library.DescribeAsync(plain))!;
        var hash = await _f.Library.HashAsync(file);
        await Assert.That(hash).IsEqualTo(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("the bytes"u8.ToArray())));
        // Asked again for the file as the library now knows it, the hash is at hand.
        await Assert.That(await _f.Library.HashAsync((await _f.Library.DescribeAsync(plain))!)).IsEqualTo(hash);
        await Assert.That(await _f.Library.HashAsync((await _f.Library.DescribeAsync(scanned))!)).IsEqualTo("FROMSCAN");
        // So it is for the file as it was handed out before the hash was worked out.
        await Assert.That(await _f.Library.HashAsync(file)).IsEqualTo(hash);
        await Assert.That(_f.Hasher.Calls).IsEqualTo(1);

        // A hash worked out for the file as it then was does not stand for it as it is said to be now.
        await Assert.That(await _f.Library.HashAsync(file with { SizeBytes = 999 })).IsEqualTo(hash);
        await Assert.That(await _f.Library.HashAsync(file with { ModifiedUtc = file.ModifiedUtc.AddMinutes(1) })).IsEqualTo(hash);
        await Assert.That(_f.Hasher.Calls).IsEqualTo(3);
        // Nor is one at hand for a file the library was told has gone elsewhere.
        _f.Library.Arrived(plain, _f.In("elsewhere.JPG"), copied: false);
        await Assert.That(await _f.Library.HashAsync(file)).IsEqualTo(hash);
        await Assert.That(_f.Hasher.Calls).IsEqualTo(4);
    }

    [Test]
    public async Task What_Is_Known_About_A_File_Goes_With_It_When_It_Is_Moved_Or_Copied()
    {
        var from = _f.Put("IMG_1.JPG");
        _f.Reader.Says["IMG_1.JPG"] = new MediaDetails(Taken, 64, 48, null, null, null);
        await _f.Library.DescribeAsync(from);
        Directory.CreateDirectory(_f.In("there"));

        var moved = _f.In("there", "IMG_1 (1).JPG");
        File.Move(from, moved);
        _f.Library.Arrived(from, moved, copied: false);
        var arrived = (await _f.Library.DescribeAsync(moved))!;
        await Assert.That((arrived.Path, arrived.Date, arrived.Details.Width)).IsEqualTo((moved, Taken, 64));
        await Assert.That(_f.Library.PathOf(arrived.Id)).IsEqualTo(moved);

        var copy = _f.In("there", "copy.JPG");
        File.Copy(moved, copy);
        _f.Library.Arrived(moved, copy, copied: true);
        await Assert.That((await _f.Library.DescribeAsync(copy))!.Date).IsEqualTo(Taken);
        await Assert.That((await _f.Library.DescribeAsync(moved))!.Date).IsEqualTo(Taken);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(1);

        // A file the library had not looked at yet is simply given its id, and read when it is asked about.
        var unseen = _f.Put("there/unseen.JPG");
        var later = _f.In("there", "later.JPG");
        File.Move(unseen, later);
        _f.Library.Arrived(unseen, later, copied: false);
        await Assert.That((await _f.Library.DescribeAsync(later))!.DateFromFile).IsTrue();
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Knows_The_Live_Photo_Identifier_Of_Every_File_It_Has_Listed()
    {
        var picture = _f.Put("IMG_1234.HEIC");
        var video = _f.Put("IMG_1234.MOV");
        var plain = _f.Put("IMG_5.JPG");
        _f.Reader.Says["IMG_1234.HEIC"] = new MediaDetails(Taken, 0, 0, null, null, null, "LIVE-1");
        var scanned = await _f.ScannedAsync(video);
        scanned.LivePhotoId = "LIVE-1";
        await _f.Library.ListAsync(_f.Root.FullName);

        var asked = new List<string>();
        var ids = _f.Library.LivePhotoIds(path => { asked.Add(Path.GetFileName(path)); return "from the file"; });
        // Read with the other details, or taken from the scan's record; a file that has none is known to have none.
        await Assert.That((ids(picture), ids(video), ids(plain))).IsEqualTo(("LIVE-1", "LIVE-1", null));
        await Assert.That(asked).IsEmpty();

        // A file the library has not seen, one that has changed since it was, and one that is not there are asked about.
        var unseen = _f.Put("there/IMG_9.HEIC");
        File.WriteAllText(picture, "changed since");
        await Assert.That((ids(unseen), ids(picture), ids(_f.In("gone.HEIC")))).IsEqualTo(("from the file", "from the file", "from the file"));
        await Assert.That(asked).IsEquivalentTo(new[] { "IMG_9.HEIC", "IMG_1234.HEIC", "gone.HEIC" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Position_Is_Looked_Up_Once_And_None_Is_Not_Looked_Up_At_All()
    {
        var butte = new PlaceMatch("Butte", "Montana", "US", 46.0038, -112.5348, "Uptown Butte", 46.0138, -112.5362);
        _f.Resolver.Setup(r => r.Locate(It.IsAny<double>(), It.IsAny<double>())).Returns(butte);
        _f.Resolver.Setup(r => r.Describe(46.0, -112.5)).Returns("Butte, Montana, US");

        await Assert.That(_f.Places.At(46.01381, -112.53621)).IsEqualTo(butte);
        await Assert.That(_f.Places.At(46.01384, -112.53618)).IsEqualTo(butte);
        await Assert.That(_f.Places.At(null, -112.5)).IsNull();
        await Assert.That(_f.Places.At(46.0, null)).IsNull();
        _f.Resolver.Verify(r => r.Locate(46.0138, -112.5362), Times.Once);
        _f.Resolver.Verify(r => r.Locate(It.IsAny<double>(), It.IsAny<double>()), Times.Once);

        await Assert.That(_f.Places.Describe(46.0, -112.5)).IsEqualTo("Butte, Montana, US");
        await Assert.That(_f.Places.Describe(null, -112.5)).IsNull();
        await Assert.That(_f.Places.Describe(46.0, null)).IsNull();
    }
}

public sealed class OrganizePlannerTests : IDisposable
{
    private readonly OrganizeFixture _f = new();
    public void Dispose() => _f.Dispose();

    private OrganizeDestination To(string? name, bool direct = false, bool yearSplit = false, string? basePath = null) => new(basePath ?? _f.Root.FullName, name, direct, yearSplit);

    private async Task<List<string>> IdsAsync(params string[] paths)
    {
        var ids = new List<string>();
        foreach (var path in paths) ids.Add(await _f.IdAsync(path));
        return ids;
    }

    [Test]
    public async Task A_Destination_Is_A_New_Folder_Inside_Another_Or_That_Folder_Itself()
    {
        await Assert.That(To("Anaconda").Folder).IsEqualTo(_f.In("Anaconda"));
        await Assert.That(To("Trips\\Glacier 2022").Folder).IsEqualTo(_f.In("Trips", "Glacier 2022"));
        // Stray separators and spaces, and the dots and spaces Windows drops from the end of a name, are taken off.
        await Assert.That(To(" /Trips/ Glacier 2022. \\").Folder).IsEqualTo(_f.In("Trips", "Glacier 2022"));
        await Assert.That(To("ignored", direct: true).Folder).IsEqualTo(_f.Root.FullName);
        // Names of dots alone are dropped, so a name cannot lead out of the folder it is made in.
        await Assert.That(To("Trips\\..\\..\\Windows").Folder).IsEqualTo(_f.In("Trips", "Windows"));
        await Assert.That(To(null, direct: true, yearSplit: true).YearSplit).IsTrue();
    }

    [Test]
    public async Task A_Destination_That_Cannot_Be_Used_Says_Why()
    {
        foreach (var basePath in new[] { null, " ", "photos" })
            await Assert.That(Assert.ThrowsExactly<ArgumentException>(() => new OrganizeDestination(basePath, "Trips", false, false)).Message).IsEqualTo("Choose where the files go.");
        await Assert.That(Assert.ThrowsExactly<DirectoryNotFoundException>(() => To("Trips", basePath: _f.In("nowhere"))).Message).IsEqualTo($"Folder not found: {_f.In("nowhere")}");
        foreach (var name in new[] { null, "", "  ", "\\/", " . " })
            await Assert.That(Assert.ThrowsExactly<ArgumentException>(() => To(name)).Message).IsEqualTo("Give the folder a name first.");
        foreach (var name in new[] { "What?", "a<b", "tab\there", "a:b", "a|b", "a*b", "a\"b", "a>b" })
            await Assert.That(Assert.ThrowsExactly<ArgumentException>(() => To(name)).Message).IsEqualTo("Folder names cannot contain < > : \" / | ? or *");
    }

    [Test]
    public async Task Names_With_A_Number_Are_Found_And_Recognised()
    {
        var taken = new HashSet<string> { "IMG_1 (1).JPG", "IMG_1 (2).JPG" };
        await Assert.That(NameRules.NextFree("IMG_1.JPG", taken.Contains)).IsEqualTo("IMG_1 (3).JPG");
        await Assert.That(NameRules.NextFree("README", _ => false)).IsEqualTo("README (1)");

        var strict = NameRules.Family("IMG_1.JPG", ignoreCase: false);
        await Assert.That(new[] { "IMG_1.JPG", "IMG_1 (1).JPG", "IMG_1 (12).JPG" }.All(strict.IsMatch)).IsTrue();
        await Assert.That(new[] { "img_1.jpg", "IMG_1 (a).JPG", "IMG_11.JPG", "IMG_1.JPG.bak", "xIMG_1.JPG", "IMG_1  (1).JPG" }.Any(strict.IsMatch)).IsFalse();
        await Assert.That(NameRules.Family("IMG_1.JPG", ignoreCase: true).IsMatch("img_1 (1).jpg")).IsTrue();
        // Characters that mean something in a pattern are taken as they are.
        await Assert.That(NameRules.Family("a+b (x).JPG").IsMatch("a+b (x) (1).JPG")).IsTrue();
        await Assert.That(NameRules.Family("a+b (x).JPG").IsMatch("aab (x).JPG")).IsFalse();
    }

    [Test]
    public async Task A_Name_Asked_For_Must_Be_A_Plain_Name_With_The_Files_Own_Extension()
    {
        foreach (var good in new[] { "IMG_6435 2023-06.JPG", "new.jpg", "a.b.JPG" }) await Assert.That(NameRules.Usable(good, "IMG_6435.JPG")).IsTrue();
        foreach (var bad in new[] { null, "", "  ", " new.JPG", "new.JPG ", "new.PNG", "new", ".JPG", "a/b.JPG", "a\\b.JPG", "what?.JPG", "a:b.JPG", "a*b.JPG", "a|b.JPG", "a<b.JPG", "a>b.JPG", "a\"b.JPG", "new.JPG." })
            await Assert.That(NameRules.Usable(bad, "IMG_6435.JPG")).IsFalse();
        await Assert.That(NameRules.Usable("notes", "README")).IsTrue();
    }

    [Test]
    public async Task Only_A_File_Whose_Item_Has_Other_Files_Can_Have_Companions()
    {
        var picture = _f.Put("IMG_1234.HEIC");
        var video = _f.Put("IMG_1234.MOV");
        var sidecar = _f.Put("IMG_O1234.AAE");
        var alone = _f.Put("IMG_5.JPG");
        var index = new SiblingIndex();
        await Assert.That(index.SiblingsOf(picture).Order()).IsEquivalentTo(new[] { video, sidecar }.Order(), CollectionOrdering.Matching);
        await Assert.That(index.SiblingsOf(alone).Count + index.SiblingsOf(_f.In("gone", "IMG_1.JPG")).Count + index.SiblingsOf("IMG_1.JPG").Count).IsEqualTo(0);
        // The folder is looked through once; a file that has since left it is no longer counted.
        _f.Put("IMG_5.AAE");
        await Assert.That(index.SiblingsOf(alone)).IsEmpty();
        File.Delete(video);
        await Assert.That(index.SiblingsOf(picture, stillThere: true)).IsEquivalentTo(new[] { sidecar }, CollectionOrdering.Matching);
        await Assert.That(index.SiblingsOf(picture).Count).IsEqualTo(2);
    }

    [Test]
    public async Task Plans_Where_Each_File_Goes_Without_Moving_Anything()
    {
        var a = _f.Put("IMG_1.JPG", year: 2022);
        var b = _f.Put("Camera Roll/IMG_2.JPG", year: 2023);
        var settled = _f.Put("Anaconda/2021/IMG_3.JPG", year: 2021);
        _f.Reader.Says["IMG_1.JPG"] = new MediaDetails(new DateTime(2021, 12, 31, 23, 59, 0), 0, 0, null, null, null);
        var ids = await IdsAsync(a, b, settled);

        var plan = await _f.Planner.PlanAsync(To("Anaconda", yearSplit: true), [ids[0], "unknown", ids[1], ids[2]]);

        await Assert.That((plan.Destination, plan.Exists, plan.Clashes.Count, plan.Companions)).IsEqualTo((_f.In("Anaconda"), true, 0, 0));
        // The year is the one it was taken in; a file already in its folder has nowhere to go.
        await Assert.That(plan.Items.Select(i => (i.File.Id, i.Folder))).IsEquivalentTo(new[] { (ids[0], _f.In("Anaconda", "2021")), (ids[1], _f.In("Anaconda", "2023")) }, CollectionOrdering.Matching);
        await Assert.That(plan.Taken.Keys).IsEquivalentTo(new[] { _f.In("Anaconda", "2021") }, CollectionOrdering.Matching);
        await Assert.That(plan.Taken[_f.In("Anaconda", "2021")]).IsEquivalentTo(new[] { "IMG_3.JPG" }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(a) && File.Exists(b)).IsTrue();

        var flat = await _f.Planner.PlanAsync(To("New place"), ids);
        await Assert.That((flat.Exists, flat.Items.Count, flat.Taken.Count, flat.Items[2].Folder)).IsEqualTo((false, 3, 0, _f.In("New place")));
    }

    [Test]
    public async Task A_File_Goes_To_The_Subfolder_Asked_For_It_And_Otherwise_Where_The_Destination_Says()
    {
        var a = _f.Put("IMG_1.JPG", year: 2022);
        var b = _f.Put("IMG_2.JPG", year: 2023);
        var c = _f.Put("IMG_3.JPG", year: 2021);
        var d = _f.Put("2022/Myrtle Beach/IMG_4.JPG", year: 2022);
        var ids = await IdsAsync(a, b, c, d);

        // One by the month, one by the place inside a year, one left to the destination, one already where it would go.
        var plan = await _f.Planner.PlanAsync(To("By year", yearSplit: true), ids, ["2022-07", " 2023 / Myrtle Beach. ", " "]);
        await Assert.That(plan.Items.Select(i => (i.File.Id, i.Folder))).IsEquivalentTo(
            new[] { (ids[0], _f.In("By year", "2022-07")), (ids[1], _f.In("By year", "2023", "Myrtle Beach")), (ids[2], _f.In("By year", "2021")), (ids[3], _f.In("By year", "2022")) }, CollectionOrdering.Matching);
        var settled = await _f.Planner.PlanAsync(To(null, direct: true), [ids[3]], ["2022\\Myrtle Beach"]);
        await Assert.That(settled.Items).IsEmpty();

        await Assert.That(OrganizeDestination.Subfolder(null)).IsNull();
        await Assert.That(OrganizeDestination.Subfolder(" \\ / .. ")).IsNull();
        await Assert.That(OrganizeDestination.Subfolder("2022/Hurl Rocks")).IsEqualTo(Path.Combine("2022", "Hurl Rocks"));
        await Assert.That(() => OrganizeDestination.Subfolder("What?")).Throws<ArgumentException>().WithMessage("Folder names cannot contain < > : \" / | ? or *");
        var file = (await _f.Library.FindAsync(ids[0]))!;
        await Assert.That(() => To("x").FolderFor(file, "a|b")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Says_Which_Names_Are_Taken_And_By_What()
    {
        var moving = _f.Put("IMG_6435.JPG", "the same bytes");
        var other = _f.Put("MOV_8203.MP4", "a video");
        var free = _f.Put("IMG_7.JPG");
        _f.Put("Anaconda/IMG_6435.JPG", "the same bytes");
        _f.Put("Anaconda/IMG_6435 (1).JPG", "same size, not");
        _f.Put("Anaconda/MOV_8203.MP4", "a longer video");
        _f.Put("Anaconda/IMG_64350.JPG");
        _f.Reader.Says["IMG_6435 (1).JPG"] = new MediaDetails(null, 3024, 4032, null, 46.1283, -112.9423);
        _f.Resolver.Setup(r => r.Describe(46.1283, -112.9423)).Returns("Anaconda, Montana, US");
        var ids = await IdsAsync(moving, other, free);

        var plan = await _f.Planner.PlanAsync(To("Anaconda"), ids);

        await Assert.That(plan.Clashes.Select(c => (c.File.Id, c.NextFree))).IsEquivalentTo(new[] { (ids[0], "IMG_6435 (2).JPG"), (ids[1], "MOV_8203 (1).MP4") }, CollectionOrdering.Matching);
        var picture = plan.Clashes[0].Existing;
        await Assert.That(picture.Select(e => (e.File.Name, e.Identical, e.PlaceName, e.File.Details.Width)))
            .IsEquivalentTo(new[] { ("IMG_6435.JPG", true, null, 0), ("IMG_6435 (1).JPG", false, "Anaconda, Montana, US", 3024) }, CollectionOrdering.Matching);
        // Every file there can be shown: each has an id of its own.
        await Assert.That((await _f.Library.FindAsync(picture[0].File.Id))!.Path).IsEqualTo(_f.In("Anaconda", "IMG_6435.JPG"));
        var video = plan.Clashes[1].Existing.Single();
        await Assert.That((video.File.Name, video.Identical, video.File.IsVideo)).IsEqualTo(("MOV_8203.MP4", false, true));
        await Assert.That(plan.Taken[_f.In("Anaconda")].Count).IsEqualTo(4);
        // Files of different sizes are not read through: only the three of one size were.
        await Assert.That(_f.Hasher.Calls).IsEqualTo(3);
    }

    [Test]
    public async Task A_File_That_Goes_Missing_While_The_Plan_Is_Made_Is_Left_Out_Of_It()
    {
        var moving = _f.Put("IMG_1.JPG");
        var other = _f.Put("IMG_2.JPG");
        _f.Put("There/IMG_1.JPG");
        var numbered = _f.Put("There/IMG_1 (1).JPG");
        var second = _f.Put("There/IMG_2.JPG");
        var ids = await IdsAsync(moving, other);
        // While the file that has the first name is looked at, its numbered copy and the file that has the second name are taken away.
        _f.Reader.OnRead = _ => { File.Delete(numbered); File.Delete(second); };

        var plan = await _f.Planner.PlanAsync(To("There"), ids);

        // The first name is still taken, by one file; the second no longer is, so its file simply keeps its name.
        var clash = plan.Clashes.Single();
        await Assert.That((clash.File.Name, clash.NextFree)).IsEqualTo(("IMG_1.JPG", "IMG_1 (1).JPG"));
        await Assert.That(clash.Existing.Select(e => e.File.Name)).IsEquivalentTo(new[] { "IMG_1.JPG" }, CollectionOrdering.Matching);
        await Assert.That(plan.Taken[_f.In("There")]).IsEquivalentTo(new[] { "IMG_1.JPG" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Plans_From_What_The_Listing_Knows_Without_Going_Back_To_Each_File()
    {
        var kept = _f.Put("IMG_1.JPG", "same size");
        var gone = _f.Put("IMG_2.JPG", "same size");
        var unlisted = _f.Put("IMG_3.JPG");
        _f.Put("There/IMG_2.JPG", "same size");
        await _f.Library.ListAsync(_f.Root.FullName);
        var ids = await IdsAsync(kept, gone);
        // One file has gone since the folder was listed; another is known only by an id handed out for it.
        File.Delete(gone);
        var library = _f.AnotherRun();
        var planner = new OrganizePlanner(library, _f.Finder, _f.Places);
        var known = (await library.ListAsync(_f.Root.FullName)).ToDictionary(f => f.Name);
        File.Delete(unlisted);

        var plan = await _f.Planner.PlanAsync(To("There"), ids);

        // The file that has gone is still planned for, as it was last seen: moving it is what will find it gone.
        // Its name is taken there, and a file that can no longer be read is not known to be the same as the one that has it.
        await Assert.That(plan.Items.Select(i => i.File.Name)).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG" }, CollectionOrdering.Matching);
        await Assert.That(plan.Clashes.Single().Existing.Single().Identical).IsFalse();
        await Assert.That(_f.Library.Recall("never-given-out")).IsNull();
        // In another run nothing was ever seen of the deleted file but its listing; a plan there leaves a vanished file as it was listed too.
        var later = await planner.PlanAsync(To("Elsewhere"), [known["IMG_1.JPG"].Id, known["IMG_3.JPG"].Id]);
        await Assert.That(later.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Counts_The_Live_Photo_Videos_And_Edit_Files_That_Would_Be_Left_Behind()
    {
        var picture = _f.Put("IMG_1234.HEIC", "id:AAAA");
        var video = _f.Put("IMG_1234.MOV", "id:AAAA");
        _f.Put("IMG_1234.AAE");
        var plain = _f.Put("IMG_5.JPG");

        var alone = await _f.Planner.PlanAsync(To("Anaconda"), await IdsAsync(picture, plain));
        await Assert.That(alone.Companions).IsEqualTo(2);
        // The video is going anyway, so only the edit file would come along.
        var both = await _f.Planner.PlanAsync(To("Anaconda"), await IdsAsync(picture, video, plain));
        await Assert.That(both.Companions).IsEqualTo(1);
    }
}
