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
}
