using Trovolve.Application.Plans;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Application.Organizing;

/// <summary>A photo with no place, and the one taken nearest in time whose place it would be given.</summary>
/// <param name="Photo">The photo with no place.</param>
/// <param name="Nearest">The photo whose place it gets.</param>
public sealed record PlaceFix(Photo Photo, Photo Nearest);

/// <summary>What Fix dates and places finds.</summary>
/// <param name="WrongDates">Photos dated on a camera's default, or in the future; a Live Photo's video goes with its picture.</param>
/// <param name="CommonestWrongDate">The wrong date most of them have, for "32 photos dated Jan 1, 2000"; null when none.</param>
/// <param name="Places">Photos with no place that can be given that of a photo taken within 30 minutes.</param>
/// <param name="TopPlace">The place most of those would get, for "214 of them get Glacier National Park"; null when none.</param>
/// <param name="TopPlaceCount">How many would get it.</param>
/// <param name="UndoDates">How many photos and videos the last fix of dates changed, which Undo puts back; 0 when there is none to undo.</param>
/// <param name="UndoPlaces">How many the last fix of places gave a place, which Undo takes away; 0 when there is none.</param>
public sealed record FixReport(IReadOnlyList<Photo> WrongDates, DateTime? CommonestWrongDate, IReadOnlyList<PlaceFix> Places, string? TopPlace, int TopPlaceCount,
    int UndoDates, int UndoPlaces);

/// <summary>
/// Fix dates and places, on a phone (Jamie, 2026-11-18; Pro): photos a camera dated on its default or in the future
/// are given the date the person chooses, each keeping its time of day; photos with no place are given that of the photo
/// taken nearest in time, within 30 minutes, screenshots and messaging apps' files aside. The phone asks first; each
/// fix is kept by the phone's own id for the photo, so that it holds whatever the file says, and the last of each
/// kind can be undone.
/// </summary>
public sealed class PhotoFixes
{
    private readonly IPhotoRepository _repo;
    private readonly IPhotoDetails _details;
    private readonly IDetailsFixStore _store;
    private readonly IMediaDetailsStore _read;
    private readonly OrganizeLibrary _library;
    private readonly IPlaceNameResolver _places;
    private readonly Subscription _plan;
    private readonly TimeProvider _time;

    /// <summary>Fixes on the phone whose photos <paramref name="details"/> holds.</summary>
    /// <param name="repo">What the scans recorded, which follows each fix.</param>
    /// <param name="details">The phone's own dates and places.</param>
    /// <param name="store">The fixes in force, and the last of each kind.</param>
    /// <param name="read">What Library read from files no scan has, which follows each fix too.</param>
    /// <param name="library">Library's listing, which reads a fixed photo afresh.</param>
    /// <param name="places">The landmarks and towns places are at.</param>
    /// <param name="plan">Fixing is part of Pro.</param>
    /// <param name="time">Today, after which no photo was taken.</param>
    public PhotoFixes(IPhotoRepository repo, IPhotoDetails details, IDetailsFixStore store, IMediaDetailsStore read, OrganizeLibrary library, IPlaceNameResolver places,
        Subscription plan, TimeProvider time)
    {
        _repo = repo;
        _details = details;
        _store = store;
        _read = read;
        _library = library;
        _places = places;
        _plan = plan;
        _time = time;
    }

    /// <summary>What needs a date or a place, and what can be undone.</summary>
    public async Task<FixReport> ReportAsync(CancellationToken ct)
    {
        var all = await _repo.GetAllAsync(ct);
        var shown = all.Where(p => !(p.IsVideo && p.LivePhotoId is not null)).ToList();
        var wrong = WrongIn(shown);
        var places = PlacesFor(shown);
        var named = places.Select(p => Named(p.Nearest)).OfType<string>().GroupBy(n => n).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).FirstOrDefault();
        var commonest = wrong.GroupBy(p => p.TakenOn!.Value.Date).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => (DateTime?)g.Key).FirstOrDefault();
        return new FixReport(wrong, commonest, places, named?.Key, named?.Count() ?? 0,
            Items(await _store.LastAsync(FixKind.Dates, ct)), Items(await _store.LastAsync(FixKind.Places, ct)));
    }

    /// <summary>Gives every photo with a wrong date the day the person chose, each keeping its time of day; the phone asks first.</summary>
    /// <returns>How many photos and videos were changed, as the phone counts them: a Live Photo once on an iPhone.</returns>
    /// <exception cref="InvalidOperationException">The plan is not Pro.</exception>
    /// <exception cref="IOException">The person said no, or the phone could not change them: none changed.</exception>
    public async Task<int> FixDatesAsync(DateOnly day, CancellationToken ct)
    {
        await ProAsync(ct);
        var items = WrongIn(await _repo.GetAllAsync(ct)).Select(p => (Photo: p, Before: new FixedDetails(p.TakenOn, false, null, null),
            After: new FixedDetails(day.ToDateTime(TimeOnly.FromTimeSpan(p.TakenOn!.Value.TimeOfDay)), false, null, null))).ToList();
        return await ApplyAsync(FixKind.Dates, items, ct);
    }

    /// <summary>Gives every photo with no place the place of the one taken nearest in time, within 30 minutes; the phone asks first.</summary>
    /// <returns>How many photos and videos were given a place, as the phone counts them: a Live Photo once on an iPhone.</returns>
    /// <exception cref="InvalidOperationException">The plan is not Pro.</exception>
    /// <exception cref="IOException">The person said no, or the phone could not change them: none changed.</exception>
    public async Task<int> AddPlacesAsync(CancellationToken ct)
    {
        await ProAsync(ct);
        var items = PlacesFor(await _repo.GetAllAsync(ct))
            .Select(p => (p.Photo, Before: new FixedDetails(null, true, null, null), After: new FixedDetails(null, true, p.Nearest.Latitude, p.Nearest.Longitude))).ToList();
        return await ApplyAsync(FixKind.Places, items, ct);
    }

    /// <summary>Puts back what the last fix of a kind changed, after the phone asks; nothing when there is none.</summary>
    /// <returns>How many were put back.</returns>
    /// <exception cref="IOException">The person said no, or the phone could not change them: none changed.</exception>
    public async Task<int> UndoAsync(FixKind kind, CancellationToken ct)
    {
        var last = await _store.LastAsync(kind, ct);
        if (last.Count == 0) return 0;
        _details.ChangeAll([.. last.Select(i => Change(i.Path, i.Before))]);
        // Put back on the phone: undone whatever happens now.
        var fixes = await _store.FixedAsync(CancellationToken.None);
        // A Live Photo's picture and video are one item on an iPhone, with one fix.
        await _store.SaveFixedAsync(last.Select(i => i.Id).Distinct().ToDictionary(id => id, id => Less(fixes.GetValueOrDefault(id), kind)), CancellationToken.None);
        await _store.SaveLastAsync(kind, [], CancellationToken.None);
        await RecordsFollowAsync(last.Select(i => (i.Path, i.Before)), CancellationToken.None);
        return Items(last);
    }

    /// <summary>A photo as a fix in force makes it: its date or place as the fix gave them, whatever the file said.</summary>
    public static void Apply(Photo photo, FixedDetails fix)
    {
        if (fix.Taken is { } taken) photo.TakenOn = taken;
        if (!fix.HasPlace) return;
        photo.Latitude = fix.Latitude;
        photo.Longitude = fix.Longitude;
    }

    /// <summary>What a file says, as a fix in force makes it.</summary>
    public static MediaDetails Apply(MediaDetails details, FixedDetails fix)
        => details with { TakenOn = fix.Taken ?? details.TakenOn, Latitude = fix.HasPlace ? fix.Latitude : details.Latitude, Longitude = fix.HasPlace ? fix.Longitude : details.Longitude };

    private async Task ProAsync(CancellationToken ct)
    {
        if (!(await _plan.CurrentAsync(ct)).Pro) throw new InvalidOperationException("Fixing dates and places is part of Pro.");
    }

    private List<Photo> WrongIn(IEnumerable<Photo> photos)
    {
        var now = _time.GetLocalNow().DateTime;
        return [.. photos.Where(p => p.TakenOn is { } taken && DetailRules.IsWrongDate(taken, now))];
    }

    // Photos with a time and no place, other than screenshots, messaging apps' files and wrong dates, and the photo nearest each.
    private List<PlaceFix> PlacesFor(IEnumerable<Photo> photos)
    {
        var now = _time.GetLocalNow().DateTime;
        var dated = photos.Where(p => p.TakenOn is { } taken && !DetailRules.IsWrongDate(taken, now)).ToList();
        var placed = dated.Where(p => p.Latitude is not null && p.Longitude is not null).ToList();
        var placeless = dated.Where(p => p.Latitude is null && !p.IsScreenshot && !MediaKinds.IsFromMessaging(p.SourcePath) && _details.CanChangePlace(p.SourcePath));
        return [.. DetailRules.NearestPlaced(placeless, placed, p => p.TakenOn!.Value).Select(f => new PlaceFix(f.Photo, f.Nearest))];
    }

    private string? Named(Photo photo) => _places.Locate(photo.Latitude!.Value, photo.Longitude!.Value) is { } at ? at.Landmark ?? at.Town : null;

    // Asks the phone, then keeps each fix by the phone's own id, the last of the kind for Undo, and the records.
    private async Task<int> ApplyAsync(FixKind kind, List<(Photo Photo, FixedDetails Before, FixedDetails After)> items, CancellationToken ct)
    {
        var known = items.Select(i => (i.Photo, Id: _details.IdOf(i.Photo.SourcePath), i.Before, i.After)).Where(i => i.Id is not null).ToList();
        if (known.Count == 0) return 0;
        _details.ChangeAll([.. known.Select(i => Change(i.Photo.SourcePath, i.After))]);
        // Changed on the phone: kept whatever happens now.
        var fixes = await _store.FixedAsync(CancellationToken.None);
        var merged = new Dictionary<string, FixedDetails?>(StringComparer.Ordinal);
        foreach (var (_, id, _, after) in known) merged[id!] = Together(fixes.GetValueOrDefault(id!), after);
        await _store.SaveFixedAsync(merged, CancellationToken.None);
        await _store.SaveLastAsync(kind, [.. known.Select(i => new FixedItem(i.Id!, i.Photo.SourcePath, i.Before, i.After))], CancellationToken.None);
        await RecordsFollowAsync(known.Select(i => (i.Photo.SourcePath, i.After)), CancellationToken.None);
        return known.Select(i => i.Id).Distinct().Count();
    }

    // Pictures and videos as the phone counts them: a Live Photo, one item in Photos, once.
    private static int Items(IReadOnlyList<FixedItem> items) => items.Select(i => i.Id).Distinct().Count();

    // What the scans recorded and what Library read follow the change.
    private async Task RecordsFollowAsync(IEnumerable<(string Path, FixedDetails Details)> changes, CancellationToken ct)
    {
        var changed = changes.ToList();
        var byPath = changed.ToDictionary(c => PhotoPath.Key(c.Path), c => c.Details);
        var records = (await _repo.GetAllAsync(ct)).Where(p => byPath.ContainsKey(PhotoPath.Key(p.SourcePath))).ToList();
        foreach (var photo in records) Apply(photo, byPath[PhotoPath.Key(photo.SourcePath)]);
        await _repo.SaveAllAsync(records, ct);
        var read = changed.Select(c => (Kept: _read.At(c.Path), c.Details)).Where(r => r.Kept is not null).Select(r => r.Kept! with { Details = Apply(r.Kept.Details, r.Details) }).ToList();
        _read.Save(read);
        _library.Forget(changed.Select(c => c.Path));
    }

    private static DetailsChange Change(string path, FixedDetails to)
        => new(path, to.Taken, to.HasPlace ? new PlaceChange(to.Latitude, to.Longitude) : null);

    // A photo's fixes together: a date and a place given at different times both stand.
    private static FixedDetails Together(FixedDetails? before, FixedDetails added)
        => before is null ? added : new FixedDetails(added.Taken ?? before.Taken, added.HasPlace || before.HasPlace, added.HasPlace ? added.Latitude : before.Latitude, added.HasPlace ? added.Longitude : before.Longitude);

    // A photo's fixes less one kind; null when nothing is left.
    private static FixedDetails? Less(FixedDetails? fix, FixKind kind)
    {
        if (fix is null) return null;
        var left = kind == FixKind.Dates ? fix with { Taken = null } : fix with { HasPlace = false, Latitude = null, Longitude = null };
        return left.Taken is null && !left.HasPlace ? null : left;
    }
}

/// <summary>Reads what a file says into the scan's record, then as the fixes made on the phone have it.</summary>
/// <param name="inner">Reads the file.</param>
/// <param name="details">The phone's own id for each photo.</param>
/// <param name="store">The fixes in force.</param>
public sealed class FixedPhotoMetadata(IPhotoMetadataExtractor inner, IPhotoDetails details, IDetailsFixStore store) : IPhotoMetadataExtractor
{
    /// <inheritdoc />
    public async Task ExtractAsync(Photo photo, Stream imageStream, CancellationToken ct)
    {
        await inner.ExtractAsync(photo, imageStream, ct);
        if (details.IdOf(photo.SourcePath) is { } id && (await store.FixedAsync(ct)).GetValueOrDefault(id) is { } fix) PhotoFixes.Apply(photo, fix);
    }
}

/// <summary>Reads what a file says for Library, then as the fixes made on the phone have it.</summary>
/// <param name="inner">Reads the file.</param>
/// <param name="details">The phone's own id for each photo.</param>
/// <param name="store">The fixes in force.</param>
public sealed class FixedMediaDetails(IMediaDetailsReader inner, IPhotoDetails details, IDetailsFixStore store) : IMediaDetailsReader
{
    /// <inheritdoc />
    public async Task<MediaDetails> ReadAsync(string path, CancellationToken ct)
    {
        var read = await inner.ReadAsync(path, ct);
        return details.IdOf(path) is { } id && (await store.FixedAsync(ct)).GetValueOrDefault(id) is { } fix ? PhotoFixes.Apply(read, fix) : read;
    }
}
