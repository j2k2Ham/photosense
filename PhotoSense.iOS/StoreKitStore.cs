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
public sealed unsafe class StoreKitStore : IAppStore
{
    private static readonly ConcurrentDictionary<nint, TaskCompletionSource<JsonElement>> Waiting = new();
    private static long _next;

    /// <summary>The App Store, finishing every transaction that arrives while the app runs.</summary>
    public StoreKitStore() => photosense_store_start();

    /// <inheritdoc />
    public string Name => "App Store";

    [DllImport("__Internal")] private static extern void photosense_store_start();
    [DllImport("__Internal")] private static extern void photosense_store_products([MarshalAs(UnmanagedType.LPUTF8Str)] string ids, nint id, delegate* unmanaged<nint, byte*, void> done);
    [DllImport("__Internal")] private static extern void photosense_store_purchases(nint id, delegate* unmanaged<nint, byte*, void> done);
    [DllImport("__Internal")] private static extern void photosense_store_buy([MarshalAs(UnmanagedType.LPUTF8Str)] string productId, nint id, delegate* unmanaged<nint, byte*, void> done);
    [DllImport("__Internal")] private static extern void photosense_store_restore(nint id, delegate* unmanaged<nint, byte*, void> done);
    [DllImport("__Internal")] private static extern void photosense_store_manage(nint id, delegate* unmanaged<nint, byte*, void> done);

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

    // Starts a call of the bridge and waits for its answer; a caller that has gone stops waiting, though StoreKit goes on.
    private static async Task<JsonElement> AskAsync(Action<nint> call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var id = (nint)Interlocked.Increment(ref _next);
        var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        Waiting[id] = answer;
        using var stop = ct.Register(() => { if (Waiting.TryRemove(id, out var gone)) gone.TrySetCanceled(ct); });
        call(id);
        return await answer.Task;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoreProduct>> ProductsAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var answer = await AskAsync(id => photosense_store_products(string.Join('\n', ids), id, &Answered), ct);
        return [.. answer.GetProperty("products").EnumerateArray().Select(p => new StoreProduct(
            p.GetProperty("id").GetString()!, p.GetProperty("price").GetString()!,
            decimal.Parse(p.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture), p.GetProperty("currency").GetString()!))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StorePurchase>> PurchasesAsync(CancellationToken ct)
    {
        var answer = await AskAsync(id => photosense_store_purchases(id, &Answered), ct);
        static DateTimeOffset At(JsonElement seconds) => DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds.GetDouble() * 1000));
        return [.. answer.GetProperty("purchases").EnumerateArray().Select(p => new StorePurchase(
            p.GetProperty("id").GetString()!, At(p.GetProperty("bought")),
            p.TryGetProperty("expires", out var expires) ? At(expires) : null, p.GetProperty("trial").GetBoolean()))];
    }

    /// <inheritdoc />
    public async Task<StoreAnswer> BuyAsync(string productId, CancellationToken ct)
    {
        var answer = await AskAsync(id => photosense_store_buy(productId, id, &Answered), ct);
        return answer.GetProperty("outcome").GetString() switch
        {
            "bought" => StoreAnswer.Bought,
            "pending" => StoreAnswer.Pending,
            _ => StoreAnswer.Cancelled,
        };
    }

    /// <inheritdoc />
    public Task RestoreAsync(CancellationToken ct) => AskAsync(id => photosense_store_restore(id, &Answered), ct);

    /// <inheritdoc />
    public Task ManageAsync(CancellationToken ct) => AskAsync(id => photosense_store_manage(id, &Answered), ct);
}
