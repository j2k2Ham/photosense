using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Tests.Application;

public sealed class RemovedFilesTests : IDisposable
{
    private const string Held = PhotoStorageOptions.RemovedFolderName;
    private readonly OrganizeFixture _f = new();

    public void Dispose() => _f.Dispose();

    private async Task<List<(string, int, long)>> FoundAsync(params string?[] roots)
        => (await _f.Removed().FindAsync(roots)).Select(h => (h.Path, h.Files, h.Bytes)).ToList();

    private async Task<Guid> RemoveAsync(string root, params string[] paths)
    {
        var ids = new List<string>();
        foreach (var path in paths) ids.Add(await _f.IdAsync(path));
        return (await _f.Mover().RemoveAsync(root, ids)).BatchId!.Value;
    }

    // ---- finding

    [Test]
    public async Task Finds_The_Folders_Of_Removed_Files_At_Any_Depth_With_How_Much_Each_Holds()
    {
        _f.Put($"Phone/{Held}/IMG_1.JPG", "aaaa");
        _f.Put($"Phone/{Held}/2022/IMG_2.JPG", "bb");
        // A hidden file is as much in the folder as any other.
        var hidden = _f.Put($"Phone/{Held}/2022/.IMG_2.AAE", "c");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        // One inside another is part of the outer one.
        _f.Put($"Phone/{Held}/Old/{Held}/IMG_3.JPG", "d");
        _f.Put($"Phone/Trips/Glacier/{Held.ToLowerInvariant()}/IMG_4.JPG", "eeeee");
        // Neither a file that was never removed, nor a folder whose name only begins the same way.
        _f.Put("Phone/Trips/IMG_5.JPG");
        _f.Put($"Phone/{Held}-kept/IMG_6.JPG");
        // One that holds nothing is not listed.
        Directory.CreateDirectory(_f.In("Phone", "Empty", Held, "2020"));

        await Assert.That(await FoundAsync(_f.In("Phone"))).IsEquivalentTo(new[]
        {
            (_f.In("Phone", Held), 4, 8L), (_f.In("Phone", "Trips", "Glacier", Held.ToLowerInvariant()), 1, 5L),
        }, CollectionOrdering.Any);
    }

    [Test]
    public async Task Looks_In_The_Folders_Given_And_In_Every_Folder_A_Scan_Covered_Once_Each()
    {
        var held = _f.Put($"Phone/{Held}/IMG_1.JPG", "aaaa");
        _f.Put($"Backup/{Held}/IMG_2.JPG", "bb");
        _f.Put($"Other/{Held}/IMG_3.JPG", "c");
        // What scans recorded: one folder twice over, and a file from before the scanned folder was kept.
        foreach (var (name, root) in new[] { ("a.jpg", _f.In("Backup")), ("b.jpg", _f.In("Backup")), ("c.jpg", (string?)null) })
        {
            var photo = await _f.ScannedAsync(_f.Put($"Backup/{name}"));
            photo.ScanRoot = root;
            await _f.Repo.AddOrUpdateAsync(photo);
        }

        // A folder given twice, in two spellings, is looked through once; what is no folder at all is passed over.
        var found = await FoundAsync(_f.In("Phone"), _f.In("Phone") + Path.DirectorySeparatorChar, null, " ", "Phone", _f.In("nowhere"), held);

        await Assert.That(found).IsEquivalentTo(new[] { (_f.In("Backup", Held), 1, 2L), (_f.In("Phone", Held), 1, 4L) }, CollectionOrdering.Matching);
        await Assert.That(await FoundAsync()).IsEquivalentTo(new[] { (_f.In("Backup", Held), 1, 2L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Finds_What_Organize_Removed_Even_From_Outside_The_Folder_Being_Organized()
    {
        var inside = _f.Put("Phone/IMG_1.JPG", "aaaa");
        var outside = _f.Put("Elsewhere/IMG_2.JPG", "bb");
        await RemoveAsync(_f.In("Phone"), inside, outside);
        // A move is no removal, wherever it put its files; and a removal says nothing of a folder that has gone, or of a file held nowhere.
        _f.Put($"Moved/{Held}/IMG_3.JPG");
        await _f.Batches.AddAsync(new OrganizeBatch { Items = [new OrganizeBatchItem { To = _f.In("Moved", Held, "IMG_3.JPG") }] });
        await _f.Batches.AddAsync(new OrganizeBatch { Removed = true, Items = [new OrganizeBatchItem { To = _f.In("Gone", Held, "IMG_4.JPG") }, new OrganizeBatchItem { To = _f.In("Loose", "IMG_5.JPG") }] });

        await Assert.That(await FoundAsync()).IsEquivalentTo(new[] { (_f.In("Elsewhere", Held), 1, 2L), (_f.In("Phone", Held), 1, 4L) }, CollectionOrdering.Any);
    }

    [Test]
    public async Task Does_Not_Follow_A_Link_To_Another_Folder()
    {
        _f.Put($"Real/{Held}/IMG_1.JPG");
        Directory.CreateDirectory(_f.In("Phone"));
        using var link = TestFiles.LinkFolder(_f.In("Phone", "Link"), _f.In("Real"));

        await Assert.That(await FoundAsync(_f.In("Phone"))).IsEmpty();
    }

    // ---- erasing

    [Test]
    public async Task Erases_Everything_In_The_Folders_And_The_Folders_Themselves_And_Nothing_Else()
    {
        _f.Put($"Phone/{Held}/IMG_1.JPG", "aaaa");
        var marked = _f.Put($"Phone/{Held}/2022/Trips/IMG_2.JPG", "bb");
        File.SetAttributes(marked, File.GetAttributes(marked) | FileAttributes.ReadOnly);
        var hidden = _f.Put($"Phone/{Held}/2022/.IMG_2.AAE", "c");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        Directory.CreateDirectory(_f.In("Phone", Held, "Empty"));
        var kept = _f.Put("Phone/IMG_9.JPG");
        _f.Put($"Other/{Held}/IMG_3.JPG", "dddd");

        // Named twice, in two spellings, it is still one folder.
        var result = await _f.Removed().EraseAsync([_f.In("Phone", Held), _f.In("Phone", Held) + Path.DirectorySeparatorChar]);

        await Assert.That((result.Erased, result.Bytes, result.Skipped, result.Problems.Count)).IsEqualTo((3, 7L, 0, 0));
        await Assert.That((Directory.Exists(_f.In("Phone", Held)), File.Exists(kept), _f.Files("Other", Held).Single())).IsEqualTo((false, true, "IMG_3.JPG"));
        var entry = _f.Audited.Single();
        await Assert.That((entry.Action, entry.Details)).IsEqualTo(("EraseRemoved", "3 files erased from 1 folders, 0 left"));
        await Assert.That(await FoundAsync(_f.In("Phone"), _f.In("Other"))).IsEquivalentTo(new[] { (_f.In("Other", Held), 1, 4L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Leaves_What_Cannot_Be_Erased_With_The_Folders_It_Is_In()
    {
        _f.Put($"Phone/{Held}/IMG_1.JPG", "aaaa");
        _f.Put($"Phone/{Held}/2022/IMG_2.JPG", "bb");
        _f.Put($"Phone/{Held}/2023/IMG_3.JPG", "c");
        _f.Put($"Phone/{Held}/2024/IMG_4.JPG", "d");
        void Erase(string path)
        {
            if (path.EndsWith("IMG_2.JPG", StringComparison.Ordinal)) throw new IOException("The file is in use.");
            if (path.EndsWith("IMG_3.JPG", StringComparison.Ordinal)) throw new UnauthorizedAccessException("Access is denied.");
            File.Delete(path);
        }

        var result = await _f.Removed(Erase).EraseAsync([_f.In("Phone", Held)]);

        await Assert.That((result.Erased, result.Bytes, result.Skipped)).IsEqualTo((2, 5L, 2));
        await Assert.That(result.Problems).IsEquivalentTo(new[] { "IMG_2.JPG: The file is in use.", "IMG_3.JPG: Access is denied." }, CollectionOrdering.Any);
        await Assert.That((_f.Files("Phone", Held).Count, _f.Files("Phone", Held, "2022").Single(), _f.Files("Phone", Held, "2023").Single(), Directory.Exists(_f.In("Phone", Held, "2024"))))
            .IsEqualTo((0, "IMG_2.JPG", "IMG_3.JPG", false));
        await Assert.That(_f.Audited.Single().Details).IsEqualTo("2 files erased from 1 folders, 2 left");
    }

    [Test]
    public async Task Erases_Nothing_When_Anything_But_A_Folder_Of_Removed_Files_Is_Named()
    {
        var held = _f.Put($"Phone/{Held}/IMG_1.JPG");
        var photo = _f.Put("Phone/IMG_2.JPG");

        foreach (var other in new[] { null, " ", Held, _f.In("Phone"), _f.In("Phone", Held, "2022") })
        {
            var refused = await Assert.ThrowsAsync<ArgumentException>(() => _f.Removed().EraseAsync([_f.In("Phone", Held), other]));
            await Assert.That(refused.Message).IsEqualTo($"Not a folder of removed files: {other}");
        }

        await Assert.That((File.Exists(held), File.Exists(photo))).IsEqualTo((true, true));
        await Assert.That(_f.Audited).IsEmpty();
    }

    [Test]
    public async Task Leaves_Alone_A_Folder_That_Has_Gone_And_A_Link_To_Somewhere_Else()
    {
        var real = _f.Put("Real/IMG_1.JPG");
        Directory.CreateDirectory(_f.In("Phone"));
        using var link = TestFiles.LinkFolder(_f.In("Phone", Held), _f.In("Real"));

        var result = await _f.Removed().EraseAsync([_f.In("Gone", Held), _f.In("Phone", Held)]);

        await Assert.That((result.Erased, result.Bytes, result.Skipped, File.Exists(real))).IsEqualTo((0, 0L, 0, true));
        await Assert.That(_f.Audited.Single().Details).IsEqualTo("0 files erased from 0 folders, 0 left");
    }

    [Test]
    public async Task A_Removal_Whose_Files_Are_All_Erased_Is_No_Longer_Offered_To_Be_Undone()
    {
        var erased = await RemoveAsync(_f.In("Phone"), _f.Put("Phone/IMG_1.JPG"), _f.Put("Phone/2022/IMG_2.JPG"));
        var partly = await RemoveAsync(_f.In("Backup"), _f.Put("Backup/IMG_3.JPG"), _f.Put("Backup/IMG_4.JPG"));
        var untouched = await RemoveAsync(_f.In("Other"), _f.Put("Other/IMG_5.JPG"));
        var moved = (await _f.Mover().ApplyAsync(new PhotoSense.Application.Organizing.OrganizeDestination(_f.Root.FullName, "Trips", false, false), copy: false, bringCompanions: false, "Trips",
            [(await _f.IdAsync(_f.Put("IMG_6.JPG")), null)])).BatchId!.Value;
        void Erase(string path)
        {
            if (path.EndsWith("IMG_4.JPG", StringComparison.Ordinal)) throw new IOException("The file is in use.");
            File.Delete(path);
        }

        await _f.Removed(Erase).EraseAsync([_f.In("Phone", Held), _f.In("Backup", Held)]);

        await Assert.That((await _f.Batches.RecentAsync()).Select(b => b.Id)).IsEquivalentTo(new[] { partly, untouched, moved }, CollectionOrdering.Any);
        await Assert.That((await _f.Mover().UndoAsync(erased)).Found).IsFalse();
        // What is left of the other can still be put back.
        var undone = await _f.Mover().UndoAsync(partly);
        await Assert.That((undone.Restored, undone.Skipped, File.Exists(_f.In("Backup", "IMG_4.JPG")))).IsEqualTo((1, 1, true));
    }
}
