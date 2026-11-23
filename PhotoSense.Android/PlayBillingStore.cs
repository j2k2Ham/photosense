using Android.BillingClient.Api;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.AndroidApp;

/// <summary>
/// Google Play Billing, as the phone's store: what is sold, at the prices Google Play gives for the person's country, what
/// was bought, and Google Play's own sheet for buying. What a purchase means as a plan is worked out in shared code
/// (<see cref="PlanProducts"/>).
/// </summary>
/// <remarks>
/// Google Play tells an app which subscriptions are active, but not when they renew, nor whether they are in a free trial:
/// that needs a server, which PhotoSense does not have. The next renewal is therefore counted from the purchase, a month
/// or a year at a time, and a yearly plan bought within the last seven days is taken to be in its trial.
/// </remarks>
public sealed class PlayBillingStore : Java.Lang.Object, IAppStore, IPurchasesUpdatedListener
{
    private readonly SemaphoreSlim _connecting = new(1, 1);
    private readonly TimeProvider _time;
    private BillingClient? _client;
    // The purchase waiting for Google Play's sheet to be put away.
    private TaskCompletionSource<StoreAnswer>? _buying;

    /// <summary>Google Play, with the clock a renewal is counted from.</summary>
    public PlayBillingStore(TimeProvider time) => _time = time;

    /// <inheritdoc />
    public string Name => "Google Play";

    // Connected once, and again whenever Google Play has let go; a failure is said as the store's own.
    private async Task<BillingClient> ClientAsync(CancellationToken ct)
    {
        await _connecting.WaitAsync(ct);
        try
        {
            if (_client is { IsReady: true }) return _client;
            _client ??= BillingClient.NewBuilder(global::Android.App.Application.Context)
                .SetListener(this)
                .EnablePendingPurchases(PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
                .EnableAutoServiceReconnection()
                .Build();
            var connected = new Connected();
            _client.StartConnection(connected);
            Check(await connected.Answer.Task.WaitAsync(ct));
            return _client;
        }
        finally
        {
            _connecting.Release();
        }
    }

    private static void Check(BillingResult result)
    {
        if (result.ResponseCode == BillingResponseCode.Ok) return;
        throw new StoreException(result.ResponseCode is BillingResponseCode.NetworkError or BillingResponseCode.ServiceUnavailable or BillingResponseCode.ServiceDisconnected
            or BillingResponseCode.ServiceTimeout or BillingResponseCode.BillingUnavailable
            ? "Google Play could not be reached. Try again."
            : $"Google Play could not do that ({result.ResponseCode}). Try again.");
    }

    private static bool Subscription(string id) => id != PlanProducts.Lifetime;

    private static QueryProductDetailsParams Asking(IEnumerable<string> ids, bool subscriptions)
        => QueryProductDetailsParams.NewBuilder().SetProductList([.. ids.Select(id => QueryProductDetailsParams.Product.NewBuilder()
            .SetProductId(id).SetProductType(subscriptions ? BillingClient.ProductType.Subs : BillingClient.ProductType.Inapp).Build())]).Build();

    private async Task<IList<ProductDetails>> DetailsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        var found = new List<ProductDetails>();
        foreach (var subscriptions in new[] { true, false })
        {
            var asked = ids.Where(id => Subscription(id) == subscriptions).ToList();
            if (asked.Count == 0) continue;
            var result = await client.QueryProductDetailsAsync(Asking(asked, subscriptions)).WaitAsync(ct);
            Check(result.Result);
            found.AddRange(result.ProductDetailsList ?? []);
        }
        return found;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoreProduct>> ProductsAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var products = new List<StoreProduct>();
        foreach (var details in await DetailsAsync(ids, ct))
        {
            // A subscription's price is its base plan's last phase, after any free trial; a one-time product has its own.
            var phase = details.GetSubscriptionOfferDetails()?.FirstOrDefault(o => o.OfferId is null)?.PricingPhases.PricingPhaseList.LastOrDefault();
            var once = details.GetOneTimePurchaseOfferDetails();
            var (price, micros, currency) = phase is not null ? (phase.FormattedPrice, phase.PriceAmountMicros, phase.PriceCurrencyCode)
                : once is not null ? (once.FormattedPrice, once.PriceAmountMicros, once.PriceCurrencyCode) : (null, 0L, null);
            if (price is not null && currency is not null) products.Add(new StoreProduct(details.ProductId, price, micros / 1_000_000m, currency));
        }
        return products;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StorePurchase>> PurchasesAsync(CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        var now = _time.GetUtcNow();
        var bought = new List<StorePurchase>();
        foreach (var type in new[] { BillingClient.ProductType.Subs, BillingClient.ProductType.Inapp })
        {
            var result = await client.QueryPurchasesAsync(QueryPurchasesParams.NewBuilder().SetProductType(type).Build()).WaitAsync(ct);
            Check(result.Result);
            foreach (var purchase in result.Purchases ?? [])
            {
                if (purchase.PurchaseState != PurchaseState.Purchased) continue;
                // Google Play refunds a purchase not acknowledged within three days.
                if (!purchase.IsAcknowledged) Check(await client.AcknowledgePurchaseAsync(AcknowledgePurchaseParams.NewBuilder().SetPurchaseToken(purchase.PurchaseToken).Build()).WaitAsync(ct));
                var at = DateTimeOffset.FromUnixTimeMilliseconds(purchase.PurchaseTime);
                foreach (var id in purchase.Products)
                {
                    var trial = id == PlanProducts.Yearly && now - at < TimeSpan.FromDays(7);
                    bought.Add(new StorePurchase(id, at, Renewal(id, at, trial, now), trial));
                }
            }
        }
        return bought;
    }

    // The next renewal, counted from the purchase; a trial ends seven days after it began. Pro paid once never ends.
    private static DateTimeOffset? Renewal(string id, DateTimeOffset bought, bool trial, DateTimeOffset now)
    {
        if (id == PlanProducts.Lifetime) return null;
        if (trial) return bought.AddDays(7);
        var next = bought;
        while (next <= now) next = id == PlanProducts.Monthly ? next.AddMonths(1) : next.AddYears(1);
        return next;
    }

    /// <inheritdoc />
    public async Task<StoreAnswer> BuyAsync(string productId, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        var details = (await DetailsAsync([productId], ct)).FirstOrDefault() ?? throw new StoreException("Google Play does not sell that plan here.");
        var screen = MainActivity.Shown ?? throw new StoreException("Open PhotoSense to buy a plan.");
        var asking = ProductDetailsParamsFor(details);
        var answer = new TaskCompletionSource<StoreAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _buying = answer;
        var launched = client.LaunchBillingFlow(screen, BillingFlowParams.NewBuilder().SetProductDetailsParamsList([asking]).Build());
        Check(launched);
        using (ct.Register(() => answer.TrySetCanceled(ct)))
            return await answer.Task;
    }

    // The offer bought: for the yearly plan its free trial where the person may have one, otherwise the base plan.
    private static BillingFlowParams.ProductDetailsParams ProductDetailsParamsFor(ProductDetails details)
    {
        var builder = BillingFlowParams.ProductDetailsParams.NewBuilder().SetProductDetails(details);
        var offers = details.GetSubscriptionOfferDetails();
        if (offers is { Count: > 0 }) builder.SetOfferToken((offers.FirstOrDefault(o => o.OfferId is not null) ?? offers[0]).OfferToken);
        return builder.Build();
    }

    /// <summary>Google Play's answer once its sheet has gone: bought, put away, or waiting for a payment or a parent.</summary>
    public void OnPurchasesUpdated(BillingResult result, IList<Purchase>? purchases)
    {
        var answer = Interlocked.Exchange(ref _buying, null);
        if (answer is null) return;
        if (result.ResponseCode == BillingResponseCode.UserCancelled) answer.TrySetResult(StoreAnswer.Cancelled);
        else if (result.ResponseCode is not (BillingResponseCode.Ok or BillingResponseCode.ItemAlreadyOwned))
            answer.TrySetException(new StoreException($"Google Play could not complete the purchase ({result.ResponseCode}). Try again."));
        else if (purchases?.Any(p => p.PurchaseState == PurchaseState.Pending) == true) answer.TrySetResult(StoreAnswer.Pending);
        else answer.TrySetResult(StoreAnswer.Bought);
    }

    /// <inheritdoc />
    public async Task RestoreAsync(CancellationToken ct) => await PurchasesAsync(ct);

    /// <inheritdoc />
    public Task ManageAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var context = global::Android.App.Application.Context;
        var page = global::Android.Net.Uri.Parse($"https://play.google.com/store/account/subscriptions?package={context.PackageName}");
        context.StartActivity(new global::Android.Content.Intent(global::Android.Content.Intent.ActionView, page).AddFlags(global::Android.Content.ActivityFlags.NewTask));
        return Task.CompletedTask;
    }

    // Google Play's answer to connecting.
    private sealed class Connected : Java.Lang.Object, IBillingClientStateListener
    {
        public TaskCompletionSource<BillingResult> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnBillingSetupFinished(BillingResult result) => Answer.TrySetResult(result);
        public void OnBillingServiceDisconnected() => Answer.TrySetResult(BillingResult.NewBuilder().SetResponseCode((int)BillingResponseCode.ServiceDisconnected).Build());
    }
}
