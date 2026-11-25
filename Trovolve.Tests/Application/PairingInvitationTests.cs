using System.Text.Json.Nodes;
using Moq;
using Trovolve.Application.Scanning.Interfaces;
using Trovolve.Application.Scanning.Services;
using Trovolve.Application.Transfer;
using Trovolve.Domain.Matching;
using Trovolve.Domain.Repositories;

namespace Trovolve.Tests.Application;

/// <summary>What a computer's QR code says, how a phone pairing afresh is turned away, and what the first scan says it found.</summary>
public class PairingInvitationTests
{
    private static readonly Device Phone = new("phone-1", "Jamie's iPhone");
    private static readonly Device Computer = new("computer-1", "Studio iMac");
    private static readonly Func<string, Device, CancellationToken, Task<bool>> NeverAsked = (_, _, _) => throw new InvalidOperationException("Nothing is compared.");

    [Test]
    public async Task A_QR_Code_Is_Read_Back_As_It_Was_Written(CancellationToken ct)
    {
        var secret = Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray();
        var link = new PairingLink("computer-1", "Jamie's Studio iMac & more", ["192.168.1.20", "10.0.0.4"], 47616, secret);
        var text = link.Write();

        await Assert.That(text).StartsWith("trovolve:pair?v=1&id=computer-1&name=Jamie");
        var read = PairingLink.Read(text)!;
        await Assert.That((read.ComputerId, read.ComputerName, string.Join(',', read.Hosts), read.Port)).IsEqualTo(("computer-1", "Jamie's Studio iMac & more", "192.168.1.20,10.0.0.4", 47616));
        await Assert.That(read.Secret).IsEquivalentTo(secret, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(null)]
    [Arguments("https://example.com/?v=1")]
    [Arguments("trovolve:pair?v=2&id=a&name=b&hosts=1.2.3.4&port=1&key=AAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("trovolve:pair?v=1&id=&name=b&hosts=1.2.3.4&port=1&key=AAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("trovolve:pair?v=1&id=a&hosts=1.2.3.4&port=1&key=AAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("trovolve:pair?v=1&id=a&name=b&hosts=&port=1&key=AAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("trovolve:pair?v=1&id=a&name=b&hosts=1.2.3.4&port=0&key=AAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("trovolve:pair?v=1&id=a&name=b&hosts=1.2.3.4&port=70000&key=AAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("trovolve:pair?v=1&id=a&name=b&hosts=1.2.3.4&port=-1&key=AAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("trovolve:pair?v=1&id=a&name=b&hosts=1.2.3.4&port=1")]
    [Arguments("trovolve:pair?v=1&id=a&name=b&hosts=1.2.3.4&port=1&key=!!!!")]
    [Arguments("trovolve:pair?v=1&id=a&name=b&hosts=1.2.3.4&port=1&key=AAAA")]
    public async Task What_Is_Not_A_Whole_Trovolve_Code_Is_Not_Read(string? text, CancellationToken ct)
    {
        await Assert.That(PairingLink.Read(text)).IsNull();
    }

    // The phone's half with a ticket, and the computer's with or without an invitation, on two ends of one connection.
    private static async Task<(Task<Linked> Phone, Task<Linked> Computer)> PairAsync(PairingTicket ticket, PairingInvitation? invitation, CancellationToken ct)
    {
        var (near, far) = await Loopback.PairAsync(ct);
        return (Pairing.PhoneAsync(near, Phone, new MemoryPairs(Phone.Id), NeverAsked, ticket, ct),
            Pairing.ComputerAsync(far, Computer, new MemoryPairs(Computer.Id), NeverAsked, invitation, true, ct));
    }

    [Test]
    public async Task A_Computer_That_Is_Not_Pairing_Turns_A_Phone_Pairing_Afresh_Away(CancellationToken ct)
    {
        var (phone, computer) = await PairAsync(new PairingTicket(new byte[32], null), null, ct);

        var turnedAway = await Assert.ThrowsAsync<TransferException>(() => phone);
        await Assert.That((turnedAway!.Message, turnedAway.Refused)).IsEqualTo(("This computer is not pairing now. On the computer, open Phones and choose Pair a phone.", true));
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => computer))!.Refused).IsTrue();
    }

    [Test]
    public async Task A_Code_That_Is_Not_The_Computers_Is_Turned_Away(CancellationToken ct)
    {
        var (phone, computer) = await PairAsync(new PairingTicket(null, "111 111"), new PairingInvitation("222222", new byte[32]), ct);

        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => phone))!.Message).IsEqualTo("That is not the code this computer shows.");
        await Assert.ThrowsAsync<TransferException>(() => computer);
    }

    [Test]
    public async Task A_Phone_That_Did_Not_Scan_The_Code_Cannot_Prove_It_And_Nothing_Is_Kept(CancellationToken ct)
    {
        var (near, far) = await Loopback.PairAsync(ct);
        var pairs = new MemoryPairs(Computer.Id);
        var computer = Pairing.ComputerAsync(far, Computer, pairs, NeverAsked, new PairingInvitation("222222", new byte[32]), true, ct);
        // A phone of the test's own: it pairs by QR code, and proves with what it does not have.
        await Wire.SendAsync(near, new JsonObject { ["t"] = "phone", ["id"] = "phone-9", ["name"] = "Someone else", ["pair"] = "qr" }, ct);
        var theirKey = Wire.Bytes(await Wire.ReceiveAsync(near, "hello", ct), "key");
        using var mine = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var myKey = mine.PublicKey.ExportSubjectPublicKeyInfo();
        var myNonce = new byte[32];
        byte[] commit = System.Security.Cryptography.SHA256.HashData([.. myKey, .. theirKey, .. myNonce]);
        await Wire.SendAsync(near, new JsonObject { ["t"] = "key", ["key"] = Convert.ToBase64String(myKey), ["commit"] = Convert.ToBase64String(commit) }, ct);
        await Wire.ReceiveAsync(near, "nonce", ct);
        await Wire.SendAsync(near, new JsonObject { ["t"] = "nonce", ["nonce"] = Convert.ToBase64String(myNonce) }, ct);
        await Wire.ReceiveAsync(near, "prove", ct);
        await Wire.SendAsync(near, new JsonObject { ["t"] = "prove", ["mac"] = Convert.ToBase64String(new byte[32]) }, ct);

        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => computer))!.Message).IsEqualTo("The phone did not scan this computer's code, so Trovolve stopped. Scan the code on this computer.");
        await Assert.That(pairs.Paired).IsEmpty();
    }

    [Test]
    public async Task A_Typed_Code_That_Is_Missing_Is_Not_The_Computers(CancellationToken ct)
    {
        var (near, far) = await Loopback.PairAsync(ct);
        var computer = Pairing.ComputerAsync(far, Computer, new MemoryPairs(Computer.Id), NeverAsked, new PairingInvitation("222222", new byte[32]), true, ct);
        await Wire.SendAsync(near, new JsonObject { ["t"] = "phone", ["id"] = "phone-9", ["name"] = "Someone else", ["pair"] = "code" }, ct);

        await Assert.That(Wire.Text(await Wire.ReceiveAsync(near, "refused", ct), "why")).IsEqualTo("That is not the code this computer shows.");
        await Assert.ThrowsAsync<TransferException>(() => computer);
    }

    [Test]
    public async Task A_Way_Of_Pairing_It_Does_Not_Know_Is_Turned_Away(CancellationToken ct)
    {
        var (near, far) = await Loopback.PairAsync(ct);
        var computer = Pairing.ComputerAsync(far, Computer, new MemoryPairs(Computer.Id), NeverAsked, new PairingInvitation("222222", new byte[32]), true, ct);
        await Wire.SendAsync(near, new JsonObject { ["t"] = "phone", ["id"] = "phone-9", ["name"] = "Someone else", ["pair"] = "bluetooth" }, ct);

        await Assert.That(Wire.Text(await Wire.ReceiveAsync(near, "refused", ct), "why")).IsEqualTo("That is not the code this computer shows.");
        await Assert.ThrowsAsync<TransferException>(() => computer);
    }

    [Test]
    public async Task The_First_Scan_Counts_Copies_Then_Other_Similar_Shots_Then_Large_Videos_Each_File_Once(CancellationToken ct)
    {
        var keeper = TestPhotos.Make("IMG_1.HEIC", size: 3_000_000);
        var copy = TestPhotos.Make("IMG_1 (1).HEIC", size: 3_000_000);
        var bigCopy = TestPhotos.Video("MOV_1 (1).MOV", size: 900_000_000);
        var kept = TestPhotos.Make("IMG_1 (2).HEIC", kept: true);
        var shot = TestPhotos.Make("IMG_2.HEIC", size: 4_000_000);
        var big = TestPhotos.Video("MOV_2.MOV", size: 300_000_000);
        var small = TestPhotos.Video("MOV_3.MOV", size: 200_000_000);
        var analysis = new Mock<IDuplicateAnalysisService>();
        analysis.Setup(a => a.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DuplicateAnalysis(
            [new DuplicateGroup(keeper, [new(copy, MatchKind.Identical, 0, 0), new(bigCopy, MatchKind.Identical, 0, 0), new(kept, MatchKind.Identical, 0, 0)])],
            [new DuplicateGroup(keeper, [new(shot, MatchKind.Similar, 4, 0.1), new(copy, MatchKind.Similar, 2, 0.05)])]));
        var repo = new Mock<IPhotoRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([keeper, copy, bigCopy, kept, shot, big, small]);

        var found = await new LibraryFindings(analysis.Object, repo.Object).FindAsync(ct);

        await Assert.That(found).IsEquivalentTo([
            new Finding("duplicates", 2, 903_000_000), new Finding("similar", 1, 4_000_000), new Finding("largeVideos", 1, 300_000_000),
        ], CollectionOrdering.Matching);
    }
}
