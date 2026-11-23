using System.Text.Json.Nodes;
using Trovolve.Application.Plans;

namespace Trovolve.Tests.Application;

/// <summary>The note asking for a backup, accepted once: here, or on a device paired with this one, whichever was first.</summary>
public class TermsAcceptanceTests
{
    private static readonly DateOnly Earlier = new(2026, 10, 2);
    private readonly MemoryPlanStore _store = new();
    private readonly TermsAcceptance _terms;

    public TermsAcceptanceTests()
    {
        _terms = new TermsAcceptance(_store, new TestClock());
    }

    [Test]
    public async Task Accepted_Here_It_Is_Dated_Today_By_This_Devices_Calendar_And_Stays_So(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(new TestClock().GetLocalNow().DateTime);
        await Assert.That(await _terms.AcceptedAsync(ct)).IsNull();

        await Assert.That(await _terms.AcceptAsync(ct)).IsEqualTo(today);
        // Accepted again, or heard of from a phone, the first day stands.
        await _terms.HeardAsync(Earlier, ct);
        await Assert.That(await _terms.AcceptAsync(ct)).IsEqualTo(today);
        await Assert.That(await _store.AcceptedAsync(ct)).IsEqualTo(today);
    }

    [Test]
    public async Task Heard_Of_From_A_Paired_Device_It_Is_Accepted_Here_Too_And_Nothing_Heard_Changes_Nothing(CancellationToken ct)
    {
        await _terms.HeardAsync(null, ct);
        await Assert.That(await _terms.AcceptedAsync(ct)).IsNull();

        await _terms.HeardAsync(Earlier, ct);

        await Assert.That(await _terms.AcceptedAsync(ct)).IsEqualTo(Earlier);
        await Assert.ThrowsAsync<OperationCanceledException>(() => _terms.HeardAsync(null, new CancellationToken(canceled: true)));
    }

    [Test]
    public async Task A_Message_Carries_The_Day_As_Written_And_Only_A_Day_Is_Read_From_One()
    {
        await Assert.That(TermsAcceptance.Write(Earlier)).IsEqualTo("2026-10-02");
        await Assert.That(TermsAcceptance.Write(null)).IsNull();
        await Assert.That(TermsAcceptance.Read(JsonValue.Create("2026-10-02"))).IsEqualTo(Earlier);
        await Assert.That(TermsAcceptance.Read(null)).IsNull();
        await Assert.That(TermsAcceptance.Read(JsonValue.Create(20261002))).IsNull();
        await Assert.That(TermsAcceptance.Read(JsonValue.Create("2 October"))).IsNull();
        await Assert.That(TermsAcceptance.Read(new JsonObject())).IsNull();
    }
}
