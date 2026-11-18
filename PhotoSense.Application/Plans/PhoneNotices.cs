using PhotoSense.Application.Removal;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Plans;

/// <summary>A phone's notifications as they stand: which are on, and whether the person lets PhotoSense send any.</summary>
/// <param name="LowStorage">Low-storage alerts are on.</param>
/// <param name="TrialReminder">The trial's reminder is on.</param>
/// <param name="Allowed">The phone lets PhotoSense send notifications.</param>
public sealed record NoticeState(bool LowStorage, bool TrialReminder, bool Allowed);

/// <summary>
/// The notifications a phone sends: the low-storage alert, when less than a tenth of the storage is free, at most once a
/// week, saying what Free up space would free; and the reminder two days before a free trial ends. Both are off until the
/// person turns them on, which asks the phone's own question the first time.
/// </summary>
public sealed class PhoneNotices
{
    /// <summary>The least time between two low-storage alerts.</summary>
    public static readonly TimeSpan AlertEvery = TimeSpan.FromDays(7);

    /// <summary>The share of the storage free below which it is low.</summary>
    public const double LowBelow = 0.10;

    /// <summary>How long before a free trial ends its reminder comes.</summary>
    public static readonly TimeSpan RemindBefore = TimeSpan.FromDays(2);

    private readonly INoticeStore _store;
    private readonly INotifier _notifier;
    private readonly IDeviceStatus _device;
    private readonly CleanUpCategories _categories;
    private readonly Subscription _subscription;
    private readonly TimeProvider _time;

    /// <summary>The phone's notifications, sent through <paramref name="notifier"/>.</summary>
    /// <param name="store">Which are on, and when the last alert went.</param>
    /// <param name="notifier">The phone's own notifications.</param>
    /// <param name="device">The phone, for its storage and what it is called.</param>
    /// <param name="categories">Clean up's categories, for what Free up space would free.</param>
    /// <param name="subscription">The plan, for a trial's end and the price after it.</param>
    /// <param name="time">The clock.</param>
    public PhoneNotices(INoticeStore store, INotifier notifier, IDeviceStatus device, CleanUpCategories categories, Subscription subscription, TimeProvider time)
    {
        _store = store;
        _notifier = notifier;
        _device = device;
        _categories = categories;
        _subscription = subscription;
        _time = time;
    }

    /// <summary>Which notifications are on, and whether the phone lets PhotoSense send any.</summary>
    public async Task<NoticeState> StateAsync(CancellationToken ct)
    {
        var settings = await _store.GetAsync(ct);
        return new NoticeState(settings.LowStorage, settings.TrialReminder, await _notifier.AllowedAsync(ct));
    }

    /// <summary>
    /// Turns low-storage alerts or the trial's reminder on or off; null leaves one as it is. Turning one on asks the
    /// phone's own question when the person has not let PhotoSense send notifications; refused, it stays off.
    /// </summary>
    public async Task<NoticeState> ChangeAsync(bool? lowStorage, bool? trialReminder, CancellationToken ct)
    {
        var settings = await _store.GetAsync(ct);
        var allowed = await _notifier.AllowedAsync(ct);
        if ((lowStorage == true || trialReminder == true) && !allowed) allowed = await _notifier.AskAsync(ct);
        settings = settings with
        {
            LowStorage = lowStorage is { } low ? low && allowed : settings.LowStorage,
            TrialReminder = trialReminder is { } remind ? remind && allowed : settings.TrialReminder,
        };
        await _store.SaveAsync(settings, ct);
        await RemindAsync(settings, ct);
        return new NoticeState(settings.LowStorage, settings.TrialReminder, allowed);
    }

    /// <summary>
    /// Sends the low-storage alert when it is due: alerts on and allowed, less than a tenth of the storage free, none sent
    /// in the last week, and something to free. The phone checks when the app starts or comes back, and in the background.
    /// </summary>
    /// <returns>Whether an alert was sent.</returns>
    public async Task<bool> CheckStorageAsync(CancellationToken ct)
    {
        var settings = await _store.GetAsync(ct);
        var storage = _device.Storage;
        var now = _time.GetUtcNow();
        if (!settings.LowStorage || storage.Total <= 0 || storage.Free >= storage.Total * LowBelow) return false;
        if (settings.LastLowStorage is { } last && now - last < AlertEvery) return false;
        if (!await _notifier.AllowedAsync(ct)) return false;
        var (_, bytes) = await _categories.FreeUpSpaceAsync(ct);
        if (bytes <= 0) return false;
        var full = (int)Math.Round((storage.Total - storage.Free) * 100d / storage.Total, MidpointRounding.AwayFromZero);
        _notifier.Show(new Notice(Notices.LowStorage, $"{DeviceWord(_device.Kind)} storage is {full}% full", $"PhotoSense found {Sizes.Of(bytes)} you can free.", Notices.FreeUpSpace));
        // Sent: noted whatever happens now, so that it is not sent twice in a week.
        await _store.SaveAsync(settings with { LastLowStorage = now }, CancellationToken.None);
        return true;
    }

    /// <summary>Sets the trial's reminder by the plan now, or takes it back; the phone does this as the app starts and after a purchase.</summary>
    public async Task RemindAsync(CancellationToken ct) => await RemindAsync(await _store.GetAsync(ct), ct);

    /// <summary>What the last notification tapped asked to open, once: <see cref="Notices.FreeUpSpace"/> or <see cref="Notices.Subscription"/>.</summary>
    public string? TakeOpened() => _notifier.TakeOpened();

    private async Task RemindAsync(NoticeSettings settings, CancellationToken ct)
    {
        var plan = await _subscription.CurrentAsync(ct);
        if (!settings.TrialReminder || plan.Tier != PlanTier.Trial || plan.Until is not { } end || end - RemindBefore <= _time.GetUtcNow())
        {
            _notifier.Cancel(Notices.TrialReminder);
            return;
        }
        var price = (await _subscription.OffersAsync(ct)).FirstOrDefault(o => o.Kind == PlanKind.Yearly)?.Product.Price;
        var then = price is null ? "Then it renews as the yearly plan" : $"Then {price} a year";
        _notifier.Schedule(new Notice(Notices.TrialReminder, "Your free trial ends in 2 days", $"{then}, unless you cancel. Cancel any time in Settings.", Notices.Subscription),
            end - RemindBefore);
    }

    // "iPhone" and "iPad" as Apple writes them; an Android "phone" or "tablet" starts the sentence.
    private static string DeviceWord(string kind)
    {
        var word = kind.StartsWith("Android ", StringComparison.Ordinal) ? kind["Android ".Length..] : kind;
        return word.StartsWith('i') ? word : char.ToUpperInvariant(word[0]) + word[1..];
    }
}
