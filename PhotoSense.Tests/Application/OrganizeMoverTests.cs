using Moq;
using PhotoSense.Application.Organizing;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests.Application;

public sealed class OrganizeMoverTests : IDisposable
{
    private readonly OrganizeFixture _f = new();
    public void Dispose() => _f.Dispose();

    private OrganizeDestination To(string name, bool yearSplit = false) => new(_f.Root.FullName, name, false, yearSplit);

    private async Task<List<(string Id, string? Name)>> FilesAsync(params string[] paths)
    {
        var files = new List<(string, string?)>();
        foreach (var path in paths) files.Add((await _f.IdAsync(path), null));
        return files;
    }

    [Test]
    public async Task Moves_Files_Into_A_New_Folder_By_Year_And_Keeps_A_Record_Of_It()
    {
        var a = _f.Put("IMG_1.JPG", "aaaa", year: 2022);
        var b = _f.Put("Camera Roll/IMG_2.JPG", "bb", year: 2023);
        var scanned = await _f.ScannedAsync(a);
        var files = await FilesAsync(a, b);

        var result = await _f.Mover().ApplyAsync(To("Anaconda", yearSplit: true), copy: false, bringCompanions: true, "Anaconda", files);

        await Assert.That((result.Done, result.Bytes, result.Companions, result.Renamed, result.Skipped, result.Problems.Count)).IsEqualTo((2, 6L, 0, 0, 0, 0));
        await Assert.That((_f.Files("Anaconda", "2022").Single(), _f.Files("Anaconda", "2023").Single())).IsEqualTo(("IMG_1.JPG", "IMG_2.JPG"));
        await Assert.That(File.Exists(a) || File.Exists(b)).IsFalse();
        await Assert.That(result.Items).IsEquivalentTo(new[] { (files[0].Id, _f.In("Anaconda", "2022", "IMG_1.JPG")), (files[1].Id, _f.In("Anaconda", "2023", "IMG_2.JPG")) }, CollectionOrdering.Matching);

        // The scan's record follows the file, and the library knows the file where it now is without reading it again.
        await Assert.That((await _f.Repo.GetAsync(scanned.Id))!.SourcePath).IsEqualTo(_f.In("Anaconda", "2022", "IMG_1.JPG"));
        var reads = _f.Reader.Reads.Count;
        await Assert.That((await _f.Library.DescribeAsync(_f.In("Anaconda", "2023", "IMG_2.JPG")))!.SizeBytes).IsEqualTo(2);
        await Assert.That(_f.Reader.Reads.Count).IsEqualTo(reads);

        var batch = (await _f.Batches.GetAsync(result.BatchId!.Value))!;
        await Assert.That((batch.Label, batch.Copy, batch.Files, batch.Bytes, batch.Items.Count, batch.Undone)).IsEqualTo(("Anaconda", false, 2, 6L, 2, false));
        await Assert.That(batch.CreatedFolders).IsEquivalentTo(new[] { _f.In("Anaconda", "2022"), _f.In("Anaconda"), _f.In("Anaconda", "2023") }, CollectionOrdering.Matching);
        var entry = _f.Audited.Single();
        await Assert.That((entry.Action, entry.PhotoId, entry.Details)).IsEqualTo(("OrganizeMove", batch.Id.ToString(), $"2 files to {_f.In("Anaconda")} with 0 linked files"));
    }

    [Test]
    public async Task Moves_Each_File_Into_The_Subfolder_Asked_For_It()
    {
        var a = _f.Put("IMG_1.JPG", year: 2022);
        var b = _f.Put("IMG_2.JPG", year: 2023);
        var c = _f.Put("IMG_3.JPG", year: 2021);

        var result = await _f.Mover().ApplyAsync(To("2022", yearSplit: true), copy: false, bringCompanions: false, "2022", await FilesAsync(a, b, c), ["Myrtle Beach", "Hurl Rocks\\Shore"]);

        await Assert.That(result.Done).IsEqualTo(3);
        // The third was asked no subfolder, so the destination's own split by year decides.
        await Assert.That((_f.Files("2022", "Myrtle Beach").Single(), _f.Files("2022", "Hurl Rocks", "Shore").Single(), _f.Files("2022", "2021").Single())).IsEqualTo(("IMG_1.JPG", "IMG_2.JPG", "IMG_3.JPG"));
    }

    [Test]
    public async Task Removed_Files_Are_Held_Inside_The_Root_With_What_Belongs_To_Them_And_Dropped_From_The_Scan()
    {
        var top = _f.Put("IMG_1.JPG", "aaaa");
        var picture = _f.Put("Camera Roll/IMG_1234.HEIC", "id:AAAA");
        var video = _f.Put("Camera Roll/IMG_1234.MOV", "id:AAAA");
        var stays = _f.Put("Camera Roll/IMG_9.JPG");
        // A file of the same name was removed before: nothing held is replaced.
        _f.Put("_PhotoSense_Removed/IMG_1.JPG", "removed earlier");
        var scanned = await _f.ScannedAsync(top);
        var scannedVideo = await _f.ScannedAsync(video);
        var ids = new[] { await _f.IdAsync(top), await _f.IdAsync(picture), await _f.IdAsync(video), "unknown" };

        var result = await _f.Mover().RemoveAsync(_f.Root.FullName + Path.DirectorySeparatorChar, ids);

        // The video went with its picture, so being asked for as well it is neither removed twice nor missed: it is said to have gone, and where to.
        await Assert.That((result.Done, result.Bytes, result.Companions, result.Renamed, result.Skipped)).IsEqualTo((2, 11L, 1, 1, 1));
        await Assert.That(result.Problems.Single()).IsEqualTo("A file is no longer where it was. Choose the folder again to see what is there now.");
        await Assert.That(result.Items).IsEquivalentTo(new[]
        {
            (ids[0], _f.In("_PhotoSense_Removed", "IMG_1 (1).JPG")), (ids[1], _f.In("_PhotoSense_Removed", "Camera Roll", "IMG_1234.HEIC")), (ids[2], _f.In("_PhotoSense_Removed", "Camera Roll", "IMG_1234.MOV")),
        }, CollectionOrdering.Matching);
        await Assert.That(_f.Files("_PhotoSense_Removed")).IsEquivalentTo(new[] { "IMG_1 (1).JPG", "IMG_1.JPG" }, CollectionOrdering.Matching);
        await Assert.That(_f.Files("_PhotoSense_Removed", "Camera Roll")).IsEquivalentTo(new[] { "IMG_1234.HEIC", "IMG_1234.MOV" }, CollectionOrdering.Matching);
        await Assert.That((_f.Files().Count, _f.Files("Camera Roll").Single())).IsEqualTo((0, "IMG_9.JPG"));
        await Assert.That(File.Exists(stays)).IsTrue();

        // What a scan recorded of them is gone, the folder no longer lists them, and the removal is on record.
        await Assert.That((await _f.Repo.GetAsync(scanned.Id), await _f.Repo.GetAsync(scannedVideo.Id))).IsEqualTo(((PhotoSense.Domain.Entities.Photo?)null, (PhotoSense.Domain.Entities.Photo?)null));
        await Assert.That((await _f.Library.ListAsync(_f.Root.FullName)).Select(f => f.Name)).IsEquivalentTo(new[] { "IMG_9.JPG" }, CollectionOrdering.Matching);
        var batch = (await _f.Batches.GetAsync(result.BatchId!.Value))!;
        await Assert.That((batch.Label, batch.Removed, batch.Copy, batch.Files, batch.Items.Count)).IsEqualTo((_f.Root.FullName, true, false, 2, 3));
        await Assert.That(batch.CreatedFolders).IsEquivalentTo(new[] { _f.In("_PhotoSense_Removed", "Camera Roll") }, CollectionOrdering.Matching);
        var entry = _f.Audited.Single();
        await Assert.That((entry.Action, entry.Details)).IsEqualTo(("OrganizeRemove", $"2 files to {_f.In("_PhotoSense_Removed")} with 1 linked files"));
    }

    [Test]
    public async Task Undoing_A_Removal_Puts_The_Files_Back_And_Takes_Away_The_Holding_Folders_Left_Empty()
    {
        var top = _f.Put("IMG_1.JPG", "aaaa");
        var picture = _f.Put("Camera Roll/IMG_1234.HEIC", "id:AAAA");
        var video = _f.Put("Camera Roll/IMG_1234.MOV", "id:AAAA");
        var mover = _f.Mover();
        var result = await mover.RemoveAsync(_f.Root.FullName, [await _f.IdAsync(top), await _f.IdAsync(picture)]);
        await Assert.That(File.Exists(top) || File.Exists(picture) || File.Exists(video)).IsFalse();

        var undone = await mover.UndoAsync(result.BatchId!.Value);

        await Assert.That((undone.Found, undone.Restored, undone.Skipped)).IsEqualTo((true, 2, 0));
        await Assert.That((File.ReadAllText(top), File.Exists(picture), File.Exists(video))).IsEqualTo(("aaaa", true, true));
        await Assert.That(Directory.Exists(_f.In("_PhotoSense_Removed"))).IsFalse();
        await Assert.That((await _f.Library.ListAsync(_f.Root.FullName)).Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_File_From_Outside_The_Root_Is_Held_Beside_Itself()
    {
        var outside = _f.Put("Elsewhere/IMG_1.JPG");
        var inside = _f.Put("Phone/2022/IMG_2.JPG");
        // A folder whose name merely begins like the holding folder's is not inside it.
        var result = await _f.Mover().RemoveAsync(_f.In("Phone"), [await _f.IdAsync(outside), await _f.IdAsync(inside)]);

        await Assert.That(result.Done).IsEqualTo(2);
        await Assert.That((_f.Files("Elsewhere", "_PhotoSense_Removed").Single(), _f.Files("Phone", "_PhotoSense_Removed", "2022").Single())).IsEqualTo(("IMG_1.JPG", "IMG_2.JPG"));
        // Nothing to remove leaves nothing to undo.
        var nothing = await _f.Mover().RemoveAsync(_f.In("Phone"), ["unknown"]);
        await Assert.That((nothing.BatchId, nothing.Skipped)).IsEqualTo(((Guid?)null, 1));
    }

    [Test]
    public async Task Never_Replaces_A_File_Whatever_Name_Is_Asked_For()
    {
        var a = _f.Put("IMG_1.JPG", "arriving");
        var b = _f.Put("b/IMG_1.JPG", "second");
        var c = _f.Put("IMG_3.JPG", "renamed");
        var d = _f.Put("IMG_4.JPG", "bad name");
        var e = _f.Put("IMG_5.JPG", "taken name");
        _f.Put("There/IMG_1.JPG", "was there");
        _f.Put("There/Mine.JPG", "was there too");
        var files = new List<(string Id, string? Name)>
        {
            (await _f.IdAsync(a), "IMG_1.JPG"), (await _f.IdAsync(b), null), (await _f.IdAsync(c), "IMG_3 2023-06.JPG"),
            (await _f.IdAsync(d), "what?.JPG"), (await _f.IdAsync(e), "Mine.JPG"),
        };

        var result = await _f.Mover().ApplyAsync(To("There"), copy: false, bringCompanions: false, "There", files);

        await Assert.That(_f.Files("There")).IsEquivalentTo(new[] { "IMG_1 (1).JPG", "IMG_1 (2).JPG", "IMG_1.JPG", "IMG_3 2023-06.JPG", "IMG_4.JPG", "Mine (1).JPG", "Mine.JPG" }, CollectionOrdering.Matching);
        await Assert.That((File.ReadAllText(_f.In("There", "IMG_1.JPG")), File.ReadAllText(_f.In("There", "Mine.JPG")), File.ReadAllText(_f.In("There", "IMG_1 (1).JPG")), File.ReadAllText(_f.In("There", "Mine (1).JPG"))))
            .IsEqualTo(("was there", "was there too", "arriving", "taken name"));
        // Four arrived under another name than their own: two numbered, one as asked, one numbered from the name asked for.
        await Assert.That((result.Done, result.Renamed, result.Skipped)).IsEqualTo((5, 4, 0));
    }

    [Test]
    public async Task Copies_Leave_The_Files_Where_They_Are()
    {
        var a = _f.Put("IMG_1.JPG", "aaaa");
        var scanned = await _f.ScannedAsync(a);

        var result = await _f.Mover().ApplyAsync(To("Copies"), copy: true, bringCompanions: false, "Copies", await FilesAsync(a));

        await Assert.That((File.ReadAllText(a), File.ReadAllText(_f.In("Copies", "IMG_1.JPG")))).IsEqualTo(("aaaa", "aaaa"));
        await Assert.That((await _f.Repo.GetAsync(scanned.Id))!.SourcePath).IsEqualTo(a);
        await Assert.That(((await _f.Batches.GetAsync(result.BatchId!.Value))!.Copy, _f.Audited.Single().Action)).IsEqualTo((true, "OrganizeCopy"));
        // Both are known, each where it is.
        await Assert.That(((await _f.Library.DescribeAsync(a))!.FromScan, (await _f.Library.DescribeAsync(_f.In("Copies", "IMG_1.JPG")))!.FromScan)).IsEqualTo((true, true));
    }

    [Test]
    public async Task Brings_A_Pictures_Live_Photo_Video_And_Edit_Files_Along_Under_Its_Name()
    {
        var picture = _f.Put("IMG_1234.HEIC", "id:AAAA");
        var video = _f.Put("IMG_1234.MOV", "id:AAAA");
        _f.Put("IMG_1234.AAE");
        _f.Put("IMG_O1234.AAE");
        var plain = _f.Put("IMG_5.JPG");
        _f.Put("There/IMG_1234.HEIC", "was there");
        var videoRecord = await _f.ScannedAsync(video);
        // The video is asked for as well, after the picture: by then it has gone along with it.
        var files = await FilesAsync(picture, video, plain);

        var result = await _f.Mover().ApplyAsync(To("There"), copy: false, bringCompanions: true, "There", files);

        await Assert.That((result.Done, result.Companions, result.Renamed, result.Skipped, result.Problems.Count)).IsEqualTo((2, 3, 1, 0, 0));
        // The picture's name was taken, so it got a number; the files that shared its name got the same one.
        await Assert.That(_f.Files("There")).IsEquivalentTo(new[] { "IMG_1234 (1).AAE", "IMG_1234 (1).HEIC", "IMG_1234 (1).MOV", "IMG_1234.HEIC", "IMG_5.JPG", "IMG_O1234.AAE" }, CollectionOrdering.Matching);
        await Assert.That(_f.Files()).IsEmpty();
        await Assert.That((await _f.Repo.GetAsync(videoRecord.Id))!.SourcePath).IsEqualTo(_f.In("There", "IMG_1234 (1).MOV"));
        var batch = (await _f.Batches.GetAsync(result.BatchId!.Value))!;
        await Assert.That((batch.Files, batch.Items.Count, batch.Items.Count(i => i.Companion))).IsEqualTo((2, 5, 3));
        await Assert.That(_f.Audited.Single().Details).EndsWith("with 3 linked files");
    }

    [Test]
    public async Task Leaves_Companions_Behind_When_Not_Asked_To_Bring_Them()
    {
        var picture = _f.Put("IMG_1234.HEIC", "id:AAAA");
        _f.Put("IMG_1234.MOV", "id:AAAA");
        _f.Put("IMG_1234.AAE");

        var result = await _f.Mover().ApplyAsync(To("There"), copy: false, bringCompanions: false, "There", await FilesAsync(picture));

        await Assert.That((result.Done, result.Companions)).IsEqualTo((1, 0));
        await Assert.That(_f.Files()).IsEquivalentTo(new[] { "IMG_1234.AAE", "IMG_1234.MOV" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Says_What_Could_Not_Be_Moved_And_Goes_On_With_The_Rest()
    {
        var fine = _f.Put("IMG_1.JPG", year: 2022);
        var blocked = _f.Put("IMG_2.JPG", year: 2023);
        var gone = _f.Put("IMG_3.JPG", year: 2022);
        var settled = _f.Put("There/2022/IMG_4.JPG", year: 2022);
        var withCompanion = _f.Put("IMG_1234.HEIC", year: 2022);
        _f.Put("IMG_1234.MOV", year: 2022);
        var files = await FilesAsync(fine, blocked, gone, settled, withCompanion);
        files.Add(("never-given-out", null));
        File.Delete(gone);
        // A file stands where the folder for 2023 would have to be made.
        _f.Put("There/2023");
        // The finder names a companion that is not there to be moved.
        var finder = new Mock<ICompanionFileFinder>();
        finder.Setup(c => c.FindAmong(withCompanion, It.IsAny<IReadOnlyList<string>>())).Returns([_f.In("IMG_1234 missing.MOV")]);

        var result = await _f.Mover(finder.Object).ApplyAsync(To("There", yearSplit: true), copy: false, bringCompanions: true, "There", files);

        await Assert.That((result.Done, result.Skipped, result.Companions)).IsEqualTo((2, 3, 0));
        await Assert.That(result.Problems.Count).IsEqualTo(4);
        await Assert.That(result.Problems.Count(p => p.StartsWith("A file is no longer where it was."))).IsEqualTo(2);
        await Assert.That(result.Problems.Count(p => p.StartsWith("IMG_2.JPG: "))).IsEqualTo(1);
        await Assert.That(result.Problems.Count(p => p.StartsWith("IMG_1234 missing.MOV: "))).IsEqualTo(1);
        await Assert.That(_f.Files("There", "2022")).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_1234.HEIC", "IMG_4.JPG" }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(blocked)).IsTrue();
        // Only the file that had another of its item beside it was asked about.
        finder.Verify(c => c.FindAmong(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Once);
        finder.Verify(c => c.FindAmong(withCompanion, It.Is<IReadOnlyList<string>>(s => s.Single() == _f.In("IMG_1234.MOV"))), Times.Once);
    }

    [Test]
    public async Task Moving_Nothing_Leaves_No_Record()
    {
        var settled = _f.Put("There/IMG_1.JPG");
        var files = await FilesAsync(settled);
        files.Add(("never-given-out", null));

        var result = await _f.Mover().ApplyAsync(To("There"), copy: false, bringCompanions: true, "There", files);

        await Assert.That((result.BatchId, result.Done, result.Skipped, result.Items.Count)).IsEqualTo((null, 0, 1, 0));
        await Assert.That(await _f.Batches.RecentAsync()).IsEmpty();
        await Assert.That(_f.Audited).IsEmpty();
    }

    [Test]
    public async Task Undoing_A_Move_Puts_Every_File_Back_And_Takes_Away_The_Folders_It_Made()
    {
        var picture = _f.Put("Camera Roll/IMG_1234.HEIC", "id:AAAA", year: 2022);
        var video = _f.Put("Camera Roll/IMG_1234.MOV", "id:AAAA", year: 2022);
        var other = _f.Put("IMG_5.JPG", year: 2023);
        var scanned = await _f.ScannedAsync(other);
        var mover = _f.Mover();
        var result = await mover.ApplyAsync(To("Trips\\Glacier", yearSplit: true), copy: false, bringCompanions: true, "Trips\\Glacier", await FilesAsync(picture, other));
        await Assert.That(File.Exists(picture) || File.Exists(video) || File.Exists(other)).IsFalse();
        // Something else has been put in one of the new folders since.
        _f.Put("Trips/Glacier/2023/kept.txt");

        var undone = await mover.UndoAsync(result.BatchId!.Value);

        await Assert.That((undone.Found, undone.Restored, undone.Skipped, undone.Problems.Count)).IsEqualTo((true, 2, 0, 0));
        await Assert.That((File.ReadAllText(picture), File.Exists(video), File.Exists(other))).IsEqualTo(("id:AAAA", true, true));
        await Assert.That((await _f.Repo.GetAsync(scanned.Id))!.SourcePath).IsEqualTo(other);
        // The folder for 2022 is empty again and goes; the one that now holds something stays, and so do those above it.
        await Assert.That((Directory.Exists(_f.In("Trips", "Glacier", "2022")), Directory.Exists(_f.In("Trips", "Glacier", "2023")), Directory.Exists(_f.In("Trips")))).IsEqualTo((false, true, true));
        await Assert.That(_f.Audited.Select(a => a.Action)).IsEquivalentTo(new[] { "OrganizeMove", "OrganizeUndo" }, CollectionOrdering.Matching);
        await Assert.That(_f.Audited[1].Details).IsEqualTo("2 files put back, 0 left");

        // Once undone it cannot be undone again, and nothing that was never done can be.
        await Assert.That((await mover.UndoAsync(result.BatchId.Value)).Found).IsFalse();
        await Assert.That((await mover.UndoAsync(Guid.NewGuid())).Found).IsFalse();
        await Assert.That(await _f.Batches.RecentAsync()).IsEmpty();
    }

    [Test]
    public async Task Undoing_A_Move_Leaves_Alone_What_It_Cannot_Put_Back()
    {
        var a = _f.Put("IMG_1.JPG", "a");
        var b = _f.Put("IMG_2.JPG", "b");
        var c = _f.Put("Old/IMG_3.JPG", "c");
        var d = _f.Put("IMG_4.JPG", "d");
        var mover = _f.Mover();
        var result = await mover.ApplyAsync(To("There"), copy: false, bringCompanions: false, "There", await FilesAsync(a, b, c, d));
        // One has been taken out of the folder since; another's old place has a new file in it; where a third came from, a file now stands.
        File.Delete(_f.In("There", "IMG_1.JPG"));
        _f.Put("IMG_2.JPG", "someone else's");
        Directory.Delete(_f.In("Old"));
        _f.Put("Old");

        var undone = await mover.UndoAsync(result.BatchId!.Value);

        await Assert.That((undone.Found, undone.Restored, undone.Skipped)).IsEqualTo((true, 1, 3));
        await Assert.That(undone.Problems.Count(p => p == "IMG_1.JPG is no longer where it was put.")).IsEqualTo(1);
        await Assert.That(undone.Problems.Count(p => p == "IMG_2.JPG was left where it is: another file now has its old place.")).IsEqualTo(1);
        await Assert.That(undone.Problems.Count(p => p.StartsWith("IMG_3.JPG: "))).IsEqualTo(1);
        await Assert.That((File.ReadAllText(_f.In("IMG_2.JPG")), File.ReadAllText(d))).IsEqualTo(("someone else's", "d"));
        await Assert.That(_f.Files("There")).IsEquivalentTo(new[] { "IMG_2.JPG", "IMG_3.JPG" }, CollectionOrdering.Matching);
        await Assert.That(_f.Audited[1].Details).IsEqualTo("1 files put back, 3 left");
    }

    [Test]
    public async Task Undoing_A_Copy_Removes_The_Copies_That_Are_Still_As_They_Were_Written()
    {
        var a = _f.Put("IMG_1.JPG", "a");
        var b = _f.Put("IMG_2.JPG", "b");
        var c = _f.Put("IMG_3.JPG", "c");
        var d = _f.Put("IMG_4.JPG", "d");
        var mover = _f.Mover();
        var result = await mover.ApplyAsync(To("Copies"), copy: true, bringCompanions: false, "Copies", await FilesAsync(a, b, c, d));
        // One copy has been worked on since, one has been touched, and one's original has gone.
        File.WriteAllText(_f.In("Copies", "IMG_2.JPG"), "edited");
        File.SetLastWriteTimeUtc(_f.In("Copies", "IMG_3.JPG"), DateTime.UtcNow.AddMinutes(5));
        File.Delete(d);

        var undone = await mover.UndoAsync(result.BatchId!.Value);

        await Assert.That((undone.Restored, undone.Skipped)).IsEqualTo((1, 3));
        await Assert.That(undone.Problems.Count(p => p.EndsWith("has changed since it was copied, so it was left."))).IsEqualTo(2);
        await Assert.That(undone.Problems.Count(p => p == "IMG_4.JPG was left: the file it was copied from is no longer there.")).IsEqualTo(1);
        await Assert.That(_f.Files("Copies")).IsEquivalentTo(new[] { "IMG_2.JPG", "IMG_3.JPG", "IMG_4.JPG" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(a)).IsEqualTo("a");
    }
}
