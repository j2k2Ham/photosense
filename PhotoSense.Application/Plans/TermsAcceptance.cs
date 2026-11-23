using System.Globalization;
using System.Text.Json.Nodes;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Application.Plans;

/// <summary>
/// Whether the person has accepted the note asking them to keep a backup of the photos they care about, and with it the
/// terms of use. It is asked once per person, not once per device (Jamie, 2026-11-23): a phone and the computer it is
/// paired with tell each other the day either accepted, when pairing and at each backup, so the other does not ask.
/// Until Family, every phone paired with a computer is taken to be the computer owner's; with Family, the plan's owner
/// accepts for the computer, and each member for their own phone.
/// </summary>
public sealed class TermsAcceptance
{
    private const string DayFormat = "yyyy-MM-dd";
    private readonly IPlanStore _store;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _keeping = new(1, 1);

    /// <summary>The acceptance <paramref name="store"/> keeps, dated by <paramref name="time"/>'s calendar.</summary>
    public TermsAcceptance(IPlanStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
    }

    /// <summary>The day the person accepted, here or on a device paired with this one; null when they have not.</summary>
    public Task<DateOnly?> AcceptedAsync(CancellationToken ct) => _store.AcceptedAsync(ct);

    /// <summary>The person accepts, today by this device's calendar; accepted before, the first day stands.</summary>
    /// <returns>The day it was accepted.</returns>
    public Task<DateOnly> AcceptAsync(CancellationToken ct) => KeepAsync(DateOnly.FromDateTime(_time.GetLocalNow().DateTime), ct);

    /// <summary>What a paired device said: the day the person accepted there, or null when they have not, which changes nothing.</summary>
    public async Task HeardAsync(DateOnly? day, CancellationToken ct)
    {
        if (day is { } accepted) await KeepAsync(accepted, ct);
        else ct.ThrowIfCancellationRequested();
    }

    /// <summary>The day as a message between two devices carries it ("2026-11-23"), or null for none.</summary>
    public static string? Write(DateOnly? day) => day?.ToString(DayFormat, CultureInfo.InvariantCulture);

    /// <summary>The day a message carries, or null when it carries none, or none that can be read.</summary>
    public static DateOnly? Read(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && DateOnly.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    // One at a time, so that a device hearing from a phone as the person accepts here keeps the one day.
    private async Task<DateOnly> KeepAsync(DateOnly day, CancellationToken ct)
    {
        await _keeping.WaitAsync(ct);
        try
        {
            if (await _store.AcceptedAsync(ct) is { } before) return before;
            await _store.KeepAcceptedAsync(day, ct);
            return day;
        }
        finally
        {
            _keeping.Release();
        }
    }
}
