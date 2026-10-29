using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Application.Transfer;

/// <summary>A phone or computer, by the id it keeps and the name it goes by.</summary>
public sealed record Device(string Id, string Name);

/// <summary>Two devices that have agreed on keys, the other of the two, and whether they paired just now.</summary>
public sealed record Linked(SecureChannel Channel, Device Other, bool Paired);

/// <summary>
/// How a phone and a computer come to trust each other. Each makes a new key for every connection and the two agree
/// on a secret from them (elliptic-curve Diffie-Hellman, P-256). The first time, each shows a six-digit number worked
/// out from everything said so far, and the person says on both whether the numbers match: someone in between would
/// make them differ. The phone commits to its key before it sees the computer's random number, so that no one in
/// between can choose a key that makes them match. After that the two keep a secret of their own, and prove to each
/// other that they have it each time they connect.
/// </summary>
public static class Pairing
{
    /// <summary>The computer's half. It connects; the phone speaks first.</summary>
    /// <param name="compare">Shows the number and says whether the person found it the same on the phone.</param>
    public static async Task<Linked> ComputerAsync(Stream stream, Device self, IPairingStore pairs, Func<string, Device, CancellationToken, Task<bool>> compare, CancellationToken ct)
    {
        var greeting = await Wire.ReceiveAsync(stream, "phone", ct);
        var phone = new Device(Wire.Text(greeting, "id"), Wire.Text(greeting, "name"));
        using var mine = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var myKey = mine.PublicKey.ExportSubjectPublicKeyInfo();
        await Wire.SendAsync(stream, new JsonObject { ["t"] = "hello", ["id"] = self.Id, ["name"] = self.Name, ["key"] = Convert.ToBase64String(myKey) }, ct);

        var answer = await Wire.ReceiveAsync(stream, "key", ct);
        var theirKey = Wire.Bytes(answer, "key");
        var commit = Wire.Bytes(answer, "commit");
        var known = answer["known"] is JsonValue flag && flag.TryGetValue(out bool knows) && knows;
        var myNonce = RandomNumberGenerator.GetBytes(32);
        await Wire.SendAsync(stream, new JsonObject { ["t"] = "nonce", ["nonce"] = Convert.ToBase64String(myNonce) }, ct);
        var theirNonce = Wire.Bytes(await Wire.ReceiveAsync(stream, "nonce", ct), "nonce");
        if (!CryptographicOperations.FixedTimeEquals(commit, Commit(theirKey, myKey, theirNonce)))
            throw new TransferException("The phone's answer did not match what it promised, so PhotoSense stopped. Try again.");

        var shared = Agree(mine, theirKey);
        var said = Transcript(self.Id, phone.Id, myKey, theirKey, myNonce, theirNonce);
        var pair = await pairs.GetAsync(phone.Id);
        byte[] secret;
        if (pair is not null && known)
        {
            await Wire.SendAsync(stream, new JsonObject { ["t"] = "prove", ["mac"] = Convert.ToBase64String(Proof(pair.Key, said, "computer")) }, ct);
            var proof = Wire.Bytes(await Wire.ReceiveAsync(stream, "prove", ct), "mac");
            if (!CryptographicOperations.FixedTimeEquals(proof, Proof(pair.Key, said, "phone")))
                throw new TransferException($"{phone.Name} is not the phone this computer was paired with. Pair them again.");
            secret = pair.Key;
        }
        else
        {
            var same = await compare(Code(said), phone, ct);
            await Wire.SendAsync(stream, new JsonObject { ["t"] = "confirm", ["same"] = same }, ct);
            if (!same) throw NoMatch;
            if (!Same(await Wire.ReceiveAsync(stream, "confirm", ct))) throw NoMatchThere("phone");
            secret = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, said, "photosense pairing"u8.ToArray());
            await pairs.SaveAsync(new PairedDevice(phone.Id, phone.Name, secret, DateTime.UtcNow));
        }
        return new Linked(Channel(stream, shared, secret, said, "computer to phone", "phone to computer"), phone, pair is null || !known);
    }

    /// <summary>The phone's half. It waits for a computer, and speaks first.</summary>
    public static async Task<Linked> PhoneAsync(Stream stream, Device self, IPairingStore pairs, Func<string, Device, CancellationToken, Task<bool>> compare, CancellationToken ct)
    {
        await Wire.SendAsync(stream, new JsonObject { ["t"] = "phone", ["id"] = self.Id, ["name"] = self.Name }, ct);
        var hello = await Wire.ReceiveAsync(stream, "hello", ct);
        var computer = new Device(Wire.Text(hello, "id"), Wire.Text(hello, "name"));
        var theirKey = Wire.Bytes(hello, "key");
        using var mine = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var myKey = mine.PublicKey.ExportSubjectPublicKeyInfo();
        var myNonce = RandomNumberGenerator.GetBytes(32);
        var pair = await pairs.GetAsync(computer.Id);
        await Wire.SendAsync(stream, new JsonObject
        {
            ["t"] = "key", ["key"] = Convert.ToBase64String(myKey), ["commit"] = Convert.ToBase64String(Commit(myKey, theirKey, myNonce)), ["known"] = pair is not null,
        }, ct);
        var theirNonce = Wire.Bytes(await Wire.ReceiveAsync(stream, "nonce", ct), "nonce");
        await Wire.SendAsync(stream, new JsonObject { ["t"] = "nonce", ["nonce"] = Convert.ToBase64String(myNonce) }, ct);

        var shared = Agree(mine, theirKey);
        var said = Transcript(computer.Id, self.Id, theirKey, myKey, theirNonce, myNonce);
        byte[] secret;
        // With no pairing to prove, the number is shown at once, as the computer shows it; the question goes again
        // should the computer say no first. A computer that has lost its pairing asks to pair afresh, and only then.
        using var asking = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var question = pair is null ? compare(Code(said), computer, asking.Token) : null;
        try
        {
            var next = Wire.Expect(await Wire.ReadFrameAsync(stream, ct), pair is null ? ["confirm"] : ["prove", "confirm"]);
            if (Wire.Text(next, "t") == "prove")
            {
                if (!CryptographicOperations.FixedTimeEquals(Wire.Bytes(next, "mac"), Proof(pair!.Key, said, "computer")))
                    throw new TransferException($"{computer.Name} is not the computer this phone was paired with. Pair them again.");
                await Wire.SendAsync(stream, new JsonObject { ["t"] = "prove", ["mac"] = Convert.ToBase64String(Proof(pair.Key, said, "phone")) }, ct);
                return new Linked(Channel(stream, shared, pair.Key, said, "phone to computer", "computer to phone"), computer, false);
            }
            if (!Same(next))
            {
                await Wire.SendAsync(stream, new JsonObject { ["t"] = "confirm", ["same"] = false }, ct);
                throw NoMatchThere("computer");
            }
            var same = await (question ?? compare(Code(said), computer, ct));
            await Wire.SendAsync(stream, new JsonObject { ["t"] = "confirm", ["same"] = same }, ct);
            if (!same) throw NoMatch;
            secret = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, said, "photosense pairing"u8.ToArray());
            await pairs.SaveAsync(new PairedDevice(computer.Id, computer.Name, secret, DateTime.UtcNow));
        }
        finally
        {
            // A question still up is put away: the answer is no longer wanted.
            await asking.CancelAsync();
        }
        return new Linked(Channel(stream, shared, secret, said, "phone to computer", "computer to phone"), computer, true);
    }

    /// <summary>The number each shows, "482 917", worked out from everything said.</summary>
    public static string Code(byte[] said)
    {
        var number = BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData([.. said, .. "code"u8])) % 1_000_000;
        return $"{number / 1000:000} {number % 1000:000}";
    }

    private static TransferException NoMatch => new("The numbers did not match, so nothing was sent. Try again, and look that both show the same number.");

    private static TransferException NoMatchThere(string where) => new($"On the {where} the numbers were said not to match, so nothing was sent. Try again.");

    private static bool Same(JsonObject confirm) => confirm["same"] is JsonValue value && value.TryGetValue(out bool same) && same;

    private static byte[] Commit(byte[] phoneKey, byte[] computerKey, byte[] phoneNonce) => SHA256.HashData([.. phoneKey, .. computerKey, .. phoneNonce]);

    private static byte[] Agree(ECDiffieHellman mine, byte[] theirKey)
    {
        try
        {
            using var theirs = ECDiffieHellman.Create();
            theirs.ImportSubjectPublicKeyInfo(theirKey, out _);
            return mine.DeriveKeyFromHash(theirs.PublicKey, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException) { throw new TransferException("The other device sent a key PhotoSense cannot use."); }
    }

    private static byte[] Transcript(string computerId, string phoneId, byte[] computerKey, byte[] phoneKey, byte[] computerNonce, byte[] phoneNonce)
        => SHA256.HashData([.. Encoding.UTF8.GetBytes(computerId), 0, .. Encoding.UTF8.GetBytes(phoneId), 0, .. computerKey, .. phoneKey, .. computerNonce, .. phoneNonce]);

    private static byte[] Proof(byte[] secret, byte[] said, string who)
    {
        byte[] message = [.. said, .. Encoding.UTF8.GetBytes(who)];
        return HMACSHA256.HashData(secret, message);
    }

    private static SecureChannel Channel(Stream stream, byte[] shared, byte[] secret, byte[] said, string sending, string receiving)
    {
        var session = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, secret, said);
        return new SecureChannel(stream, HKDF.Expand(HashAlgorithmName.SHA256, session, 32, Encoding.UTF8.GetBytes(sending)), HKDF.Expand(HashAlgorithmName.SHA256, session, 32, Encoding.UTF8.GetBytes(receiving)));
    }
}
