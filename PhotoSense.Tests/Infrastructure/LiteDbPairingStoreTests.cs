using LiteDB;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>The devices paired with this one, and the id this one goes by, kept in the database.</summary>
public class LiteDbPairingStoreTests
{
    [Test]
    public async Task This_Device_Is_Given_An_Id_Once_And_Keeps_It(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var self = await db.Pairings(new TestClock()).SelfIdAsync(ct);

        await Assert.That(self).Matches("^[0-9a-f]{32}$");
        await Assert.That(await db.Pairings(new TestClock()).SelfIdAsync(ct)).IsEqualTo(self);
    }

    [Test]
    public async Task A_Paired_Device_Is_Found_By_Its_Id_And_A_Second_Pairing_Replaces_The_First(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var pairs = db.Pairings(new TestClock());
        await Assert.That(await pairs.GetAsync("phone-1", ct)).IsNull();

        await pairs.SaveAsync(new PairedDevice("phone-1", "Jamie's iPhone", [1, 2, 3]), ct);
        await pairs.SaveAsync(new PairedDevice("phone-1", "Jamie's iPhone", [4, 5, 6]), ct);

        var found = await pairs.GetAsync("phone-1", ct);
        await Assert.That((found!.Id, found.Name, Convert.ToHexString(found.Key))).IsEqualTo(("phone-1", "Jamie's iPhone", "040506"));
        await Assert.That(db.GetCollection("paired_devices").Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Nothing_Is_Answered_For_A_Caller_That_Has_Gone(CancellationToken ct)
    {
        using var db = new LiteDatabase(new MemoryStream());
        var pairs = db.Pairings(new TestClock());
        var gone = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => pairs.SelfIdAsync(gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => pairs.GetAsync("phone-1", gone));
        await Assert.ThrowsAsync<OperationCanceledException>(() => pairs.SaveAsync(new PairedDevice("phone-1", "x", []), gone));
    }
}
