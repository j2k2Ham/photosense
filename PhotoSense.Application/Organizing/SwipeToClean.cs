using PhotoSense.Application.Plans;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Application.Organizing;

/// <summary>A photo of today's set, or one marked on a day before, as the swipe screen shows it.</summary>
/// <param name="Id">The library's id for it, as the page knows it.</param>
/// <param name="Bytes">What it takes, a Live Photo's video included.</param>
/// <param name="OnComputer">Every file of it is on the computer, checked.</param>
/// <param name="Delete">Marked to delete, kept, or not decided yet (null).</param>
/// <param name="Spared">Marked to delete, then tapped on the summary to be kept after all.</param>
public sealed record SwipeCard(string Id, long Bytes, bool OnComputer, bool? Delete, bool Spared);

/// <summary>A day of the week shown under Swipe to clean, and whether something was swiped on it.</summary>
/// <param name="Date">The day.</param>
/// <param name="Swiped">Something was swiped on it.</param>
public sealed record SwipeWeekDay(DateOnly Date, bool Swiped);

/// <summary>How Swipe to clean stands today.</summary>
/// <param name="Date">Today, by the phone's calendar.</param>
/// <param name="Set">Today's set, in the order shown, with what was decided of each.</param>
/// <param name="Earlier">Photos marked to delete on days before and not deleted yet.</param>
/// <param name="Week">The last seven days, today last.</param>
/// <param name="Streak">How many days in a row something was swiped, up to today or, before today's first swipe, up to yesterday.</param>
/// <param name="Freed">What today's last deletion deleted; null before one.</param>
/// <param name="Unswiped">How many photos and videos in the library have never been swiped, today's set aside.</param>
public sealed record SwipeState(DateOnly Date, IReadOnlyList<SwipeCard> Set, IReadOnlyList<SwipeCard> Earlier, IReadOnlyList<SwipeWeekDay> Week, int Streak, SwipeFreed? Freed, int Unswiped);

/// <summary>
/// Swipe to clean: a set of photos and videos a day, drawn at random among those never swiped, the same all day,
/// each kept or marked to delete one at a time. Nothing is deleted while swiping: what is marked is deleted together
/// at the end, after the phone's own question, to its Recently Deleted or bin, the largest first and no more than the
/// day's free removals allow; the rest stay marked for another day.
/// </summary>
public sealed class SwipeToClean
{
    /// <summary>How many photos and videos a day's set holds, at most.</summary>
    public const int SetSize = 50;

    private readonly ISwipeStore _store;
    private readonly OrganizeLibrary _library;
    private readonly IPhotoFileSystem _files;
    private readonly PhoneBackup _backup;
    private readonly DailyRemovals _removals;
    private readonly string _root;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Swipe to clean over the library at <paramref name="root"/>.</summary>
    /// <param name="store">Today's set, what was swiped, what waits to be deleted, and the days swiped.</param>
    /// <param name="library">The library's pictures and videos, with their ids.</param>
    /// <param name="files">Where they are kept; on a phone, it deletes several together after one question.</param>
    /// <param name="backup">Which of them are on the computer, checked.</param>
    /// <param name="removals">What is deleted counts toward the day's free removals.</param>
    /// <param name="root">The library's folder.</param>
    /// <param name="clock">Which day it is.</param>
    public SwipeToClean(ISwipeStore store, OrganizeLibrary library, IPhotoFileSystem files, PhoneBackup backup, DailyRemovals removals, string root, TimeProvider clock)
    {
        _store = store;
        _library = library;
        _files = files;
        _backup = backup;
        _removals = removals;
        _root = root;
        _clock = clock;
    }

    /// <summary>How Swipe to clean stands today; the first time on a day, a new set is drawn.</summary>
    public async Task<SwipeState> StateAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var (day, items) = await TodayAsync(ct);
            return await StateOfAsync(day, items, ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Keeps a photo of today's set, or marks it to delete; nothing is deleted yet.</summary>
    /// <param name="id">The photo, by the library's id.</param>
    /// <param name="delete">Marks it to delete; otherwise it is kept.</param>
    /// <param name="ct">Stops it before anything is noted.</param>
    /// <exception cref="InvalidOperationException">It is not in today's set, or was decided already.</exception>
    public async Task<SwipeState> DecideAsync(string id, bool delete, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var (day, items) = await TodayAsync(ct);
            var key = items.FirstOrDefault(i => i.Item.Main.Id == id).Key;
            if (key is null || !day.Set.Contains(key)) throw new InvalidOperationException("That photo is not in today's set. Swipe to clean shows another.");
            if (day.Decisions.Any(d => d.Key == key)) throw new InvalidOperationException("That photo was swiped already. Undo takes back the last one.");
            day = day with { Decisions = [.. day.Decisions, new SwipeDecision(key, delete)] };
            await _store.SaveDayAsync(day, ct);
            await _store.SetSwipedAsync([key], true, ct);
            await _store.SetDayAsync(day.Date, true, ct);
            if (delete) await _store.SaveMarkedAsync([.. await _store.MarkedAsync(ct), key], ct);
            return await StateOfAsync(day, items, ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Takes back the last decision of today: the photo is shown again, neither kept nor marked.</summary>
    /// <exception cref="InvalidOperationException">Nothing was swiped today.</exception>
    public async Task<SwipeState> UndoAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var (day, items) = await TodayAsync(ct);
            if (day.Decisions.Count == 0) throw new InvalidOperationException("Nothing swiped today to undo.");
            var last = day.Decisions[^1];
            day = day with { Decisions = [.. day.Decisions.Take(day.Decisions.Count - 1)], Spared = [.. day.Spared.Where(k => k != last.Key)] };
            await _store.SaveDayAsync(day, ct);
            await _store.SetSwipedAsync([last.Key], false, ct);
            if (day.Decisions.Count == 0) await _store.SetDayAsync(day.Date, false, ct);
            await _store.SaveMarkedAsync([.. (await _store.MarkedAsync(ct)).Where(k => k != last.Key)], ct);
            return await StateOfAsync(day, items, ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>On the summary, keeps a photo marked to delete after all, or marks it again.</summary>
    /// <param name="id">The photo, by the library's id.</param>
    /// <param name="spared">Keeps it; otherwise it is marked to delete again.</param>
    /// <param name="ct">Stops it before anything is noted.</param>
    /// <exception cref="InvalidOperationException">It is not marked to delete, nor was it spared today.</exception>
    public async Task<SwipeState> SpareAsync(string id, bool spared, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var (day, items) = await TodayAsync(ct);
            var key = items.FirstOrDefault(i => i.Item.Main.Id == id).Key;
            var marked = await _store.MarkedAsync(ct);
            if (key is null || !(spared ? marked.Contains(key) : day.Spared.Contains(key)))
                throw new InvalidOperationException(spared ? "That photo is not marked to delete." : "That photo was not kept on the summary.");
            // Kept on the summary it is no longer marked; marked again it is no longer kept.
            var keptNow = day.Spared.Where(k => k != key).ToList();
            var markedNow = marked.Where(k => k != key).ToList();
            (spared ? keptNow : markedNow).Add(key);
            day = day with { Spared = keptNow };
            await _store.SaveDayAsync(day, ct);
            await _store.SaveMarkedAsync(markedNow, ct);
            return await StateOfAsync(day, items, ct);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Deletes what is marked, today's and that left from days before, the largest first and no more than
    /// <paramref name="atMost"/>: to the phone's Recently Deleted or bin, after its own question. Each counts toward the
    /// day's free removals; what is left stays marked for another day.
    /// </summary>
    /// <param name="atMost">The most to delete; null for all that is marked.</param>
    /// <param name="ct">Stops it before anything is asked.</param>
    /// <exception cref="InvalidOperationException">More than the free plan has left today, or this is not a phone.</exception>
    /// <exception cref="IOException">The person said no, or the phone could not delete them: nothing went.</exception>
    public async Task<SwipeFreed> DeleteAsync(int? atMost, CancellationToken ct)
    {
        if (_files is not IPhotoChangesTogether phone) throw new InvalidOperationException("Only a phone deletes what is swiped.");
        await _gate.WaitAsync(ct);
        try
        {
            var (day, items) = await TodayAsync(ct);
            var byKey = items.ToDictionary(i => i.Key, i => i.Item, StringComparer.Ordinal);
            // A photo deleted some other way since it was marked is no longer waiting.
            var marked = (await _store.MarkedAsync(ct)).Where(byKey.ContainsKey).ToList();
            var going = marked.OrderByDescending(k => byKey[k].Bytes).Take(atMost ?? marked.Count).ToList();
            var freed = new SwipeFreed(0, 0, marked.Count);
            if (going.Count > 0)
            {
                var allowance = await _removals.TodayAsync(ct);
                if (!allowance.Allows(going.Count)) throw new InvalidOperationException(DailyRemovals.Refusal(allowance, going.Count));
                phone.EraseAll([.. going.SelectMany(k => byKey[k].Files.Select(f => f.Path))]);
                // Deleted: counted and noted whatever the caller does now.
                await _removals.RecordAsync(going.Count, allowance, CancellationToken.None);
                freed = new SwipeFreed(going.Count, going.Sum(k => byKey[k].Bytes), marked.Count - going.Count);
            }
            await _store.SaveMarkedAsync([.. marked.Except(going)], CancellationToken.None);
            await _store.SaveDayAsync(day with { Freed = freed }, CancellationToken.None);
            return freed;
        }
        finally { _gate.Release(); }
    }

    // Today's day, drawn afresh on the first ask of a day, and the library's pictures and videos with their keys.
    private async Task<(SwipeDay Day, List<(string Key, PhotoItem Item)> Items)> TodayAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var items = PhotoItem.Of(await _library.ListAsync(_root, ct)).Select(i => (Key: KeyOf(i), Item: i)).ToList();
        if (await _store.DayAsync(ct) is { } day && day.Date == today) return (day, items);
        var swiped = await _store.SwipedAsync(ct);
        // Drawn the same way all day, should the day's record be lost; from a fixed order, so that the draw is the same.
        var unswiped = items.Select(i => i.Key).Where(k => !swiped.Contains(k)).Order(StringComparer.Ordinal).ToArray();
        new Random(today.DayNumber).Shuffle(unswiped);
        var drawn = new SwipeDay(today, [.. unswiped.Take(SetSize)], [], [], null);
        await _store.SaveDayAsync(drawn, ct);
        return (drawn, items);
    }

    private async Task<SwipeState> StateOfAsync(SwipeDay day, List<(string Key, PhotoItem Item)> items, CancellationToken ct)
    {
        var byKey = items.ToDictionary(i => i.Key, i => i.Item, StringComparer.Ordinal);
        // Backup says it of each picture or video, by its main file, once every file of it is there, checked.
        var on = (await _backup.LibraryAsync(ct)).OnComputer.ToHashSet(StringComparer.Ordinal);
        var marked = (await _store.MarkedAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var decided = day.Decisions.ToDictionary(d => d.Key, d => d.Delete, StringComparer.Ordinal);
        SwipeCard Card(string key)
        {
            var item = byKey[key];
            bool? delete = decided.TryGetValue(key, out var d) ? d : null;
            return new SwipeCard(item.Main.Id, item.Bytes, on.Contains(item.Main.Id), delete, day.Spared.Contains(key));
        }
        // A photo deleted some other way since it was drawn or marked is no longer shown.
        var set = day.Set.Where(byKey.ContainsKey).Select(Card).ToList();
        var earlier = marked.Where(k => byKey.ContainsKey(k) && !day.Set.Contains(k)).Select(k => Card(k) with { Delete = true }).ToList();
        var swiped = await _store.SwipedAsync(ct);
        var unswiped = items.Count(i => !swiped.Contains(i.Key) && !day.Set.Contains(i.Key));
        var days = await _store.DaysAsync(ct);
        var week = Enumerable.Range(0, 7).Select(n => day.Date.AddDays(n - 6)).Select(d => new SwipeWeekDay(d, days.Contains(d))).ToList();
        var streak = 0;
        for (var d = days.Contains(day.Date) ? day.Date : day.Date.AddDays(-1); days.Contains(d); d = d.AddDays(-1)) streak++;
        return new SwipeState(day.Date, set, earlier, week, streak, day.Freed, unswiped);
    }

    // What a photo is known by from one run of PhotoSense to the next, and wherever it is moved: its picture's checksum, where the scan worked it out.
    private static string KeyOf(PhotoItem item) => item.Main.ContentHash?.ToLowerInvariant() ?? $"path:{item.Main.Path}";
}
