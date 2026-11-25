namespace Trovolve.Domain.Services;

/// <summary>Something the store sells, priced as the store says for the person's own country.</summary>
/// <param name="Id">The store's id for it.</param>
/// <param name="Price">The price as the store writes it, such as "$19.99".</param>
/// <param name="Amount">The price as a number, in <paramref name="Currency"/>.</param>
/// <param name="Currency">The currency's ISO 4217 code, such as "USD".</param>
public sealed record StoreProduct(string Id, string Price, decimal Amount, string Currency);

/// <summary>Something the person has bought and still has, as the store says.</summary>
/// <param name="ProductId">The store's id for what was bought.</param>
/// <param name="Bought">When it was bought, or its trial began.</param>
/// <param name="Expires">When it renews or runs out; null when it never does.</param>
/// <param name="Trial">It is in its free trial.</param>
public sealed record StorePurchase(string ProductId, DateTimeOffset Bought, DateTimeOffset? Expires, bool Trial);

/// <summary>How a purchase ended.</summary>
public enum StoreAnswer
{
    /// <summary>Bought: the store has said so.</summary>
    Bought,
    /// <summary>The person put the store's sheet away.</summary>
    Cancelled,
    /// <summary>Waiting for someone else to agree, such as a parent with Ask to Buy, or a payment to clear.</summary>
    Pending,
}

/// <summary>
/// The phone's own store: the App Store through StoreKit 2 on an iPhone, Google Play Billing on Android. A computer has
/// none: it learns the plan from a paired phone.
/// </summary>
public interface IAppStore
{
    /// <summary>What the store is called: "App Store" or "Google Play".</summary>
    string Name { get; }

    /// <summary>What the store sells of <paramref name="ids"/>, with its prices; those it does not sell are left out.</summary>
    Task<IReadOnlyList<StoreProduct>> ProductsAsync(IReadOnlyList<string> ids, CancellationToken ct);

    /// <summary>What the person has bought and still has.</summary>
    Task<IReadOnlyList<StorePurchase>> PurchasesAsync(CancellationToken ct);

    /// <summary>Puts the store's own sheet for buying <paramref name="productId"/> in front of the person, and answers once it has gone.</summary>
    Task<StoreAnswer> BuyAsync(string productId, CancellationToken ct);

    /// <summary>Asks the store again for what the person has bought, on this or any other device with the same account.</summary>
    Task RestoreAsync(CancellationToken ct);

    /// <summary>Opens the store's own page where the person changes or cancels a subscription.</summary>
    Task ManageAsync(CancellationToken ct);
}

/// <summary>The store could not do what was asked, such as when it cannot be reached; each store turns its own errors into one.</summary>
public sealed class StoreException : Exception
{
    /// <summary>A failure, said for the person.</summary>
    public StoreException(string message) : base(message) { }
}
