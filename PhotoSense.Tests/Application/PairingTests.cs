using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Tests.Application;

/// <summary>How a phone and a computer come to trust each other, and what they say once they do.</summary>
public sealed class PairingTests
{
    private static readonly Device Phone = new("phone-1", "Jamie's iPhone");
    private static readonly Device Computer = new("computer-1", "DESKTOP-1");
    private readonly MemoryPairs _phonePairs = new(Phone.Id);
    private readonly MemoryPairs _computerPairs = new(Computer.Id);
    private readonly List<(string Who, string Code)> _shown = [];

    private Func<string, Device, CancellationToken, Task<bool>> Answering(string who, bool same = true) => (code, _, _) =>
    {
        lock (_shown) _shown.Add((who, code));
        return Task.FromResult(same);
    };

    private static Func<string, Device, CancellationToken, Task<bool>> NeverAsked => (_, _, _) => throw new InvalidOperationException("Nothing was to be asked.");

    // Each side as it runs on its own device: what fails there ends that device's connection.
    private static async Task<T> Ending<T>(Stream stream, Task<T> side)
    {
        try { return await side; }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task<(Linked Phone, Linked Computer)> PairAsync(Func<string, Device, CancellationToken, Task<bool>> phoneAsks, Func<string, Device, CancellationToken, Task<bool>> computerAsks)
    {
        var (near, far) = await Loopback.PairAsync();
        var phone = Ending(near, Pairing.PhoneAsync(near, Phone, _phonePairs, phoneAsks, CancellationToken.None));
        var computer = Ending(far, Pairing.ComputerAsync(far, Computer, _computerPairs, computerAsks, CancellationToken.None));
        return (await phone, await computer);
    }

    private async Task PairedBeforeAsync(byte[] phoneKeeps, byte[] computerKeeps)
    {
        await _phonePairs.SaveAsync(new PairedDevice(Computer.Id, Computer.Name, phoneKeeps, DateTime.UtcNow));
        await _computerPairs.SaveAsync(new PairedDevice(Phone.Id, Phone.Name, computerKeeps, DateTime.UtcNow));
    }

    [Test]
    public async Task The_First_Time_Both_Show_One_Number_Each_Keeps_The_Other_And_Then_Only_They_Understand_Each_Other()
    {
        var (phone, computer) = await PairAsync(Answering("phone"), Answering("computer"));

        await Assert.That(_shown.Select(s => s.Who).Order()).IsEquivalentTo(["computer", "phone"], CollectionOrdering.Matching);
        await Assert.That(_shown.Select(s => s.Code).Distinct().Single()).Matches(@"^\d{3} \d{3}$");
        await Assert.That((phone.Other, phone.Paired, computer.Other, computer.Paired)).IsEqualTo((Computer, true, Phone, true));
        await Assert.That(_phonePairs.Paired.Single().Key).IsEquivalentTo(_computerPairs.Paired.Single().Key, CollectionOrdering.Matching);

        await phone.Channel.SendAsync(new JsonObject { ["t"] = "hello again" }, CancellationToken.None);
        await phone.Channel.SendDataAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);
        await Assert.That(Wire.Text(await computer.Channel.ReceiveAsync(CancellationToken.None, "hello again"), "t")).IsEqualTo("hello again");
        await Assert.That(await computer.Channel.ReceiveDataAsync(CancellationToken.None)).IsEquivalentTo(new byte[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await computer.Channel.SendAsync(new JsonObject { ["t"] = "back" }, CancellationToken.None);
        await Assert.That(Wire.Text(await phone.Channel.ReceiveAsync(CancellationToken.None, "back"), "t")).IsEqualTo("back");
        phone.Channel.Dispose();
        computer.Channel.Dispose();
    }

    [Test]
    public async Task Paired_Once_They_Prove_It_To_Each_Other_Without_Asking()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        await PairedBeforeAsync(secret, secret);
        var (phone, computer) = await PairAsync(NeverAsked, NeverAsked);
        await Assert.That((phone.Paired, computer.Paired, phone.Other.Name, computer.Other.Name)).IsEqualTo((false, false, "DESKTOP-1", "Jamie's iPhone"));
    }

    [Test]
    public async Task A_Computer_That_Lost_Its_Pairing_Has_Both_Asked_Again()
    {
        await _phonePairs.SaveAsync(new PairedDevice(Computer.Id, Computer.Name, RandomNumberGenerator.GetBytes(32), DateTime.UtcNow));
        var (phone, computer) = await PairAsync(Answering("phone"), Answering("computer"));
        await Assert.That((_shown.Count, _shown[0].Code == _shown[1].Code, phone.Paired, computer.Paired)).IsEqualTo((2, true, true, true));
        await Assert.That(_phonePairs.Paired.Single().Key).IsEquivalentTo(_computerPairs.Paired.Single().Key, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Phone_That_Lost_Its_Pairing_Has_Both_Asked_Again()
    {
        await _computerPairs.SaveAsync(new PairedDevice(Phone.Id, Phone.Name, RandomNumberGenerator.GetBytes(32), DateTime.UtcNow));
        var (phone, computer) = await PairAsync(Answering("phone"), Answering("computer"));
        await Assert.That((_shown.Count, phone.Paired, computer.Paired)).IsEqualTo((2, true, true));
        await Assert.That(_phonePairs.Paired.Single().Key).IsEquivalentTo(_computerPairs.Paired.Single().Key, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Pairings_That_Do_Not_Agree_Are_Found_Out_And_Nothing_Goes_Further()
    {
        await PairedBeforeAsync(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        var (near, far) = await Loopback.PairAsync();
        var phone = Ending(near, Pairing.PhoneAsync(near, Phone, _phonePairs, NeverAsked, CancellationToken.None));
        var computer = Ending(far, Pairing.ComputerAsync(far, Computer, _computerPairs, NeverAsked, CancellationToken.None));
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => phone))!.Message).IsEqualTo("DESKTOP-1 is not the computer this phone was paired with. Pair them again.");
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => computer))!.Message).IsEqualTo("The other device stopped answering.");
    }

    [Test]
    public async Task Numbers_Said_Not_To_Match_On_The_Phone_First_Stop_Both()
    {
        var (near, far) = await Loopback.PairAsync();
        var computerAnswer = new TaskCompletionSource<bool>();
        var phone = Ending(near, Pairing.PhoneAsync(near, Phone, _phonePairs, Answering("phone", same: false), CancellationToken.None));
        var computer = Ending(far, Pairing.ComputerAsync(far, Computer, _computerPairs, (_, _, _) => computerAnswer.Task, CancellationToken.None));
        // The phone said no while the computer still asks; the computer says yes after.
        await Task.Delay(100);
        computerAnswer.SetResult(true);
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => phone))!.Message).StartsWith("The numbers did not match");
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => computer))!.Message).IsEqualTo("On the phone the numbers were said not to match, so nothing was sent. Try again.");
        await Assert.That(_phonePairs.Paired.Count + _computerPairs.Paired.Count).IsEqualTo(0);
    }

    // ---- someone else's device, saying what PhotoSense would not

    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes);

    /// <summary>A phone that keeps to the messages but not always to what they promise.</summary>
    private static async Task PlayPhoneAsync(Stream stream, JsonNode? known, bool keepsPromise = true, bool proves = false, string? answer = null)
    {
        var ct = CancellationToken.None;
        await Wire.SendAsync(stream, new JsonObject { ["t"] = "phone", ["id"] = "someone", ["name"] = "Someone's phone" }, ct);
        var hello = await Wire.ReceiveAsync(stream, "hello", ct);
        using var mine = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var myKey = mine.PublicKey.ExportSubjectPublicKeyInfo();
        var nonce = RandomNumberGenerator.GetBytes(32);
        byte[] promised = [.. myKey, .. Wire.Bytes(hello, "key"), .. keepsPromise ? nonce : new byte[32]];
        var key = new JsonObject { ["t"] = "key", ["key"] = Base64(myKey), ["commit"] = Base64(SHA256.HashData(promised)) };
        if (known is not null) key["known"] = known;
        await Wire.SendAsync(stream, key, ct);
        await Wire.ReceiveAsync(stream, "nonce", ct);
        await Wire.SendAsync(stream, new JsonObject { ["t"] = "nonce", ["nonce"] = Base64(nonce) }, ct);
        if (proves)
        {
            await Wire.ReceiveAsync(stream, "prove", ct);
            await Wire.SendAsync(stream, new JsonObject { ["t"] = "prove", ["mac"] = Base64(new byte[32]) }, ct);
        }
        else if (keepsPromise)
        {
            await Wire.ReceiveAsync(stream, "confirm", ct);
            await Wire.SendAsync(stream, (JsonObject)JsonNode.Parse(answer ?? "{\"t\": \"confirm\", \"same\": true}")!, ct);
        }
    }

    private async Task<(Task Phone, Task<Linked> Computer)> WithPhoneAsync(Func<Stream, Task> phone)
    {
        var (near, far) = await Loopback.PairAsync();
        return (phone(near), Ending(far, Pairing.ComputerAsync(far, Computer, _computerPairs, Answering("computer"), CancellationToken.None)));
    }

    [Test]
    [Arguments(null)]
    [Arguments("yes")]
    public async Task A_Phone_That_Does_Not_Say_Whether_It_Knows_The_Computer_Is_Paired_Afresh(string? known)
    {
        await _computerPairs.SaveAsync(new PairedDevice("someone", "Someone's phone", RandomNumberGenerator.GetBytes(32), DateTime.UtcNow));
        var (phone, computer) = await WithPhoneAsync(s => PlayPhoneAsync(s, known is null ? null : JsonValue.Create(known)));
        await phone;
        await Assert.That(((await computer).Paired, _shown.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task A_Phone_Whose_Key_Is_Not_The_One_It_Promised_Is_Stopped()
    {
        var (_, computer) = await WithPhoneAsync(s => PlayPhoneAsync(s, false, keepsPromise: false));
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => computer))!.Message).IsEqualTo("The phone's answer did not match what it promised, so PhotoSense stopped. Try again.");
        await Assert.That(_shown).IsEmpty();
    }

    [Test]
    public async Task A_Phone_That_Cannot_Prove_The_Pairing_Is_Stopped()
    {
        await _computerPairs.SaveAsync(new PairedDevice("someone", "Someone's phone", RandomNumberGenerator.GetBytes(32), DateTime.UtcNow));
        var (_, computer) = await WithPhoneAsync(s => PlayPhoneAsync(s, true, proves: true));
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => computer))!.Message).IsEqualTo("Someone's phone is not the phone this computer was paired with. Pair them again.");
    }

    [Test]
    [Arguments("{\"t\": \"confirm\", \"same\": \"yes\"}")]
    [Arguments("{\"t\": \"confirm\"}")]
    public async Task A_Phone_Whose_Answer_Is_Not_Yes_Is_Taken_To_Say_No(string answer)
    {
        var (_, computer) = await WithPhoneAsync(s => PlayPhoneAsync(s, false, answer: answer));
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => computer))!.Message).IsEqualTo("On the phone the numbers were said not to match, so nothing was sent. Try again.");
        await Assert.That(_computerPairs.Paired).IsEmpty();
    }

    [Test]
    public async Task A_Computer_Whose_Key_Is_Not_A_Key_Is_Stopped()
    {
        var (near, far) = await Loopback.PairAsync();
        var phone = Pairing.PhoneAsync(near, Phone, _phonePairs, NeverAsked, CancellationToken.None);
        await Wire.ReceiveAsync(far, "phone", CancellationToken.None);
        await Wire.SendAsync(far, new JsonObject { ["t"] = "hello", ["id"] = "someone", ["name"] = "Someone's computer", ["key"] = Base64([1, 2, 3]) }, CancellationToken.None);
        await Wire.ReceiveAsync(far, "key", CancellationToken.None);
        await Wire.SendAsync(far, new JsonObject { ["t"] = "nonce", ["nonce"] = Base64(new byte[32]) }, CancellationToken.None);
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => phone))!.Message).IsEqualTo("The other device sent a key PhotoSense cannot use.");
    }

    [Test]
    public async Task The_Number_Is_Six_Digits_From_What_Was_Said_And_The_Same_Each_Time()
    {
        var said = SHA256.HashData("everything said"u8);
        await Assert.That(Pairing.Code(said)).IsEqualTo(Pairing.Code([.. said]));
        await Assert.That(Pairing.Code(said)).Matches(@"^\d{3} \d{3}$");
        await Assert.That(Pairing.Code(said)).IsNotEqualTo(Pairing.Code(SHA256.HashData("something else said"u8)));
    }

    // ---- frames and messages

    private static byte[] Frame(byte[] length, params byte[] payload) => [.. length, .. payload];

    [Test]
    [Arguments(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    [Arguments(new byte[] { 0x7F, 0, 0, 0 })]
    public async Task A_Frame_Of_No_Size_Or_Too_Large_Is_Not_Understood(byte[] length)
    {
        var thrown = await Assert.ThrowsAsync<TransferException>(() => Wire.ReadFrameAsync(new MemoryStream(Frame(length)), CancellationToken.None));
        await Assert.That(thrown!.Message).IsEqualTo("The other device sent something PhotoSense does not understand.");
    }

    [Test]
    public async Task A_Frame_Cut_Short_Says_The_Other_Device_Stopped_Answering()
    {
        var thrown = await Assert.ThrowsAsync<TransferException>(() => Wire.ReadFrameAsync(new MemoryStream(Frame([0, 0, 0, 9], 1, 2)), CancellationToken.None));
        await Assert.That(thrown!.Message).IsEqualTo("The other device stopped answering.");
    }

    [Test]
    [Arguments("not json", "The other device sent something PhotoSense does not understand.")]
    [Arguments("[1, 2]", "The other device sent something PhotoSense does not understand.")]
    [Arguments("{\"t\": 7}", "The other device sent something PhotoSense does not understand.")]
    [Arguments("{\"x\": \"hello\"}", "The other device sent something PhotoSense does not understand.")]
    [Arguments("{\"t\": \"other\"}", "The other device sent something PhotoSense does not understand.")]
    [Arguments("{\"t\": \"busy\"}", "The phone is already sending to another computer.")]
    [Arguments("{\"t\": \"stop\", \"why\": \"The phone was locked.\"}", "The phone was locked.")]
    [Arguments("{\"t\": \"stop\"}", "The other device stopped.")]
    public async Task A_Message_Not_Expected_Says_Why(string message, string why)
    {
        var thrown = Assert.Throws<TransferException>(() => Wire.Expect(Encoding.UTF8.GetBytes(message), "hello"));
        await Assert.That(thrown!.Message).IsEqualTo(why);
    }

    [Test]
    public async Task Fields_Missing_Or_Not_In_Base_64_Are_Not_Understood()
    {
        var message = Wire.Expect("{\"t\": \"hello\", \"key\": \"not base 64!\"}"u8.ToArray(), "hello");
        await Assert.That(Assert.Throws<TransferException>(() => Wire.Text(message, "name"))!.Message).IsEqualTo("The other device sent something PhotoSense does not understand.");
        await Assert.That(Assert.Throws<TransferException>(() => Wire.Bytes(message, "key"))!.Message).IsEqualTo("The other device sent something PhotoSense does not understand.");
    }

    // ---- sealed frames

    private static (SecureChannel Sending, SecureChannel Receiving, MemoryStream Line) Channels()
    {
        var line = new MemoryStream();
        var (one, other) = (RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        return (new SecureChannel(line, one, other), new SecureChannel(line, other, one), line);
    }

    [Test]
    public async Task A_Piece_Of_A_File_Where_A_Message_Was_Expected_And_The_Other_Way_Round_Is_Not_Understood()
    {
        var (sending, receiving, line) = Channels();
        await sending.SendDataAsync(new byte[] { 1 }, CancellationToken.None);
        await sending.SendAsync(new JsonObject { ["t"] = "end" }, CancellationToken.None);
        line.Position = 0;
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => receiving.ReceiveAsync(CancellationToken.None, "end")))!.Message).IsEqualTo("The other device sent something PhotoSense does not understand.");
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => receiving.ReceiveDataAsync(CancellationToken.None)))!.Message).IsEqualTo("The other device sent something PhotoSense does not understand.");
    }

    [Test]
    public async Task A_Frame_Too_Short_To_Be_Sealed_Is_Not_Understood()
    {
        using var receiving = new SecureChannel(new MemoryStream(Frame([0, 0, 0, 3], 1, 2, 3)), new byte[32], new byte[32]);
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => receiving.ReceiveDataAsync(CancellationToken.None)))!.Message).IsEqualTo("The other device sent something PhotoSense does not understand.");
    }

    [Test]
    public async Task Anything_Changed_Or_Sent_Again_On_The_Way_Is_Noticed()
    {
        var (sending, receiving, line) = Channels();
        await sending.SendDataAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        // Sent again: its number is one already used.
        line.Position = 0;
        await Assert.That(await receiving.ReceiveDataAsync(CancellationToken.None)).IsEquivalentTo(new byte[] { 1, 2, 3 }, CollectionOrdering.Matching);
        line.Position = 0;
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => receiving.ReceiveDataAsync(CancellationToken.None)))!.Message).IsEqualTo("Something on the way changed what was sent, so PhotoSense stopped. Try again.");

        // Changed: one bit of it.
        var (sending2, receiving2, line2) = Channels();
        await sending2.SendDataAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);
        line2.GetBuffer()[5] ^= 1;
        line2.Position = 0;
        await Assert.That((await Assert.ThrowsAsync<TransferException>(() => receiving2.ReceiveDataAsync(CancellationToken.None)))!.Message).IsEqualTo("Something on the way changed what was sent, so PhotoSense stopped. Try again.");
    }
}
