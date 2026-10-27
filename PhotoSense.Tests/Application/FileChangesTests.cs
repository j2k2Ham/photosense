using PhotoSense.Application.Changes;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Application;

public sealed class FileChangesTests : IDisposable
{
    private readonly ChangesFixture _f = new();

    public void Dispose() => _f.Dispose();

    private async Task<OrganizeBatch> DoneAsync(OrganizeBatch batch)
    {
        await Assert.That((await _f.Changes.CarryOutAsync(batch)).Done).IsTrue();
        return batch;
    }

    // A change written down as begun, as a stop of the service would leave it.
    private async Task<OrganizeBatch> CutShortAsync(OrganizeBatch batch, ChangeState state = ChangeState.Started)
    {
        batch.State = state;
        await _f.Batches.SaveAsync(batch);
        return batch;
    }

    // ---- working out where files go

    [Test]
    public async Task A_Place_Is_The_One_Asked_For_Or_Its_Name_Numbered_When_A_File_Or_Another_File_Of_The_Change_Has_It()
    {
        _f.Put("There/IMG_1.JPG");
        var taken = new HashSet<string>(PhotoPath.Comparer);

        await Assert.That(_f.Changes.FreePlace(_f.In("There", "IMG_2.JPG"), taken)).IsEqualTo(_f.In("There", "IMG_2.JPG"));
        await Assert.That(_f.Changes.FreePlace(_f.In("There", "IMG_1.JPG"), taken)).IsEqualTo(_f.In("There", "IMG_1 (1).JPG"));
        await Assert.That(_f.Changes.FreePlace(_f.In("There", "IMG_2.JPG"), taken)).IsEqualTo(_f.In("There", "IMG_2 (1).JPG"));
        await Assert.That(_f.Changes.FreePlace(_f.In("There", "IMG_1.JPG"), taken)).IsEqualTo(_f.In("There", "IMG_1 (2).JPG"));
        await Assert.That(taken.Count).IsEqualTo(4);
    }

    [Test]
    public async Task The_Folders_To_Make_Are_Those_Not_There_Yet_Each_Once_And_Deepest_First()
    {
        _f.Put("There/old.txt");
        var folders = new List<string>();

        _f.Changes.NeedFolder(folders, _f.In("There", "2024", "May"));
        _f.Changes.NeedFolder(folders, _f.In("There", "2024", "June"));
        _f.Changes.NeedFolder(folders, _f.In("There"));

        await Assert.That(folders).IsEquivalentTo(new[] { _f.In("There", "2024", "May"), _f.In("There", "2024"), _f.In("There", "2024", "June") }, CollectionOrdering.Matching);
    }

    // ---- carrying a change out

    [Test]
    public async Task A_Change_Is_Written_Down_Before_Any_File_Moves_And_Again_Once_Done()
    {
        var a = _f.Put("IMG_1.JPG", "one");
        var b = _f.Put("IMG_2.JPG", "three");
        var record = await _f.RecordAsync(a);
        var batch = _f.Change(moves: [(a, _f.In("There", "2024", "IMG_1.JPG")), (b, _f.In("There", "2024", "IMG_2.JPG"))]);
        var states = new List<ChangeState?>();
        _f.Disk.BeforeMove = _ => states.Add(_f.Batches.GetAsync(batch.Id).Result?.State);

        var result = await _f.Changes.CarryOutAsync(batch);

        await Assert.That((result.Done, result.Problem, result.Left.Count)).IsEqualTo((true, (string?)null, 0));
        await Assert.That(states).IsEquivalentTo(new ChangeState?[] { ChangeState.Started, ChangeState.Started }, CollectionOrdering.Matching);
        await Assert.That(_f.Files("There", "2024")).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG" }, CollectionOrdering.Matching);
        var kept = (await _f.Batches.GetAsync(batch.Id))!;
        await Assert.That((kept.State, kept.Undone, kept.Files, kept.Bytes, kept.Items[0].SizeBytes)).IsEqualTo((ChangeState.Done, false, 2, 8L, 3L));
        await Assert.That(kept.Items[0].ModifiedUtcTicks).IsEqualTo(File.GetLastWriteTimeUtc(_f.In("There", "2024", "IMG_1.JPG")).Ticks);
        // The scan's record follows its file.
        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(_f.In("There", "2024", "IMG_1.JPG"));
    }

    [Test]
    public async Task When_One_File_Cannot_Move_Those_That_Moved_Go_Back_And_So_Do_The_Folders_Made_For_Them()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var c = _f.Put("IMG_3.JPG");
        var record = await _f.RecordAsync(a);
        var batch = _f.Change(moves: [(a, _f.In("There", "IMG_1.JPG")), (b, _f.In("There", "IMG_2.JPG")), (c, _f.In("There", "IMG_3.JPG"))]);
        _f.Disk.Fail("move", b);

        var result = await _f.Changes.CarryOutAsync(batch);

        await Assert.That(result.Done).IsFalse();
        await Assert.That(result.Problem).IsEqualTo("IMG_2.JPG could not be moved: The disk would not move IMG_2.JPG. Nothing was moved: every file is where it was.");
        await Assert.That(File.Exists(a) && File.Exists(b) && File.Exists(c)).IsTrue();
        await Assert.That(Directory.Exists(_f.In("There"))).IsFalse();
        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(a);
        var kept = (await _f.Batches.GetAsync(batch.Id))!;
        await Assert.That((kept.State, kept.Undone)).IsEqualTo((ChangeState.Done, true));
        await Assert.That(await _f.Batches.RecentAsync()).IsEmpty();
    }

    [Test]
    public async Task A_Reason_The_Disk_Gives_Without_A_Full_Stop_Is_Still_Told_As_A_Sentence()
    {
        var a = _f.Put("IMG_1.JPG");
        _f.Disk.Fail("move", p => p == a, "Access denied ");

        var result = await _f.Changes.CarryOutAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG"))]));

        await Assert.That(result.Problem).IsEqualTo("IMG_1.JPG could not be moved: Access denied. Nothing was moved: every file is where it was.");
    }

    [Test]
    public async Task A_Copy_That_Fails_Part_Of_The_Way_Leaves_No_Copy_Behind()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var batch = _f.Change(copy: true, moves: [(a, _f.In("Copies", "IMG_1.JPG")), (b, _f.In("Copies", "IMG_2.JPG"))]);
        _f.Disk.Fail("copy", b);

        var result = await _f.Changes.CarryOutAsync(batch);

        await Assert.That(result.Problem).IsEqualTo("IMG_2.JPG could not be copied: The disk would not copy IMG_2.JPG. Nothing was copied, and no copy was left behind.");
        await Assert.That(Directory.Exists(_f.In("Copies"))).IsFalse();
        await Assert.That(File.Exists(a) && File.Exists(b)).IsTrue();
    }

    [Test]
    public async Task A_Removal_That_Fails_Part_Of_The_Way_Keeps_Every_File_And_Every_Record()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var first = await _f.RecordAsync(a);
        var second = await _f.RecordAsync(b);
        var batch = _f.Change(removed: true, moves: [(a, _f.In("_PhotoSense_Removed", "IMG_1.JPG")), (b, _f.In("_PhotoSense_Removed", "IMG_2.JPG"))]);
        _f.Disk.Fail("move", b);

        var result = await _f.Changes.CarryOutAsync(batch);

        await Assert.That(result.Problem).IsEqualTo("IMG_2.JPG could not be moved: The disk would not move IMG_2.JPG. Nothing was removed: every file is where it was.");
        await Assert.That(await _f.Repo.GetAsync(first.Id)).IsNotNull();
        await Assert.That(await _f.Repo.GetAsync(second.Id)).IsNotNull();
        await Assert.That(File.Exists(a)).IsTrue();
    }

    [Test]
    public async Task A_Folder_That_Cannot_Be_Made_Stops_The_Change_Before_Any_File_Moves()
    {
        var a = _f.Put("IMG_1.JPG");
        var batch = _f.Change(moves: [(a, _f.In("There", "2024", "IMG_1.JPG"))]);
        _f.Disk.Fail("make", _f.In("There", "2024"));

        var result = await _f.Changes.CarryOutAsync(batch);

        await Assert.That(result.Problem).IsEqualTo($"The folder 2024 could not be made in {_f.In("There")}: The disk would not make 2024. Nothing was moved: every file is where it was.");
        await Assert.That(File.Exists(a)).IsTrue();
        // The folder made before it goes again.
        await Assert.That(Directory.Exists(_f.In("There"))).IsFalse();
    }

    [Test]
    public async Task A_File_That_Cannot_Go_Back_Leaves_The_Change_To_Be_Put_Back_At_The_Next_Start()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var record = await _f.RecordAsync(a);
        var batch = _f.Change(moves: [(a, _f.In("There", "IMG_1.JPG")), (b, _f.In("There", "IMG_2.JPG"))]);
        _f.Disk.Fail("move", b).Fail("move", _f.In("There", "IMG_1.JPG"));

        var result = await _f.Changes.CarryOutAsync(batch);

        await Assert.That(result.Problem).IsEqualTo("IMG_1.JPG could not be put back where it was: The disk would not move IMG_1.JPG. PhotoSense puts it back the next time it starts.");
        await Assert.That((await _f.Batches.UnfinishedAsync()).Single().Id).IsEqualTo(batch.Id);

        _f.Disk.Heal();
        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).IsEqualTo("Moving files to There was cut short when PhotoSense stopped. Every file is back where it was.");
        await Assert.That(File.Exists(a) && File.Exists(b)).IsTrue();
        await Assert.That(Directory.Exists(_f.In("There"))).IsFalse();
        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(a);
        await Assert.That(await _f.Batches.UnfinishedAsync()).IsEmpty();
        await Assert.That((_f.Audited.Single().Action, _f.Audited[0].Details)).IsEqualTo(("ChangeRecovered", said[0]));
    }

    [Test]
    public async Task Cancelling_Part_Of_The_Way_Puts_Back_What_Had_Moved()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var batch = _f.Change(moves: [(a, _f.In("There", "IMG_1.JPG")), (b, _f.In("There", "IMG_2.JPG"))]);
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _f.Changes.CarryOutAsync(batch, _ => { cancel.Cancel(); return true; }, cancel.Token));

        await Assert.That(File.Exists(a) && File.Exists(b)).IsTrue();
        await Assert.That((await _f.Batches.GetAsync(batch.Id))!.Undone).IsTrue();
    }

    [Test]
    public async Task A_File_Let_Go_Of_Goes_Straight_Back_And_Its_Companions_Stay_With_It()
    {
        var a = _f.Put("IMG_1.HEIC");
        var video = _f.Put("IMG_1.MOV");
        var b = _f.Put("IMG_2.HEIC");
        var sidecar = _f.Put("IMG_2.AAE");
        var first = await _f.RecordAsync(a);
        var second = await _f.RecordAsync(b);
        var batch = _f.Change(removed: true, moves: [(a, _f.In("_PhotoSense_Removed", "IMG_1.HEIC")), (video, _f.In("_PhotoSense_Removed", "IMG_1.MOV")),
            (b, _f.In("_PhotoSense_Removed", "IMG_2.HEIC")), (sidecar, _f.In("_PhotoSense_Removed", "IMG_2.AAE"))]);
        batch.Items[1].Companion = batch.Items[3].Companion = true;
        var asked = new List<string>();

        var result = await _f.Changes.CarryOutAsync(batch, item => { asked.Add(Path.GetFileName(item.From)); return item.From != a; });

        await Assert.That(result.Done).IsTrue();
        await Assert.That(result.Left.Select(i => Path.GetFileName(i.From))).IsEquivalentTo(new[] { "IMG_1.HEIC", "IMG_1.MOV" }, CollectionOrdering.Matching);
        await Assert.That(asked).IsEquivalentTo(new[] { "IMG_1.HEIC", "IMG_2.HEIC" }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(a) && File.Exists(video)).IsTrue();
        await Assert.That(_f.Files("_PhotoSense_Removed")).IsEquivalentTo(new[] { "IMG_2.AAE", "IMG_2.HEIC" }, CollectionOrdering.Matching);
        var kept = (await _f.Batches.GetAsync(batch.Id))!;
        await Assert.That((kept.Items.Count, kept.Files)).IsEqualTo((2, 1));
        // The record of the file let go of stays; the other's goes, kept for an undo.
        await Assert.That(await _f.Repo.GetAsync(first.Id)).IsNotNull();
        await Assert.That(await _f.Repo.GetAsync(second.Id)).IsNull();
        await Assert.That((await _f.Batches.RecordsAsync(batch.Id)).Select(r => r.Id)).Contains(second.Id);
    }

    [Test]
    public async Task A_Change_That_Lets_Go_Of_Every_File_Leaves_Nothing_To_Undo()
    {
        var a = _f.Put("IMG_1.JPG");
        var batch = _f.Change(moves: [(a, _f.In("There", "IMG_1.JPG"))]);

        var result = await _f.Changes.CarryOutAsync(batch, _ => false);

        await Assert.That((result.Done, result.Left.Count)).IsEqualTo((true, 1));
        await Assert.That(File.Exists(a)).IsTrue();
        await Assert.That(Directory.Exists(_f.In("There"))).IsFalse();
        await Assert.That(await _f.Batches.RecentAsync()).IsEmpty();
    }

    [Test]
    public async Task A_Copy_Leaves_The_Records_Where_They_Are()
    {
        var a = _f.Put("IMG_1.JPG");
        var record = await _f.RecordAsync(a);

        await DoneAsync(_f.Change(copy: true, moves: [(a, _f.In("Copies", "IMG_1.JPG"))]));

        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(a);
        await Assert.That(File.Exists(a) && File.Exists(_f.In("Copies", "IMG_1.JPG"))).IsTrue();
    }

    [Test]
    public async Task A_Removal_Of_Files_No_Scan_Recorded_Keeps_No_Records()
    {
        var batch = await DoneAsync(_f.Change(removed: true, moves: [(_f.Put("IMG_1.JPG"), _f.In("_PhotoSense_Removed", "IMG_1.JPG"))]));
        await Assert.That(await _f.Batches.RecordsAsync(batch.Id)).IsEmpty();
    }

    // ---- undoing it

    [Test]
    public async Task Undoing_A_Move_Puts_Each_File_And_Its_Record_Back_And_Takes_Away_The_Folders_It_Made()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var record = await _f.RecordAsync(a);
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "2024", "IMG_1.JPG")), (b, _f.In("There", "2024", "IMG_2.JPG"))]));

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That((undone.Found, undone.Done, undone.Copies, undone.Back.Count, undone.Left.Count, undone.Problem)).IsEqualTo((true, true, false, 2, 0, (string?)null));
        await Assert.That(File.Exists(a) && File.Exists(b)).IsTrue();
        await Assert.That(Directory.Exists(_f.In("There"))).IsFalse();
        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(a);
        await Assert.That((await _f.Batches.GetAsync(batch.Id))!.Undone).IsTrue();
        // Once undone, it is not undone again.
        await Assert.That((await _f.Changes.UndoAsync(batch.Id)).Found).IsFalse();
    }

    [Test]
    public async Task A_File_Goes_Back_Under_Its_Name_With_A_Number_When_Another_File_Has_Taken_Its_Place()
    {
        var a = _f.Put("IMG_1.JPG", "mine");
        var record = await _f.RecordAsync(a);
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG"))]));
        _f.Put("IMG_1.JPG", "someone else's");

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That(undone.Back.Single().Back).IsEqualTo(_f.In("IMG_1 (1).JPG"));
        await Assert.That(File.ReadAllText(_f.In("IMG_1 (1).JPG"))).IsEqualTo("mine");
        await Assert.That(File.ReadAllText(a)).IsEqualTo("someone else's");
        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(_f.In("IMG_1 (1).JPG"));
    }

    [Test]
    public async Task Undoing_Makes_Again_The_Folder_A_File_Came_From_When_It_Has_Gone()
    {
        var a = _f.Put("Old/Deep/IMG_1.JPG");
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG"))]));
        Directory.Delete(_f.In("Old"), recursive: true);

        await Assert.That((await _f.Changes.UndoAsync(batch.Id)).Done).IsTrue();

        await Assert.That(File.Exists(a)).IsTrue();
    }

    [Test]
    public async Task Undoing_Leaves_A_File_That_Is_No_Longer_Where_It_Was_Put_And_Says_So()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG")), (b, _f.In("There", "IMG_2.JPG"))]));
        File.Delete(_f.In("There", "IMG_1.JPG"));

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That(undone.Left.Single()).IsEqualTo("IMG_1.JPG is no longer where it was put.");
        await Assert.That(undone.Back.Single().From).IsEqualTo(b);
        await Assert.That(File.Exists(b)).IsTrue();
    }

    [Test]
    public async Task Undoing_When_Nothing_Is_Left_To_Put_Back_Still_Ends_The_Change()
    {
        var a = _f.Put("IMG_1.JPG");
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG"))]));
        File.Delete(_f.In("There", "IMG_1.JPG"));

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That((undone.Done, undone.Back.Count, undone.Left.Count)).IsEqualTo((true, 0, 1));
        await Assert.That((await _f.Batches.GetAsync(batch.Id))!.Undone).IsTrue();
    }

    [Test]
    public async Task Undoing_A_Copy_Deletes_The_Copies_Still_As_Written_While_What_They_Copy_Is_There()
    {
        var a = _f.Put("IMG_1.JPG", "a");
        var b = _f.Put("IMG_2.JPG", "b");
        var c = _f.Put("IMG_3.JPG", "c");
        var d = _f.Put("IMG_4.JPG", "d");
        var batch = await DoneAsync(_f.Change(copy: true, moves: [(a, _f.In("Copies", "IMG_1.JPG")), (b, _f.In("Copies", "IMG_2.JPG")), (c, _f.In("Copies", "IMG_3.JPG")), (d, _f.In("Copies", "IMG_4.JPG"))]));
        // One copy has been worked on since, one has been touched, and one's original has gone.
        File.WriteAllText(_f.In("Copies", "IMG_2.JPG"), "edited");
        File.SetLastWriteTimeUtc(_f.In("Copies", "IMG_3.JPG"), DateTime.UtcNow.AddMinutes(5));
        File.Delete(d);

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That((undone.Copies, undone.Back.Single().To)).IsEqualTo((true, _f.In("Copies", "IMG_1.JPG")));
        await Assert.That(undone.Left).IsEquivalentTo(new[]
        {
            "IMG_4.JPG was left: the file it was copied from is no longer there.",
            "IMG_3.JPG has changed since it was copied, so it was left.",
            "IMG_2.JPG has changed since it was copied, so it was left.",
        }, CollectionOrdering.Matching);
        await Assert.That(_f.Files("Copies")).IsEquivalentTo(new[] { "IMG_2.JPG", "IMG_3.JPG", "IMG_4.JPG" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(a)).IsEqualTo("a");
    }

    [Test]
    public async Task Undoing_A_Removal_Brings_Back_The_Files_And_What_Scans_Recorded_Of_Them()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("Sub/IMG_2.JPG");
        var c = _f.Put("IMG_3.JPG");
        var first = await _f.RecordAsync(a);
        var second = await _f.RecordAsync(b);
        var batch = await DoneAsync(_f.Change(removed: true, moves: [(a, _f.In("_PhotoSense_Removed", "IMG_1.JPG")), (b, _f.In("_PhotoSense_Removed", "Sub", "IMG_2.JPG")), (c, _f.In("_PhotoSense_Removed", "IMG_3.JPG"))]));
        await Assert.That(await _f.Repo.GetAllAsync()).IsEmpty();
        _f.Put("Sub/IMG_2.JPG", "a new picture under the old name");

        await Assert.That((await _f.Changes.UndoAsync(batch.Id)).Done).IsTrue();

        await Assert.That((await _f.Repo.GetAsync(first.Id))!.SourcePath).IsEqualTo(a);
        await Assert.That((await _f.Repo.GetAsync(second.Id))!.SourcePath).IsEqualTo(_f.In("Sub", "IMG_2 (1).JPG"));
        await Assert.That((await _f.Repo.GetAllAsync()).Count).IsEqualTo(2);
        await Assert.That(File.Exists(c)).IsTrue();
        await Assert.That(Directory.Exists(_f.In("_PhotoSense_Removed"))).IsFalse();
    }

    [Test]
    public async Task An_Undo_That_Cannot_Put_One_File_Back_Puts_None_Back_And_Can_Be_Tried_Again()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG")), (b, _f.In("There", "IMG_2.JPG"))]));
        // An undo goes last file first: the second goes back, then the first cannot.
        _f.Disk.Fail("move", _f.In("There", "IMG_1.JPG"));

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That((undone.Found, undone.Done, undone.Back.Count)).IsEqualTo((true, false, 2));
        await Assert.That(undone.Problem).IsEqualTo("IMG_1.JPG could not be put back: The disk would not move IMG_1.JPG. Nothing was put back: every file is where it is.");
        await Assert.That(_f.Files("There")).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG" }, CollectionOrdering.Matching);
        var kept = (await _f.Batches.GetAsync(batch.Id))!;
        await Assert.That((kept.State, kept.Undone, kept.Items.TrueForAll(i => i.Back is null))).IsEqualTo((ChangeState.Done, false, true));

        _f.Disk.Heal();
        await Assert.That((await _f.Changes.UndoAsync(batch.Id)).Done).IsTrue();
        await Assert.That(File.Exists(a) && File.Exists(b)).IsTrue();
    }

    [Test]
    public async Task An_Undo_That_Cannot_Make_A_Folder_Again_Puts_Nothing_Back()
    {
        var a = _f.Put("Old/IMG_1.JPG");
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG"))]));
        Directory.Delete(_f.In("Old"));
        _f.Disk.Fail("make", _f.In("Old"));

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That(undone.Problem).IsEqualTo($"The folder Old could not be made in {_f.Root.FullName}: The disk would not make Old. Nothing was put back: every file is where it is.");
        await Assert.That(File.Exists(_f.In("There", "IMG_1.JPG"))).IsTrue();
    }

    [Test]
    public async Task An_Undo_Of_A_Copy_That_Cannot_Delete_One_Copy_Deletes_None()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var batch = await DoneAsync(_f.Change(copy: true, moves: [(a, _f.In("Copies", "IMG_1.JPG")), (b, _f.In("Copies", "IMG_2.JPG"))]));
        _f.Disk.Fail("move", _f.In("Copies", "IMG_1.JPG"));

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That(undone.Problem).IsEqualTo("The copy IMG_1.JPG could not be deleted: The disk would not move IMG_1.JPG. No copy was deleted.");
        await Assert.That(_f.Files("Copies")).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Copy_That_Cannot_Be_Deleted_Once_Set_Aside_Is_Left_Under_Its_Own_Name_Where_It_Can_Be()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var batch = await DoneAsync(_f.Change(copy: true, moves: [(a, _f.In("Copies", "IMG_1.JPG")), (b, _f.In("Copies", "IMG_2.JPG"))]));
        _f.Disk.Fail("delete", p => Path.GetFileName(p).StartsWith('.'));
        // The second copy cannot even be given its name back, and stays hidden.
        _f.Disk.Fail("move", p => Path.GetFileName(p).StartsWith(".IMG_2", StringComparison.Ordinal));

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That((undone.Done, undone.Back.Count, undone.Left.Count)).IsEqualTo((true, 0, 2));
        await Assert.That(undone.Left[0]).IsEqualTo($"IMG_2.JPG could not be deleted, so the copy was left: The disk would not delete .IMG_2.JPG.{batch.Id:N}.undo.");
        await Assert.That(_f.Files("Copies")).IsEquivalentTo(new[] { $".IMG_2.JPG.{batch.Id:N}.undo", "IMG_1.JPG" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_Undo_That_Cannot_Return_A_File_Is_Taken_Back_At_The_Next_Start()
    {
        var a = _f.Put("IMG_1.JPG");
        var b = _f.Put("IMG_2.JPG");
        var record = await _f.RecordAsync(b);
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG")), (b, _f.In("There", "IMG_2.JPG"))]));
        // The undo puts the second back, cannot put the first back, and then cannot return the second to where it found it.
        _f.Disk.Fail("move", _f.In("There", "IMG_1.JPG")).Fail("move", b);

        var undone = await _f.Changes.UndoAsync(batch.Id);

        await Assert.That(undone.Problem).IsEqualTo("IMG_2.JPG could not be returned to where the undo found it: The disk would not move IMG_2.JPG. PhotoSense returns it the next time it starts.");
        await Assert.That((await _f.Batches.UnfinishedAsync()).Single().State).IsEqualTo(ChangeState.Undoing);

        _f.Disk.Heal();
        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).IsEqualTo("Undoing \"Moving files to There\" was cut short when PhotoSense stopped. Every file is where it was before the undo.");
        await Assert.That(_f.Files("There")).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG" }, CollectionOrdering.Matching);
        var kept = (await _f.Batches.GetAsync(batch.Id))!;
        await Assert.That((kept.State, kept.Undone, kept.UndoFolders.Count)).IsEqualTo((ChangeState.Done, false, 0));
        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(_f.In("There", "IMG_2.JPG"));
        await Assert.That((await _f.Changes.UndoAsync(batch.Id)).Done).IsTrue();
    }

    [Test]
    public async Task Only_A_Change_Carried_Out_In_Full_And_Not_Undone_Can_Be_Undone()
    {
        await Assert.That((await _f.Changes.UndoAsync(Guid.NewGuid())).Found).IsFalse();
        var started = await CutShortAsync(new OrganizeBatch { Label = "There" });
        await Assert.That((await _f.Changes.UndoAsync(started.Id)).Found).IsFalse();
    }

    [Test]
    public async Task A_Folder_That_Will_Not_Go_Is_Left_And_Holds_Nothing_Else_Up()
    {
        var a = _f.Put("IMG_1.JPG");
        var batch = await DoneAsync(_f.Change(moves: [(a, _f.In("There", "IMG_1.JPG"))]));
        _f.Disk.Fail("remove folder", _f.In("There"));

        await Assert.That((await _f.Changes.UndoAsync(batch.Id)).Done).IsTrue();

        await Assert.That(File.Exists(a)).IsTrue();
        await Assert.That(Directory.Exists(_f.In("There"))).IsTrue();
    }

    // ---- after a stop

    [Test]
    public async Task With_Nothing_Cut_Short_A_Start_Changes_Nothing()
        => await Assert.That(await _f.Changes.RecoverAsync()).IsEmpty();

    [Test]
    public async Task A_Move_Cut_Short_Is_Put_Back_At_The_Next_Start_Whatever_Each_File_Had_Got_To()
    {
        // Moved; not yet moved; copied whole to another disk with the original not yet taken away; part copied;
        // and one whose new place holds a file that is not the one being moved.
        var moved = _f.Put("There/IMG_1.JPG", "one");
        var notYet = _f.Put("IMG_2.JPG", "two");
        var copied = _f.Put("IMG_3.JPG", "three");
        _f.Put("There/IMG_3.JPG", "three");
        var part = _f.Put("IMG_4.JPG", "four");
        _f.Put("There/IMG_4.JPG", "fo");
        var other = _f.Put("IMG_5.JPG", "five");
        _f.Put("There/IMG_5.JPG", "FIVE");
        var record = await _f.RecordAsync(moved);
        var batch = await CutShortAsync(_f.Change(moves: [(_f.In("IMG_1.JPG"), moved), (notYet, _f.In("There", "IMG_2.JPG")), (copied, _f.In("There", "IMG_3.JPG")),
            (part, _f.In("There", "IMG_4.JPG")), (other, _f.In("There", "IMG_5.JPG"))]));

        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).IsEqualTo("Moving files to There was cut short when PhotoSense stopped. Every file is back where it was.");
        await Assert.That(_f.Files()).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG", "IMG_3.JPG", "IMG_4.JPG", "IMG_5.JPG" }, CollectionOrdering.Matching);
        await Assert.That(_f.Files("There")).IsEquivalentTo(new[] { "IMG_5.JPG" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(_f.In("There", "IMG_5.JPG"))).IsEqualTo("FIVE");
        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(_f.In("IMG_1.JPG"));
        await Assert.That((await _f.Batches.GetAsync(batch.Id))!.Undone).IsTrue();
    }

    [Test]
    public async Task A_Copy_Cut_Short_Leaves_None_Of_What_It_Wrote()
    {
        var whole = _f.Put("IMG_1.JPG", "one");
        _f.Put("Copies/IMG_1.JPG", "one");
        var part = _f.Put("IMG_2.JPG", "two");
        _f.Put("Copies/IMG_2.JPG", "t");
        var notYet = _f.Put("IMG_3.JPG", "three");
        // Its original has gone since, so what is there may be the only copy now.
        _f.Put("Copies/IMG_4.JPG", "four");
        await CutShortAsync(_f.Change(copy: true, moves: [(whole, _f.In("Copies", "IMG_1.JPG")), (part, _f.In("Copies", "IMG_2.JPG")), (notYet, _f.In("Copies", "IMG_3.JPG")),
            (_f.In("IMG_4.JPG"), _f.In("Copies", "IMG_4.JPG"))]));

        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).IsEqualTo("Copying files to There was cut short when PhotoSense stopped. The copies it had made so far were deleted.");
        await Assert.That(_f.Files("Copies")).IsEquivalentTo(new[] { "IMG_4.JPG" }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(whole) && File.Exists(part) && File.Exists(notYet)).IsTrue();
    }

    [Test]
    public async Task A_Removal_Cut_Short_Brings_Back_The_Records_It_Had_Dropped()
    {
        var held = _f.Put("_PhotoSense_Removed/IMG_1.JPG");
        var dropped = new Photo { SourcePath = _f.In("IMG_1.JPG"), FileName = "IMG_1.JPG" };
        var stayed = await _f.RecordAsync(_f.Put("IMG_2.JPG"));
        var batch = _f.Change(removed: true, moves: [(_f.In("IMG_1.JPG"), held), (stayed.SourcePath, _f.In("_PhotoSense_Removed", "IMG_2.JPG"))]);
        batch.CreatedFolders.Add(_f.In("_PhotoSense_Removed"));
        await _f.Batches.KeepRecordsAsync(batch.Id, [dropped, stayed]);
        await CutShortAsync(batch);

        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).IsEqualTo("Deleting files from There was cut short when PhotoSense stopped. Every file is back where it was.");
        await Assert.That((await _f.Repo.GetAsync(dropped.Id))!.SourcePath).IsEqualTo(_f.In("IMG_1.JPG"));
        await Assert.That((await _f.Repo.GetAllAsync()).Count).IsEqualTo(2);
        await Assert.That(File.Exists(_f.In("IMG_1.JPG")) && File.Exists(stayed.SourcePath)).IsTrue();
        await Assert.That(Directory.Exists(_f.In("_PhotoSense_Removed"))).IsFalse();
    }

    [Test]
    public async Task An_Undo_Cut_Short_Is_Taken_Back_Whatever_Each_File_Had_Got_To()
    {
        var batch = _f.Change(moves: [(_f.In("IMG_1.JPG"), _f.In("There", "IMG_1.JPG")), (_f.In("IMG_2.JPG"), _f.In("There", "IMG_2.JPG")),
            (_f.In("IMG_3.JPG"), _f.In("There", "IMG_3.JPG")), (_f.In("IMG_4.JPG"), _f.In("There", "IMG_4.JPG")), (_f.In("IMG_5.JPG"), _f.In("There", "IMG_5.JPG"))]);
        // Put back already; not yet; copied back from another disk and not yet taken away; and one whose old
        // place holds a file that is not it. The fifth was left out of the undo.
        _f.Put("IMG_1.JPG", "one");
        _f.Put("There/IMG_2.JPG", "two");
        _f.Put("IMG_3.JPG", "three");
        _f.Put("There/IMG_3.JPG", "three");
        _f.Put("IMG_4.JPG", "someone else's");
        _f.Put("There/IMG_4.JPG", "four");
        foreach (var item in batch.Items.Take(4)) item.Back = item.From;
        Directory.CreateDirectory(_f.In("Made"));
        batch.UndoFolders.Add(_f.In("Made"));
        var record = await _f.RecordAsync(_f.In("IMG_1.JPG"));
        await CutShortAsync(batch, ChangeState.Undoing);

        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).IsEqualTo("Undoing \"Moving files to There\" was cut short when PhotoSense stopped. Every file is where it was before the undo.");
        await Assert.That(_f.Files("There")).IsEquivalentTo(new[] { "IMG_1.JPG", "IMG_2.JPG", "IMG_3.JPG", "IMG_4.JPG" }, CollectionOrdering.Matching);
        await Assert.That(_f.Files()).IsEquivalentTo(new[] { "IMG_4.JPG" }, CollectionOrdering.Matching);
        await Assert.That((await _f.Repo.GetAsync(record.Id))!.SourcePath).IsEqualTo(_f.In("There", "IMG_1.JPG"));
        await Assert.That(Directory.Exists(_f.In("Made"))).IsFalse();
        var kept = (await _f.Batches.GetAsync(batch.Id))!;
        await Assert.That((kept.State, kept.Undone, kept.Items.TrueForAll(i => i.Back is null), kept.UndoFolders.Count)).IsEqualTo((ChangeState.Done, false, true, 0));
    }

    [Test]
    public async Task An_Undo_Of_A_Removal_Cut_Short_Drops_Again_The_Records_It_Had_Brought_Back()
    {
        var batch = _f.Change(removed: true, moves: [(_f.In("IMG_1.JPG"), _f.In("_PhotoSense_Removed", "IMG_1.JPG"))]);
        var brought = await _f.RecordAsync(_f.Put("IMG_1.JPG"));
        var unrelated = await _f.RecordAsync(_f.Put("IMG_9.JPG"));
        await _f.Batches.KeepRecordsAsync(batch.Id, [brought, unrelated]);
        batch.Items[0].Back = _f.In("IMG_1.JPG");
        Directory.CreateDirectory(_f.In("_PhotoSense_Removed"));
        await CutShortAsync(batch, ChangeState.Undoing);

        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).StartsWith("Undoing \"Deleting files from There\" was cut short");
        await Assert.That(File.Exists(_f.In("_PhotoSense_Removed", "IMG_1.JPG"))).IsTrue();
        await Assert.That(await _f.Repo.GetAsync(brought.Id)).IsNull();
        await Assert.That(await _f.Repo.GetAsync(unrelated.Id)).IsNotNull();
    }

    [Test]
    public async Task An_Undo_Of_A_Copy_Cut_Short_Gives_The_Copies_It_Had_Set_Aside_Their_Names_Back()
    {
        var batch = _f.Change(copy: true, moves: [(_f.In("IMG_1.JPG"), _f.In("Copies", "IMG_1.JPG")), (_f.In("IMG_2.JPG"), _f.In("Copies", "IMG_2.JPG"))]);
        batch.Items[0].Back = _f.Put("Copies/.IMG_1.JPG.x.undo", "one");
        // A file has its name already, so the one set aside is left as it is.
        batch.Items[1].Back = _f.Put("Copies/.IMG_2.JPG.x.undo", "two");
        _f.Put("Copies/IMG_2.JPG", "two");
        await CutShortAsync(batch, ChangeState.Undoing);

        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).StartsWith("Undoing \"Copying files to There\" was cut short");
        await Assert.That(_f.Files("Copies")).IsEquivalentTo(new[] { ".IMG_2.JPG.x.undo", "IMG_1.JPG", "IMG_2.JPG" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task What_Cannot_Be_Put_Back_At_A_Start_Is_Tried_Again_At_The_Next()
    {
        await CutShortAsync(_f.Change(moves: [(_f.In("IMG_1.JPG"), _f.Put("There/IMG_1.JPG"))]));
        _f.Disk.Fail("move", _f.In("There", "IMG_1.JPG"));

        var said = await _f.Changes.RecoverAsync();

        await Assert.That(said.Single()).IsEqualTo("Moving files to There was cut short when PhotoSense stopped, and could not all be put back yet: The disk would not move IMG_1.JPG. PhotoSense tries again each time it starts.");
        await Assert.That((await _f.Batches.UnfinishedAsync()).Count).IsEqualTo(1);

        _f.Disk.Heal();
        await _f.Changes.RecoverAsync();

        await Assert.That(File.Exists(_f.In("IMG_1.JPG"))).IsTrue();
        await Assert.That(await _f.Batches.UnfinishedAsync()).IsEmpty();
    }

    // ---- the disk

    [Test]
    public async Task A_File_Is_A_Copy_Of_Another_When_It_Holds_What_The_Other_Begins_With()
    {
        var bytes = new byte[200_000];
        new Random(7).NextBytes(bytes);
        var changed = bytes.ToArray();
        changed[150_000] ^= 0xFF;
        string Write(string name, byte[] content)
        {
            var path = _f.In(name);
            File.WriteAllBytes(path, content);
            return path;
        }
        var original = Write("original.bin", bytes);

        await Assert.That(Disk.Real.IsCopyOf(Write("whole.bin", bytes), original)).IsTrue();
        await Assert.That(Disk.Real.IsCopyOf(Write("part.bin", bytes[..100_000]), original)).IsTrue();
        await Assert.That(Disk.Real.IsCopyOf(Write("empty.bin", []), original)).IsTrue();
        await Assert.That(Disk.Real.IsCopyOf(Write("longer.bin", [.. bytes, 1]), original)).IsFalse();
        await Assert.That(Disk.Real.IsCopyOf(Write("differs.bin", changed), original)).IsFalse();
    }

    [Test]
    public async Task A_Folder_Is_Taken_Away_Only_When_It_Is_Empty()
    {
        Directory.CreateDirectory(_f.In("empty"));
        _f.Put("full/a.txt");

        Disk.Real.RemoveEmptyFolder(_f.In("empty"));
        Disk.Real.RemoveEmptyFolder(_f.In("full"));
        Disk.Real.RemoveEmptyFolder(_f.In("never there"));

        await Assert.That(Directory.Exists(_f.In("empty"))).IsFalse();
        await Assert.That(File.Exists(_f.In("full", "a.txt"))).IsTrue();
    }
}
