using Trovolve.Domain.Entities;

namespace Trovolve.Tests.Application;

/// <summary>Every removal held to the free plan's limit each day, and counted once its files have gone.</summary>
public sealed class DailyLimitTests : IDisposable
{
    private readonly ChangesFixture _f = new();

    public void Dispose() => _f.Dispose();

    private DateOnly Today => DateOnly.FromDateTime(_f.Plan.Clock.GetLocalNow().DateTime);

    private OrganizeBatch Removal(params string[] names)
        => _f.Change(removed: true, moves: [.. names.Select(n => (_f.Put(n), _f.In("_Trovolve_Removed", n)))]);

    [Test]
    public async Task A_Removal_Counts_The_Files_That_Went_But_Not_Their_Companions(CancellationToken ct)
    {
        var batch = Removal("IMG_1.HEIC", "IMG_1.MOV", "IMG_2.JPG");
        batch.Items[1].Companion = true;

        await Assert.That((await _f.Changes.CarryOutAsync(batch, null, ct)).Done).IsTrue();
        await Assert.That(await _f.Plan.Store.RemovedOnAsync(Today, ct)).IsEqualTo(2);
    }

    [Test]
    public async Task A_File_Let_Go_Of_Is_Not_Counted(CancellationToken ct)
    {
        var batch = Removal("IMG_1.JPG", "IMG_2.JPG");

        await Assert.That((await _f.Changes.CarryOutAsync(batch, item => !item.From.EndsWith("IMG_2.JPG", StringComparison.Ordinal), ct)).Done).IsTrue();
        await Assert.That(await _f.Plan.Store.RemovedOnAsync(Today, ct)).IsEqualTo(1);
    }

    [Test]
    public async Task A_Move_Is_No_Removal_And_Is_Neither_Held_Nor_Counted(CancellationToken ct)
    {
        await _f.Plan.Store.AddRemovedAsync(Today, 50, ct);
        var move = _f.Change(moves: (_f.Put("IMG_1.JPG"), _f.In("There", "IMG_1.JPG")));

        await Assert.That((await _f.Changes.CarryOutAsync(move, null, ct)).Done).IsTrue();
        await Assert.That(await _f.Plan.Store.RemovedOnAsync(Today, ct)).IsEqualTo(50);
    }

    [Test]
    public async Task A_Removal_Of_More_Than_Are_Left_Is_Refused_Before_Anything_Is_Written_Down_Or_Moved(CancellationToken ct)
    {
        await _f.Plan.Store.AddRemovedAsync(Today, 49, ct);
        var batch = Removal("IMG_1.JPG", "IMG_2.JPG");

        var result = await _f.Changes.CarryOutAsync(batch, null, ct);

        await Assert.That(result.Done).IsFalse();
        await Assert.That(result.Problem).IsEqualTo("You chose 2 items. The free plan deletes 50 items a day, and you have 1 left today. Nothing was removed.");
        await Assert.That(_f.Stored(batch.Id)).IsNull();
        await Assert.That(_f.Files()).IsEquivalentTo(["IMG_1.JPG", "IMG_2.JPG"], CollectionOrdering.Matching);
        await Assert.That(await _f.Plan.Store.RemovedOnAsync(Today, ct)).IsEqualTo(49);
    }

    [Test]
    public async Task What_Is_Left_Can_Still_Be_Removed_And_Then_Nothing_More_Today(CancellationToken ct)
    {
        await _f.Plan.Store.AddRemovedAsync(Today, 49, ct);

        await Assert.That((await _f.Changes.CarryOutAsync(Removal("IMG_1.JPG"), null, ct)).Done).IsTrue();
        var refused = await _f.Changes.CarryOutAsync(Removal("IMG_2.JPG"), null, ct);

        await Assert.That(refused.Problem).IsEqualTo("You have used today's 50 free removals. More are available tomorrow. Nothing was removed.");
    }

    [Test]
    public async Task An_Undo_Today_Gives_Back_What_It_Puts_Back(CancellationToken ct)
    {
        var batch = Removal("IMG_1.HEIC", "IMG_1.MOV", "IMG_2.JPG");
        batch.Items[1].Companion = true;
        await _f.Changes.CarryOutAsync(batch, null, ct);
        await Assert.That((await _f.Batches.GetAsync(batch.Id, ct))!.Counted).IsEqualTo(2);

        await Assert.That((await _f.Changes.UndoAsync(batch.Id, ct)).Done).IsTrue();
        await Assert.That(await _f.Plan.Store.RemovedOnAsync(Today, ct)).IsEqualTo(0);
    }

    [Test]
    public async Task An_Undo_On_A_Later_Day_Gives_Nothing_Back(CancellationToken ct)
    {
        var batch = Removal("IMG_1.JPG");
        await _f.Changes.CarryOutAsync(batch, null, ct);
        ((TestClock)_f.Plan.Clock).Now = TestClock.Day.AddDays(1);
        await _f.Plan.Store.AddRemovedAsync(Today, 5, ct);

        await _f.Changes.UndoAsync(batch.Id, ct);
        await Assert.That(await _f.Plan.Store.RemovedOnAsync(Today, ct)).IsEqualTo(5);
    }

    [Test]
    public async Task A_Removal_On_Pro_Counted_Nothing_And_Its_Undo_Gives_Nothing_Back(CancellationToken ct)
    {
        await _f.Plan.Store.KeepSharedAsync(new Entitlement(PlanTier.Pro, PlanKind.Lifetime, SharedBy: "Jamie's iPhone"), ct);
        var batch = Removal("IMG_1.JPG");
        await _f.Changes.CarryOutAsync(batch, null, ct);
        await _f.Plan.Store.KeepSharedAsync(null, ct);
        await _f.Plan.Store.AddRemovedAsync(Today, 3, ct);

        await _f.Changes.UndoAsync(batch.Id, ct);
        await Assert.That((await _f.Batches.GetAsync(batch.Id, ct))!.Counted).IsEqualTo(0);
        await Assert.That(await _f.Plan.Store.RemovedOnAsync(Today, ct)).IsEqualTo(3);
    }

    [Test]
    public async Task Pro_Removes_Any_Number(CancellationToken ct)
    {
        await _f.Plan.Store.AddRemovedAsync(Today, 50, ct);
        await _f.Plan.Store.KeepSharedAsync(new Entitlement(PlanTier.Pro, PlanKind.Lifetime, SharedBy: "Jamie's iPhone"), ct);

        await Assert.That((await _f.Changes.CarryOutAsync(Removal("IMG_1.JPG", "IMG_2.JPG"), null, ct)).Done).IsTrue();
        await Assert.That(await _f.Plan.Store.RemovedOnAsync(Today, ct)).IsEqualTo(50);
    }
}
