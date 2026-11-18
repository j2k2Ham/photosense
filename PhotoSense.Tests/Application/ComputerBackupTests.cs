using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Tests.Endpoints;
using System.Text.Json.Nodes;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Tests.Application;

/// <summary>
/// The computer's half of a backup, against a phone of the test's own that says what it likes: files the computer
/// holds already, long lists, broken messages and damaged files. The two talk over this computer's own network.
/// </summary>
public sealed class ComputerBackupTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("photosense-computer-backup-");
    private readonly MemoryBackupStore _store = new();
    private readonly ComputerBackup _backup;
    private readonly Device _phone = new("phone-1", "Jamie's iPhone");
    private readonly List<IDisposable> _open = [];

    public ComputerBackupTests() => _backup = TestBackups.Computer(Folder("Home", "Pictures", "PhotoSense"), _store, home: Folder("Home"));

    public void Dispose()
    {
        foreach (var open in _open) open.Dispose();
        TestFiles.Remove(_data);
    }

    private string Folder(params string[] names) => Path.Combine([_data.FullName, .. names]);

    private string Backups => Folder("Home", "Pictures", "PhotoSense", "Jamie's iPhone");

    /// <summary>The computer serving the phone, and the phone's end, each sealed with keys of the test's own; the phone's connection, to hang up.</summary>
    private async Task<(Task Serving, SecureChannel Phone, Stream Line)> ConnectedAsync(CancellationToken ct, ComputerBackup? backup = null)
    {
        var (near, far) = await Loopback.PairAsync(ct);
        var (one, two) = (RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        var computer = new SecureChannel(near, one, two);
        var phone = new SecureChannel(far, two, one);
        _open.AddRange([near, far, computer, phone]);
        return ((backup ?? _backup).ServeAsync(computer, _phone, ct), phone, far);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static JsonObject File(string id, string name, byte[] bytes, bool sayChecksum = true)
    {
        var file = new JsonObject { ["id"] = id, ["name"] = name, ["size"] = bytes.Length };
        if (sayChecksum) file["sha256"] = Sha(bytes);
        return file;
    }

    /// <summary>Reads every part of the list of checksums the computer holds, and the folder said with the last.</summary>
    private static async Task<(List<string> Held, string Folder)> HeldAsync(SecureChannel phone)
    {
        var held = new List<string>();
        while (true)
        {
            var part = await phone.ReceiveAsync(CancellationToken.None, "held");
            held.AddRange(((JsonArray)part["sha256"]!).Select(n => (string)n!));
            if (!(bool)part["more"]!) return (held, (string)part["folder"]!);
        }
    }

    private static async Task SendFileAsync(SecureChannel phone, string id, byte[] bytes, string? endCheck = null, long? size = null)
    {
        var get = await phone.ReceiveAsync(CancellationToken.None, "get");
        await Assert.That(Wire.Text(get, "id")).IsEqualTo(id);
        await phone.SendAsync(new JsonObject { ["t"] = "file", ["id"] = id, ["size"] = size ?? bytes.Length }, CancellationToken.None);
        await phone.SendDataAsync(bytes, CancellationToken.None);
        await phone.SendAsync(new JsonObject { ["t"] = "end", ["id"] = id, ["sha256"] = endCheck ?? Sha(bytes) }, CancellationToken.None);
    }

    [Test]
    public async Task Long_Lists_Go_In_Parts_And_What_The_Computer_Holds_Is_Neither_Asked_For_Nor_Written_Twice(CancellationToken ct)
    {
        var there = Directory.CreateDirectory(Backups).FullName;
        byte[] kept = [1, 2, 3, 4];
        var path = Path.Combine(there, "IMG_0001.JPG");
        await System.IO.File.WriteAllBytesAsync(path, kept, ct);
        var info = new FileInfo(path);
        await _store.SavePhoneAsync(new BackupPhone(_phone.Id, "Old name", "iPhone", there, false, TestClock.Day, null, null, 7, 700), ct);
        await _store.HoldAsync(new HeldFile(_phone.Id, Sha(kept), path, info.Length, info.LastWriteTimeUtc, TestClock.Day), ct);
        // More checksums than one message holds, all of one file as it was: none needs reading again.
        for (var n = 0; n < ComputerBackup.ListPart; n++)
            await _store.HoldAsync(new HeldFile(_phone.Id, $"other-{n:0000}", path, info.Length, info.LastWriteTimeUtc, TestClock.Day), ct);

        var (serving, phone, line) = await ConnectedAsync(ct);
        await phone.SendAsync(new JsonObject { ["t"] = "backup" }, ct);
        var (held, folder) = await HeldAsync(phone);
        await Assert.That((held.Count, held.Contains(Sha(kept)), folder)).IsEqualTo((ComputerBackup.ListPart + 1, true, "Pictures/PhotoSense/Jamie's iPhone"));

        // The offer in three parts, one with no files at all: the first file said by its checksum, the second only by its bytes.
        await phone.SendAsync(new JsonObject { ["t"] = "offer", ["files"] = new JsonArray(File("f1", "IMG_0001.JPG", kept)), ["more"] = true }, ct);
        await phone.SendAsync(new JsonObject { ["t"] = "offer", ["more"] = true }, ct);
        // A checksum that is not one is as none.
        var copy = File("f2", "IMG_0001 copy.JPG", kept, sayChecksum: false);
        copy["sha256"] = 5;
        await phone.SendAsync(new JsonObject { ["t"] = "offer", ["files"] = new JsonArray(copy), ["more"] = "no" }, ct);
        await Assert.That(Wire.Text(await phone.ReceiveAsync(ct, "got"), "id")).IsEqualTo("f1");
        // Phones shows the backup arriving, with what came last.
        var listed = (await new BackupApi(new TestApi(s => s.AddSingleton(_backup))).PhonesAsync(Http.Get("backup/phones").FromClient(), ct)).Json()[0].GetProperty("arriving");
        await Assert.That((listed.GetProperty("done").GetInt32(), listed.GetProperty("total").GetInt32(), listed.GetProperty("arrived").GetArrayLength())).IsEqualTo((1, 2, 1));
        await SendFileAsync(phone, "f2", kept);
        var got = await phone.ReceiveAsync(ct, "got");
        await Assert.That((Wire.Text(got, "id"), Wire.Text(got, "sha256"))).IsEqualTo(("f2", Sha(kept)));
        await phone.ReceiveAsync(ct, "done");
        // Said goodbye with nothing more: what the phone said before stands.
        await phone.SendAsync(new JsonObject { ["t"] = "bye" }, ct);
        await serving;

        await Assert.That(Directory.GetFiles(there).Select(f => Path.GetFileName(f))).IsEquivalentTo(["IMG_0001.JPG"], CollectionOrdering.Matching);
        var phoneNow = (await _store.PhonesAsync(ct)).Single();
        await Assert.That((phoneNow.Name, phoneNow.Kind, phoneNow.Safe, phoneNow.SafeBytes, phoneNow.LastBackup is not null)).IsEqualTo(("Jamie's iPhone", "iPhone", 7, 700L, true));
        // Nothing new came: only that it was coming is noted.
        await Assert.That((await _store.EventsAsync(_phone.Id, 9, ct)).Select(e => e.Text)).IsEquivalentTo(["Receiving 2 items"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Phone_That_Only_Says_Goodbye_Changes_Nothing_And_A_Phone_Paired_Again_Keeps_Its_Folder(CancellationToken ct)
    {
        var (serving, phone, line) = await ConnectedAsync(ct);
        await phone.SendAsync(new JsonObject { ["t"] = "bye" }, ct);
        await serving;
        await Assert.That((await _store.PhonesAsync(ct)).Count).IsEqualTo(0);

        await Assert.That(await _backup.PairedAsync(_phone, ct)).IsEqualTo("Pictures/PhotoSense/Jamie's iPhone");
        var elsewhere = Directory.CreateDirectory(Folder("Elsewhere")).FullName;
        await _backup.MoveToAsync(_phone.Id, elsewhere, ct);
        await Assert.That(await _backup.PairedAsync(_phone with { Name = "Jamie's new iPhone" }, ct)).IsEqualTo(elsewhere);
        var kept = (await _store.PhonesAsync(ct)).Single();
        await Assert.That((kept.Name, kept.Folder)).IsEqualTo(("Jamie's new iPhone", elsewhere));
        // The person's own folder itself is named whole.
        await Assert.That(_backup.Named(Folder("Home"))).IsEqualTo(Folder("Home"));
    }

    [Test]
    [Arguments("size", "The phone sent something PhotoSense does not understand.")]
    [Arguments("size in words", "The phone sent something PhotoSense does not understand.")]
    [Arguments("size below nothing", "The phone sent something PhotoSense does not understand.")]
    [Arguments("more", "The phone sent more of a file than it said it would.")]
    [Arguments("damaged", "A file arrived damaged, so it was not kept. Back up again.")]
    public async Task A_File_Sent_Wrong_Stops_The_Backup_And_Nothing_Of_It_Stays(string wrong, string said, CancellationToken ct)
    {
        byte[] bytes = [9, 8, 7, 6, 5];
        var (serving, phone, line) = await ConnectedAsync(ct);
        await phone.SendAsync(new JsonObject { ["t"] = "backup", ["kind"] = "iPhone" }, ct);
        await HeldAsync(phone);
        await phone.SendAsync(new JsonObject { ["t"] = "offer", ["files"] = new JsonArray(File("f1", "IMG_0009.JPG", bytes)), ["more"] = false }, ct);
        if (wrong.StartsWith("size", StringComparison.Ordinal))
        {
            await phone.ReceiveAsync(ct, "get");
            var header = new JsonObject { ["t"] = "file", ["id"] = "f1" };
            if (wrong == "size in words") header["size"] = "five";
            if (wrong == "size below nothing") header["size"] = -5;
            await phone.SendAsync(header, ct);
        }
        else await SendFileAsync(phone, "f1", bytes, endCheck: wrong == "damaged" ? new string('0', 64) : null, size: wrong == "more" ? 2 : null);

        var stopped = await Assert.ThrowsAsync<TransferException>(() => serving);
        await Assert.That(stopped!.Message).IsEqualTo(said);
        await Assert.That(Directory.EnumerateFileSystemEntries(Backups, "*", new EnumerationOptions { AttributesToSkip = 0 }).Count()).IsEqualTo(0);
        await _backup.StoppedAsync(_phone, stopped);
        await Assert.That((await _store.EventsAsync(_phone.Id, 1, ct)).Single().Text).IsEqualTo($"Backup stopped: {said}");
    }

    [Test]
    public async Task A_Phone_Gone_Part_Way_Leaves_What_Arrived_Whole_And_Nothing_Cut_Short(CancellationToken ct)
    {
        byte[] first = [1, 1, 1];
        var (serving, phone, line) = await ConnectedAsync(ct);
        await phone.SendAsync(new JsonObject { ["t"] = "backup" }, ct);
        await HeldAsync(phone);
        await phone.SendAsync(new JsonObject { ["t"] = "offer", ["files"] = new JsonArray(File("f1", "A.JPG", first), File("f2", "B.JPG", [2, 2, 2, 2])) }, ct);
        await SendFileAsync(phone, "f1", first);
        await phone.ReceiveAsync(ct, "got");
        await phone.ReceiveAsync(ct, "get");
        await phone.SendAsync(new JsonObject { ["t"] = "file", ["id"] = "f2", ["size"] = 4 }, ct);
        await phone.SendDataAsync(new byte[] { 2, 2 }, ct);
        await Assert.That(_backup.Arriving(_phone.Id)!.Bytes).IsGreaterThanOrEqualTo(3);
        // What came is listed only while it is there.
        System.IO.File.Delete(Path.Combine(Backups, "A.JPG"));
        var listed = (await new BackupApi(new TestApi(s => s.AddSingleton(_backup))).PhonesAsync(Http.Get("backup/phones").FromClient(), ct)).Json()[0].GetProperty("arriving");
        await Assert.That((listed.GetProperty("done").GetInt32(), listed.GetProperty("arrived").GetArrayLength())).IsEqualTo((1, 0));
        line.Dispose();

        var stopped = await Assert.ThrowsAsync<IOException>(() => serving);
        await Assert.That(stopped).IsNotNull();
        await Assert.That(Directory.EnumerateFileSystemEntries(Backups, "*", new EnumerationOptions { AttributesToSkip = 0 }).Count()).IsEqualTo(0);
        await Assert.That(_backup.Arriving(_phone.Id)).IsNull();
        await Assert.That((await _store.EventsAsync(_phone.Id, 1, ct)).Single().Text).IsEqualTo("1 item backed up and checked");
        // Stopped by something other than the phone, such as the program stopping, it is said plainly.
        await _backup.StoppedAsync(_phone, new OperationCanceledException());
        await Assert.That((await _store.EventsAsync(_phone.Id, 1, ct)).Single().Text).IsEqualTo("Backup stopped part way. The phone carries on next time.");
    }

    [Test]
    public async Task A_File_That_Could_Not_Be_Made_Stops_The_Backup_With_Nothing_To_Clear_Away(CancellationToken ct)
    {
        var disk = new FailingDisk();
        disk.Fail("create", _ => true);
        var backup = TestBackups.Computer(Folder("Home", "Pictures", "PhotoSense"), _store, home: Folder("Home"), files: disk);
        var (serving, phone, line) = await ConnectedAsync(ct, backup);
        await phone.SendAsync(new JsonObject { ["t"] = "backup" }, ct);
        await HeldAsync(phone);
        await phone.SendAsync(new JsonObject { ["t"] = "offer", ["files"] = new JsonArray(File("f1", "A.JPG", [1], sayChecksum: false)) }, ct);
        await phone.ReceiveAsync(ct, "get");
        await phone.SendAsync(new JsonObject { ["t"] = "file", ["id"] = "f1", ["size"] = 1 }, ct);
        var stopped = await Assert.ThrowsAsync<IOException>(() => serving);
        await Assert.That(stopped!.Message).StartsWith("The disk would not create .A.JPG.");
        await Assert.That(Directory.GetFiles(Backups).Length).IsEqualTo(0);
    }

    private string Trip => Folder("Home", "Pictures", "PhotoSense", "2026-07 Glacier National Park");

    /// <summary>Writes a file into a folder, holds it for the phone, and gives its checksum.</summary>
    private async Task<string> HeldFileAsync(string folder, string name, byte[] bytes, CancellationToken ct)
    {
        var path = Path.Combine(Directory.CreateDirectory(folder).FullName, name);
        await System.IO.File.WriteAllBytesAsync(path, bytes, ct);
        var info = new FileInfo(path);
        await _store.HoldAsync(new HeldFile(_phone.Id, Sha(bytes), path, info.Length, info.LastWriteTimeUtc, TestClock.Day), ct);
        return Sha(bytes);
    }

    /// <summary>A backup with nothing to send, then the trip folder asked for, in two parts; the computer's answer.</summary>
    private async Task<JsonObject> TripAsync(string folder, IReadOnlyList<string> checksums, CancellationToken ct, ComputerBackup? backup = null)
    {
        var (serving, phone, _) = await ConnectedAsync(ct, backup);
        await phone.SendAsync(new JsonObject { ["t"] = "backup" }, ct);
        await HeldAsync(phone);
        await phone.SendAsync(new JsonObject { ["t"] = "offer" }, ct);
        await phone.ReceiveAsync(ct, "done");
        await phone.SendAsync(new JsonObject { ["t"] = "trip", ["sha256"] = new JsonArray([.. checksums.Take(1).Select(c => (JsonNode)c)]), ["more"] = true }, ct);
        await phone.SendAsync(new JsonObject { ["t"] = "trip", ["sha256"] = new JsonArray([.. checksums.Skip(1).Select(c => (JsonNode)c)]), ["folder"] = folder }, ct);
        var made = await phone.ReceiveAsync(ct, "trip-made");
        await phone.SendAsync(new JsonObject { ["t"] = "bye" }, ct);
        await serving;
        return made;
    }

    private async Task<string[]> NotesAsync(CancellationToken ct) => [.. (await _store.EventsAsync(_phone.Id, 20, ct)).Select(e => e.Text)];

    [Test]
    public async Task A_Trips_Copies_Move_Into_A_Folder_Of_Their_Own_Names_Taken_Numbered_And_Are_Held_There_After(CancellationToken ct)
    {
        await _store.SavePhoneAsync(new BackupPhone(_phone.Id, "Jamie's iPhone", "iPhone", Backups, false, TestClock.Day, null, null), ct);
        var a = await HeldFileAsync(Backups, "IMG_A.JPG", [1, 1], ct);
        var b = await HeldFileAsync(Backups, "IMG_B.JPG", [2, 2, 2], ct);
        var c = await HeldFileAsync(Backups, "IMG_C.JPG", [3, 3, 3, 3], ct);
        // The person's own file, of the same name, is there already: it stays, and the copy takes a number.
        Directory.CreateDirectory(Trip);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(Trip, "IMG_A.JPG"), [9], ct);

        var made = await TripAsync("2026-07 Glacier National Park", [a, b, "not-held"], ct);
        await Assert.That((Wire.Text(made, "folder"), (int)made["moved"]!)).IsEqualTo(("Pictures/PhotoSense/2026-07 Glacier National Park", 2));
        await Assert.That(Directory.GetFiles(Trip).Select(f => Path.GetFileName(f)).Order()).IsEquivalentTo(["IMG_A (1).JPG", "IMG_A.JPG", "IMG_B.JPG"], CollectionOrdering.Matching);
        await Assert.That(await System.IO.File.ReadAllBytesAsync(Path.Combine(Trip, "IMG_A.JPG"), ct)).IsEquivalentTo(new byte[] { 9 }, CollectionOrdering.Matching);
        await Assert.That(Directory.GetFiles(Backups).Select(f => Path.GetFileName(f))).IsEquivalentTo(["IMG_C.JPG"], CollectionOrdering.Matching);
        var held = (await _store.HeldAsync(_phone.Id, ct)).ToDictionary(h => h.Sha256, h => h.Path);
        await Assert.That((held[a], held[b], held[c])).IsEqualTo((Path.Combine(Trip, "IMG_A (1).JPG"), Path.Combine(Trip, "IMG_B.JPG"), Path.Combine(Backups, "IMG_C.JPG")));
        await Assert.That((await _store.PhonesAsync(ct)).Single().TripFolders).IsEquivalentTo([Trip], CollectionOrdering.Matching);
        await Assert.That((await NotesAsync(ct))[0]).IsEqualTo("Made the trip folder Pictures/PhotoSense/2026-07 Glacier National Park with 2 items");

        // Asked again: what is there already stays, and the folder is listed once.
        var again = await TripAsync("2026-07 Glacier National Park", [a], ct);
        await Assert.That((int)again["moved"]!).IsEqualTo(0);
        await Assert.That((await _store.PhonesAsync(ct)).Single().TripFolders!.Count).IsEqualTo(1);
        await Assert.That((await _store.HeldAsync(_phone.Id, ct)).Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_Copy_Moved_Among_The_Phones_Folders_Is_Found_Again_By_Its_Checksum_And_Not_Sent_Twice(CancellationToken ct)
    {
        await _store.SavePhoneAsync(new BackupPhone(_phone.Id, "Jamie's iPhone", "iPhone", Backups, false, TestClock.Day, null, null), ct);
        var a = await HeldFileAsync(Backups, "IMG_A.JPG", [1, 1], ct);
        var b = await HeldFileAsync(Backups, "IMG_B.JPG", [2, 2, 2], ct);
        var c = await HeldFileAsync(Backups, "IMG_C.JPG", [3, 3, 3, 3], ct);
        var d = await HeldFileAsync(Backups, "IMG_D.JPG", [5, 5, 5, 5, 5], ct);
        await TripAsync("2026-07 Glacier National Park", [a, b], ct);
        // Deleted, with nothing of its size anywhere: it is sent again.
        System.IO.File.Delete(Path.Combine(Backups, "IMG_D.JPG"));

        // The trip folder's move undone, as Recently moved would: the copies are back in the phone's folder, one numbered.
        await System.IO.File.WriteAllBytesAsync(Path.Combine(Backups, "IMG_A.JPG"), [7, 7], ct);
        System.IO.File.Move(Path.Combine(Trip, "IMG_A.JPG"), Path.Combine(Backups, "IMG_A (1).JPG"));
        System.IO.File.Move(Path.Combine(Trip, "IMG_B.JPG"), Path.Combine(Backups, "IMG_B.JPG"));
        // Another file of the same size and time as one gone is not taken for it.
        var gone = Path.Combine(Backups, "IMG_C.JPG");
        var stranger = Path.Combine(Backups, "OTHER.JPG");
        await System.IO.File.WriteAllBytesAsync(stranger, [4, 4, 4, 4], ct);
        System.IO.File.SetLastWriteTimeUtc(stranger, System.IO.File.GetLastWriteTimeUtc(gone));
        System.IO.File.Delete(gone);
        // A file of the same size and time as IMG_A's copy, with other bytes, is not taken for it either.
        System.IO.File.SetLastWriteTimeUtc(Path.Combine(Backups, "IMG_A.JPG"), System.IO.File.GetLastWriteTimeUtc(Path.Combine(Backups, "IMG_A (1).JPG")));

        var (serving, phone, _) = await ConnectedAsync(ct);
        await phone.SendAsync(new JsonObject { ["t"] = "backup" }, ct);
        var (stillHeld, _) = await HeldAsync(phone);
        await phone.SendAsync(new JsonObject { ["t"] = "offer" }, ct);
        await phone.ReceiveAsync(ct, "done");
        await phone.SendAsync(new JsonObject { ["t"] = "bye" }, ct);
        await serving;

        await Assert.That(stillHeld.Order()).IsEquivalentTo(new[] { a, b }.Order(), CollectionOrdering.Matching);
        var held = (await _store.HeldAsync(_phone.Id, ct)).ToDictionary(h => h.Sha256, h => h.Path);
        await Assert.That((held[a], held[b], held.ContainsKey(c))).IsEqualTo((Path.Combine(Backups, "IMG_A (1).JPG"), Path.Combine(Backups, "IMG_B.JPG"), false));
        await Assert.That(held.ContainsKey(d)).IsFalse();
        await Assert.That((await NotesAsync(ct))[0]).IsEqualTo("2 items no longer in the folder as they were, so the phone sends them again");
    }

    [Test]
    public async Task A_Trip_With_Nothing_Held_Makes_Its_Folder_Empty_And_A_Move_That_Fails_Is_Said_To_The_Phone(CancellationToken ct)
    {
        await _store.SavePhoneAsync(new BackupPhone(_phone.Id, "Jamie's iPhone", "iPhone", Backups, false, TestClock.Day, null, null), ct);
        var empty = await TripAsync("2025-06 Anaconda and Butte", [], ct);
        await Assert.That((Wire.Text(empty, "folder"), (int)empty["moved"]!)).IsEqualTo(("Pictures/PhotoSense/2025-06 Anaconda and Butte", 0));
        await Assert.That(Directory.Exists(Folder("Home", "Pictures", "PhotoSense", "2025-06 Anaconda and Butte"))).IsTrue();

        var a = await HeldFileAsync(Backups, "IMG_A.JPG", [1, 1], ct);
        var disk = new FailingDisk().Fail("move", _ => true, "The disk is full.");
        var failing = TestBackups.Computer(Folder("Home", "Pictures", "PhotoSense"), _store, home: Folder("Home"), files: disk);
        var refused = await TripAsync("2026-07 Glacier National Park", [a], ct, failing);
        await Assert.That(Wire.Text(refused, "problem")).StartsWith("Pictures/PhotoSense/2026-07 Glacier National Park could not be made. ");
        await Assert.That((System.IO.File.Exists(Path.Combine(Backups, "IMG_A.JPG")), Directory.Exists(Trip))).IsEqualTo((true, false));
        await Assert.That((await _store.HeldAsync(_phone.Id, ct)).Single().Path).IsEqualTo(Path.Combine(Backups, "IMG_A.JPG"));
    }
}
