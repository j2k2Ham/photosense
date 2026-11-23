using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trovolve.Application.Organizing;
using Trovolve.Application.Transfer;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Tests.Endpoints;

/// <summary>The plan, the removals left today, and buying, restoring and managing a plan through the phone's store.</summary>
public class PlanEndpointsTests
{
    private readonly FakeAppStore _app = new();
    private readonly PlanApi _phone;
    private readonly PlanApi _computer;
    private readonly TestApi _computerApi;

    public PlanEndpointsTests()
    {
        _phone = new PlanApi(new TestApi(s => s.AddSingleton<IAppStore>(_app)));
        _computerApi = new TestApi();
        _computer = new PlanApi(_computerApi);
    }

    [Test]
    public async Task A_Phone_On_The_Free_Plan_Says_So_With_Todays_Removals_And_The_Stores_Plans_And_Prices(CancellationToken ct)
    {
        var plan = (await _phone.GetAsync(Http.Get("plan"), ct)).Json();

        await Assert.That(plan.GetProperty("tier").GetString()).IsEqualTo("free");
        await Assert.That(plan.GetProperty("kind").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(plan.GetProperty("store").GetString()).IsEqualTo("App Store");
        await Assert.That((plan.GetProperty("removalsPerDay").GetInt32(), plan.GetProperty("removedToday").GetInt32(), plan.GetProperty("leftToday").GetInt32())).IsEqualTo((50, 0, 50));
        var offers = plan.GetProperty("offers").EnumerateArray()
            .Select(o => $"{o.GetProperty("kind").GetString()} {o.GetProperty("price").GetString()} {o.GetProperty("amount").GetDecimal()} {o.GetProperty("currency").GetString()}");
        await Assert.That(offers).IsEquivalentTo(["yearly $19.99 19.99 USD", "monthly $2.99 2.99 USD", "lifetime $39.00 39 USD", "family $29.99 29.99 USD"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Computer_Has_No_Store_Nor_Offers_And_Has_The_Plan_A_Phone_Shared_With_No_Limit(CancellationToken ct)
    {
        var shared = new Entitlement(PlanTier.Trial, PlanKind.Yearly, TestClock.Day, TestClock.Day.AddDays(7), "Jamie's iPhone");
        await _computerApi.Services.GetRequiredService<IPlanStore>().KeepSharedAsync(shared, ct);

        var plan = (await _computer.GetAsync(Http.Get("plan"), ct)).Json();

        await Assert.That((plan.GetProperty("tier").GetString(), plan.GetProperty("kind").GetString())).IsEqualTo(("trial", "yearly"));
        await Assert.That(DateTimeOffset.Parse(plan.GetProperty("started").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(TestClock.Day);
        await Assert.That(DateTimeOffset.Parse(plan.GetProperty("until").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(TestClock.Day.AddDays(7));
        await Assert.That(plan.GetProperty("sharedBy").GetString()).IsEqualTo("Jamie's iPhone");
        await Assert.That(plan.GetProperty("store").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(plan.GetProperty("leftToday").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(plan.GetProperty("offers").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    [Arguments(StoreAnswer.Bought, "bought", "pro")]
    [Arguments(StoreAnswer.Cancelled, "cancelled", "free")]
    [Arguments(StoreAnswer.Pending, "pending", "free")]
    public async Task Buying_Puts_The_Stores_Sheet_Up_And_Says_How_It_Ended_With_The_Plan_Then(StoreAnswer answer, string outcome, string tier, CancellationToken ct)
    {
        _app.Answer = answer;
        var response = await _phone.BuyAsync(Http.Post("plan/buy", "{\"kind\":\"monthly\"}").FromClient(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(_app.Bought).IsEquivalentTo([PlanProducts.Monthly]);
        await Assert.That(response.Json().GetProperty("outcome").GetString()).IsEqualTo(outcome);
        await Assert.That(response.Json().GetProperty("plan").GetProperty("tier").GetString()).IsEqualTo(tier);
    }

    [Test]
    [Arguments("not json")]
    [Arguments("{\"kind\":\"weekly\"}")]
    [Arguments("{\"kind\":\"1\"}")]
    public async Task Buying_Needs_A_Plan_Named(string body, CancellationToken ct)
    {
        var response = await _phone.BuyAsync(Http.Post("plan/buy", body).FromClient(), ct);

        await Assert.That((response.StatusCode, response.Text())).IsEqualTo((HttpStatusCode.BadRequest, "Choose a plan first."));
        await Assert.That(_app.Bought).IsEmpty();
    }

    [Test]
    public async Task Nothing_Is_Bought_For_Nothing_Nor_On_A_Computer_Nor_While_The_Store_Cannot_Be_Reached(CancellationToken ct)
    {
        var none = await _phone.BuyAsync(Http.Post("plan/buy", "{\"kind\":\"none\"}").FromClient(), ct);
        var computer = await _computer.BuyAsync(Http.Post("plan/buy", "{\"kind\":\"yearly\"}").FromClient(), ct);
        _app.Unreachable = true;
        var unreachable = await _phone.BuyAsync(Http.Post("plan/buy", "{\"kind\":\"yearly\"}").FromClient(), ct);

        await Assert.That((none.StatusCode, none.Text())).IsEqualTo((HttpStatusCode.Conflict, "That plan is not for sale."));
        await Assert.That((computer.StatusCode, computer.Text())).IsEqualTo((HttpStatusCode.Conflict, "Plans are bought in Trovolve on a phone or tablet."));
        await Assert.That((unreachable.StatusCode, unreachable.Text())).IsEqualTo((HttpStatusCode.Conflict, "The App Store could not be reached. Try again."));
    }

    [Test]
    public async Task Restoring_Asks_The_Store_Again_And_Answers_With_The_Plan(CancellationToken ct)
    {
        _app.Restorable.Add(new StorePurchase(PlanProducts.Lifetime, TestClock.Day, null, Trial: false));
        var plan = (await _phone.RestoreAsync(Http.Post("plan/restore").FromClient(), ct)).Json();

        await Assert.That((plan.GetProperty("tier").GetString(), plan.GetProperty("kind").GetString())).IsEqualTo(("pro", "lifetime"));
        await Assert.That(plan.GetProperty("until").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Managing_Opens_The_Stores_Page(CancellationToken ct)
    {
        var response = await _phone.ManageAsync(Http.Post("plan/manage").FromClient(), ct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(_app.Managed).IsEqualTo(1);
    }

    [Test]
    public async Task A_Computer_Neither_Restores_Nor_Manages_And_Nor_Does_A_Phone_Whose_Store_Cannot_Be_Reached(CancellationToken ct)
    {
        var restore = await _computer.RestoreAsync(Http.Post("plan/restore").FromClient(), ct);
        var manage = await _computer.ManageAsync(Http.Post("plan/manage").FromClient(), ct);
        _app.Unreachable = true;
        var restoreOffline = await _phone.RestoreAsync(Http.Post("plan/restore").FromClient(), ct);
        var manageOffline = await _phone.ManageAsync(Http.Post("plan/manage").FromClient(), ct);

        await Assert.That((restore.StatusCode, restore.Text())).IsEqualTo((HttpStatusCode.Conflict, "Purchases are restored in Trovolve on a phone or tablet."));
        await Assert.That((manage.StatusCode, manage.Text())).IsEqualTo((HttpStatusCode.Conflict, "Subscriptions are managed in Trovolve on a phone or tablet."));
        await Assert.That((restoreOffline.StatusCode, restoreOffline.Text())).IsEqualTo((HttpStatusCode.Conflict, "The App Store could not be reached. Try again."));
        await Assert.That((manageOffline.StatusCode, manageOffline.Text())).IsEqualTo((HttpStatusCode.Conflict, "The App Store could not be reached. Try again."));
    }

    [Test]
    public async Task Only_The_Page_Itself_Buys_Restores_Or_Manages(CancellationToken ct)
    {
        await Assert.That((await _phone.BuyAsync(Http.Post("plan/buy", "{\"kind\":\"yearly\"}"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _phone.RestoreAsync(Http.Post("plan/restore"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _phone.ManageAsync(Http.Post("plan/manage"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(_app.Bought).IsEmpty();
        await Assert.That(_app.Managed).IsEqualTo(0);
    }

    [Test]
    public async Task The_Terms_Are_Accepted_Only_From_The_Page_And_Then_Stay_Accepted(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(new TestClock().GetLocalNow().DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That((await _computer.TermsAsync(Http.Get("terms"), ct)).Json().GetProperty("accepted").ValueKind).IsEqualTo(JsonValueKind.Null);

        await Assert.That((await _computer.AcceptAsync(Http.Post("terms/accept"), ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That((await _computer.TermsAsync(Http.Get("terms"), ct)).Json().GetProperty("accepted").ValueKind).IsEqualTo(JsonValueKind.Null);

        var accepted = await _computer.AcceptAsync(Http.Post("terms/accept").FromClient(), ct);
        await Assert.That(accepted.Json().GetProperty("accepted").GetString()).IsEqualTo(today);
        await Assert.That((await _computer.TermsAsync(Http.Get("terms"), ct)).Json().GetProperty("accepted").GetString()).IsEqualTo(today);
    }

    [Test]
    public async Task A_Phone_Accepting_The_Terms_Tells_The_Computer_It_Was_Paired_With(CancellationToken ct)
    {
        var services = new TestApi().Services;
        var backup = new PhoneBackup(new MemoryBackupLedger(), new FakeComputerFinder(), new MemoryPairs("phone"), services.GetRequiredService<OrganizeLibrary>(), TestFiles.Disk,
            TestPlans.Subscription(), TestPlans.Removals(), new FakeDevice(), new FakeFavorites(), "Jamie's iPhone", Path.GetTempPath(), new TestClock(), new MemoryVaultFiles(), new MemoryLocationStore(),
            TestPlans.Terms());
        var before = backup.Telling;

        var accepted = await new PlanApi(new TestApi(s => s.AddSingleton(backup))).AcceptAsync(Http.Post("terms/accept").FromClient(), ct);

        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(ReferenceEquals(backup.Telling, before)).IsFalse();
        // Paired with no computer yet, there is none to tell: pairing tells it.
        await Assert.That(await backup.Telling).IsFalse();
    }
}
