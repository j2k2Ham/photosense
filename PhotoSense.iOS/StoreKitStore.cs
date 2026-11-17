using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>
/// The App Store through StoreKit 2, as the phone's store. StoreKit 2 is Swift alone, so a small bridge of PhotoSense's
/// own (<c>Native/StoreKitBridge.swift</c>, built into the app) does the asking and answers each call once, with JSON.
/// What a purchase means as a plan is worked out in shared code (<c>PlanProducts</c>).
/// </summary>
public sealed class StoreKitStore : IAppStore
{
    /// <summary>The App Store, finishing every transaction that arrives while the app runs.</summary>
    public StoreKitStore() => StoreBridge.Start();

    /// <inheritdoc />
    public string Name => "App Store";

    // Starts a call of the bridge and waits for its answer; a caller that has gone stops waiting, though StoreKit goes on.
    private static async Task<JsonElement> AskAsync(Action<nint> call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (id, answer) = StoreBridge.Expect();
        using var stop = ct.Register(() => StoreBridge.GiveUp(id, ct));
        call(id);
        return await answer;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoreProduct>> ProductsAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var answer = await AskAsync(id => StoreBridge.Products(string.Join('\n', ids), id), ct);
        return [.. answer.GetProperty("products").EnumerateArray().Select(p => new StoreProduct(
            p.GetProperty("id").GetString()!, p.GetProperty("price").GetString()!,
            decimal.Parse(p.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture), p.GetProperty("currency").GetString()!))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StorePurchase>> PurchasesAsync(CancellationToken ct)
    {
        var answer = await AskAsync(StoreBridge.Purchases, ct);
        static DateTimeOffset At(JsonElement seconds) => DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds.GetDouble() * 1000));
        return [.. answer.GetProperty("purchases").EnumerateArray().Select(p => new StorePurchase(
            p.GetProperty("id").GetString()!, At(p.GetProperty("bought")),
            p.TryGetProperty("expires", out var expires) ? At(expires) : null, p.GetProperty("trial").GetBoolean()))];
    }

    /// <inheritdoc />
    public async Task<StoreAnswer> BuyAsync(string productId, CancellationToken ct)
    {
        var answer = await AskAsync(id => StoreBridge.Buy(productId, id), ct);
        return answer.GetProperty("outcome").GetString() switch
        {
            "bought" => StoreAnswer.Bought,
            "pending" => StoreAnswer.Pending,
            _ => StoreAnswer.Cancelled,
        };
    }

    /// <inheritdoc />
    public Task RestoreAsync(CancellationToken ct) => AskAsync(StoreBridge.Restore, ct);

    /// <inheritdoc />
    public Task ManageAsync(CancellationToken ct) => AskAsync(StoreBridge.Manage, ct);
}

/// <summary>
/// The Swift bridge's functions, and the one function it calls back with each answer: the pointers are kept here, so
/// that the store's own methods can wait for answers, which an unsafe context may not.
/// </summary>
internal static unsafe class StoreBridge
{
    private static readonly ConcurrentDictionary<nint, TaskCompletionSource<JsonElement>> Waiting = new();
    private static long _next;

    [DllImport("__Internal")] private static extern void photosense_store_start();
    [DllImport("__Internal")] private static extern void photosense_store_products([MarshalAs(UnmanagedType.LPUTF8Str)] string ids, nint id, delegate* unmanaged<nint, byte*, void> done);
    [DllImport("__Internal")] private static extern void photosense_store_purchases(nint id, delegate* unmanaged<nint, byte*, void> done);
    [DllImport("__Internal")] private static extern void photosense_store_buy([MarshalAs(UnmanagedType.LPUTF8Str)] string productId, nint id, delegate* unmanaged<nint, byte*, void> done);
    [DllImport("__Internal")] private static extern void photosense_store_restore(nint id, delegate* unmanaged<nint, byte*, void> done);
    [DllImport("__Internal")] private static extern void photosense_store_manage(nint id, delegate* unmanaged<nint, byte*, void> done);

    /// <summary>Starts the bridge, which finishes every transaction that arrives while the app runs.</summary>
    public static void Start() => photosense_store_start();

    /// <summary>A number for the next call, and its answer to come.</summary>
    public static (nint Id, Task<JsonElement> Answer) Expect()
    {
        var id = (nint)Interlocked.Increment(ref _next);
        var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        Waiting[id] = answer;
        return (id, answer.Task);
    }

    /// <summary>Stops waiting for the call under that number: its caller has gone.</summary>
    public static void GiveUp(nint id, CancellationToken ct)
    {
        if (Waiting.TryRemove(id, out var gone)) gone.TrySetCanceled(ct);
    }

    /// <summary>Asks for the products with these ids, one to a line.</summary>
    public static void Products(string ids, nint id) => photosense_store_products(ids, id, &Answered);

    /// <summary>Asks for the purchases that stand.</summary>
    public static void Purchases(nint id) => photosense_store_purchases(id, &Answered);

    /// <summary>Buys a product, through the App Store's own sheet.</summary>
    public static void Buy(string productId, nint id) => photosense_store_buy(productId, id, &Answered);

    /// <summary>Restores purchases made with the same Apple Account.</summary>
    public static void Restore(nint id) => photosense_store_restore(id, &Answered);

    /// <summary>Opens the App Store's page for managing the subscription.</summary>
    public static void Manage(nint id) => photosense_store_manage(id, &Answered);

    // The bridge's answer, on whatever thread StoreKit gave it, for the call waiting under that number.
    [UnmanagedCallersOnly]
    private static void Answered(nint id, byte* json)
    {
        if (!Waiting.TryRemove(id, out var waiting)) return;
        using var answer = JsonDocument.Parse(Marshal.PtrToStringUTF8((IntPtr)json) ?? "{}");
        var root = answer.RootElement.Clone();
        if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean()) waiting.TrySetResult(root);
        else waiting.TrySetException(new StoreException(root.TryGetProperty("error", out var why) ? why.GetString()! : "The App Store could not do that. Try again."));
    }
}
