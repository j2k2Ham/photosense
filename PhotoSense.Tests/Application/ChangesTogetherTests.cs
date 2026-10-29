using LiteDB;
using Moq;
using PhotoSense.Application.Changes;
using PhotoSense.Application.Removal;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Application;

/// <summary>
/// Photos in memory, kept by something that takes many changes as one, as a phone's photo library does: what it is
/// handed is all made or none of it is, and the calls that do so can be told to fail.
/// </summary>
internal sealed class TogetherFiles : IPhotoFileSystem, IPhotoChangesTogether
{
    private readonly InMemoryFileSystem _files = new();
    private int _calls;

    /// <summary>The calls of MoveAll, counted from 1, that fail.</summary>
    public HashSet<int> FailingMoves { get; } = [];
    public bool FailErasing { get; set; }
    public bool FailTidying { get; set; }
    public List<IReadOnlyList<FileMove>> Moves { get; } = [];
    public List<IReadOnlyList<string>> Erased { get; } = [];
    public List<IReadOnlyList<string>> Tidied { get; } = [];

    public string Put(string path, string text) => _files.Put(path, text);

    public void MoveAll(IReadOnlyList<FileMove> moves)
    {
        Moves.Add(moves);
        if (FailingMoves.Contains(++_calls)) throw new IOException("The person said no.");
        foreach (var (from, to) in moves)
        {
            _files.MakeFolder(Path.GetDirectoryName(to)!);
            _files.Move(from, to);
        }
    }

    public void EraseAll(IReadOnlyList<string> paths)
    {
        Erased.Add(paths);
        if (FailErasing) throw new IOException("Nothing was deleted.");
        foreach (var path in paths) _files.Erase(path);
    }

    public void RemoveEmptyFolders(IReadOnlyList<string> folders)
    {
        Tidied.Add(folders);
        if (FailTidying) throw new UnauthorizedAccessException("Albums cannot be taken away.");
        foreach (var folder in folders) _files.RemoveEmptyFolder(folder);
    }

    public bool FileExists(string path) => _files.FileExists(path);
    public bool FolderExists(string path) => _files.FolderExists(path);
    public StoredFile? Describe(string path) => _files.Describe(path);
    public IReadOnlyList<string> FilesIn(string folder) => _files.FilesIn(folder);
    public IReadOnlyList<string> FoldersIn(string folder) => _files.FoldersIn(folder);
    public bool IsLink(string path) => false;
    public Stream OpenRead(string path) => _files.OpenRead(path);
    public bool IsCopyOf(string copy, string original) => _files.IsCopyOf(copy, original);
    public void Move(string from, string to) => throw new InvalidOperationException("Moved one at a time.");
    public void Copy(string from, string to) => throw new UnauthorizedAccessException("Never copied.");
    public Stream CreateNew(string path) => _files.CreateNew(path);
    public void MakeFolder(string path) => _files.MakeFolder(path);
    public void RemoveEmptyFolder(string path) => throw new InvalidOperationException("Taken away one at a time.");
    public void DeleteCopy(string path) => throw new InvalidOperationException("Never copied.");
    public void Erase(string path) => throw new InvalidOperationException("Erased one at a time.");
}

/// <summary>Changes, undos and erasing where the photos are kept by something that takes many changes as one.</summary>
public sealed class ChangesTogetherTests : IDisposable
{
    private readonly LiteDatabase _db = new(new MemoryStream());
    private readonly TogetherFiles _files = new();
    private readonly LiteDbOrganizeBatchStore _batches;
    private readonly FileChanges _changes;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "photosense-together-" + Guid.NewGuid().ToString("N"));

    public ChangesTogetherTests()
    {
        _batches = new LiteDbOrganizeBatchStore(_db);
        _changes = new FileChanges(_batches, new InMemoryPhotoRepository(), new Mock<IAuditRepository>().Object, _files);
    }

    public void Dispose() => _db.Dispose();

    private string In(params string[] parts) => Path.Combine([_root, .. parts]);

    // A picture with its Live Photo video, and another picture, set to go to an album.
    private OrganizeBatch Batch(bool removed = false)
    {
        string a = _files.Put(In("IMG_1.JPG"), "one"), video = _files.Put(In("IMG_1.MOV"), "video"), b = _files.Put(In("IMG_2.JPG"), "two!");
        return new OrganizeBatch
        {
            Label = "Trips", Removed = removed, CreatedFolders = [In("Trips")],
            Items = [new() { From = a, To = In("Trips", "IMG_1.JPG") }, new() { From = video, To = In("Trips", "IMG_1.MOV"), Companion = true }, new() { From = b, To = In("Trips", "IMG_2.JPG") }],
        };
    }

    [Test]
    public async Task A_Change_Is_Handed_Over_Whole_And_Undone_Whole_With_The_Folders_It_Made_Taken_Away_Together()
    {
        var batch = Batch();
        var done = await _changes.CarryOutAsync(batch);
        await Assert.That((done.Done, _files.Moves.Count, _files.Moves[0].Count, batch.Files, batch.Bytes)).IsEqualTo((true, 1, 3, 2, 7L));
        await Assert.That(_files.FilesIn(In("Trips")).Count).IsEqualTo(3);

        var undone = await _changes.UndoAsync(batch.Id);
        await Assert.That((undone.Done, undone.Back.Count, _files.Moves.Count)).IsEqualTo((true, 3, 2));
        await Assert.That(_files.Moves[1].Select(m => Path.GetFileName(m.To))).IsEquivalentTo(["IMG_2.JPG", "IMG_1.MOV", "IMG_1.JPG"], CollectionOrdering.Matching);
        await Assert.That((_files.FolderExists(In("Trips")), _files.Tidied.Single().Single())).IsEqualTo((false, In("Trips")));
    }

    [Test]
    public async Task Files_Let_Go_Of_Go_Back_Together_With_Their_Companions()
    {
        var batch = Batch();
        var done = await _changes.CarryOutAsync(batch, item => !item.From.EndsWith("IMG_1.JPG", StringComparison.Ordinal));
        await Assert.That((done.Done, done.Left.Count, batch.Items.Single().To)).IsEqualTo((true, 2, In("Trips", "IMG_2.JPG")));
        await Assert.That(_files.Moves[1].Select(m => Path.GetFileName(m.From))).IsEquivalentTo(["IMG_1.JPG", "IMG_1.MOV"], CollectionOrdering.Matching);
        await Assert.That((_files.FileExists(In("IMG_1.JPG")), _files.FileExists(In("IMG_1.MOV")))).IsEqualTo((true, true));
        // Letting every file go changes nothing: there is nothing to undo, and the folder made for them goes.
        var none = Batch();
        none.Items.ForEach(i => i.From = i.From.Replace("IMG_", "NEW_", StringComparison.Ordinal));
        foreach (var item in none.Items) _files.Put(item.From, "new");
        none.Items.ForEach(i => i.To = i.To.Replace("IMG_", "NEW_", StringComparison.Ordinal));
        await Assert.That((await _changes.CarryOutAsync(none, _ => false)).Done).IsTrue();
        await Assert.That(none.Undone).IsTrue();
    }

    [Test]
    public async Task When_The_Whole_Is_Refused_Nothing_Moves_And_The_Files_Are_Spoken_Of_Together()
    {
        _files.FailingMoves.Add(1);
        var moved = await _changes.CarryOutAsync(Batch());
        await Assert.That((moved.Done, moved.Problem)).IsEqualTo((false, "The files could not be moved: The person said no. Nothing was moved: every file is where it was."));

        _files.FailingMoves.Add(2);
        var removed = await _changes.CarryOutAsync(Batch(removed: true));
        await Assert.That(removed.Problem).IsEqualTo("The files could not be removed: The person said no. Nothing was removed: every file is where it was.");
        await Assert.That(_files.FileExists(In("IMG_1.JPG"))).IsTrue();
    }

    [Test]
    public async Task When_Letting_Go_Fails_Everything_Is_Put_Back_Together_Or_Said_To_Be_Put_Back_At_The_Next_Start()
    {
        // The files go; letting one go is refused; putting all of them back works.
        _files.FailingMoves.Add(2);
        var first = await _changes.CarryOutAsync(Batch(), item => !item.From.EndsWith("IMG_2.JPG", StringComparison.Ordinal));
        await Assert.That((first.Done, first.Problem)).IsEqualTo((false, "The files could not be moved: The person said no. Nothing was moved: every file is where it was."));
        await Assert.That((_files.FileExists(In("IMG_1.JPG")), _files.Moves[2].Count)).IsEqualTo((true, 3));

        // And when putting them back is refused too, the change is left begun, to be put back at the next start.
        _files.FailingMoves.UnionWith([5, 6]);
        var batch = Batch();
        var second = await _changes.CarryOutAsync(batch, item => !item.From.EndsWith("IMG_2.JPG", StringComparison.Ordinal));
        await Assert.That(second.Problem).IsEqualTo("The files could not be put back where they were: The person said no. PhotoSense puts them back the next time it starts.");
        await Assert.That((await _batches.GetAsync(batch.Id))!.State).IsEqualTo(ChangeState.Started);
    }

    [Test]
    public async Task An_Undo_Refused_Puts_Nothing_Back_And_Folders_That_Will_Not_Go_Stay()
    {
        var batch = Batch();
        await _changes.CarryOutAsync(batch);
        _files.FailingMoves.Add(2);
        var refused = await _changes.UndoAsync(batch.Id);
        await Assert.That((refused.Done, refused.Problem)).IsEqualTo((false, "The files could not be put back: The person said no. Nothing was put back: every file is where it is."));
        await Assert.That(_files.FilesIn(In("Trips")).Count).IsEqualTo(3);

        _files.FailTidying = true;
        await Assert.That((await _changes.UndoAsync(batch.Id)).Done).IsTrue();
        await Assert.That(_files.FolderExists(In("Trips"))).IsTrue();
    }

    [Test]
    public async Task Delete_Permanently_Erases_Each_Folder_Of_Removed_Files_In_One_Go_And_Says_When_It_Could_Not()
    {
        var removed = In("_PhotoSense_Removed");
        _files.Put(Path.Combine(removed, "IMG_1.JPG"), "one");
        _files.Put(Path.Combine(removed, "Trips", "IMG_2.JPG"), "two!");
        var erasing = new RemovedFiles(new InMemoryPhotoRepository(), _batches, new Mock<IAuditRepository>().Object, _files);

        _files.FailErasing = true;
        var refused = await erasing.EraseAsync([removed]);
        await Assert.That((refused.Erased, refused.Skipped, refused.Problems.Single())).IsEqualTo((0, 2, "Nothing was deleted."));

        _files.FailErasing = false;
        _files.FailTidying = true;
        var erased = await erasing.EraseAsync([removed]);
        await Assert.That((erased.Erased, erased.Bytes, erased.Skipped, _files.Erased.Count, _files.Erased[1].Count)).IsEqualTo((2, 7L, 0, 2, 2));
        await Assert.That(_files.FolderExists(removed)).IsTrue();
        await Assert.That(_files.Tidied[^1]).IsEquivalentTo([Path.Combine(removed, "Trips"), removed], CollectionOrdering.Matching);
    }
}
