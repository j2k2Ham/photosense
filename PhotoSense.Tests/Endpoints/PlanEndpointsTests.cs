using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Endpoints;

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
        await Assert.That((computer.StatusCode, computer.Text())).IsEqualTo((HttpStatusCode.Conflict, "Plans are bought in PhotoSense on a phone or tablet."));
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

        await Assert.That((restore.StatusCode, restore.Text())).IsEqualTo((HttpStatusCode.Conflict, "Purchases are restored in PhotoSense on a phone or tablet."));
        await Assert.That((manage.StatusCode, manage.Text())).IsEqualTo((HttpStatusCode.Conflict, "Subscriptions are managed in PhotoSense on a phone or tablet."));
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
}
