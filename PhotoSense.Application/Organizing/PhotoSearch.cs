using PhotoSense.Application.Plans;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Matching;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;

namespace PhotoSense.Application.Organizing;

/// <summary>What a search found.</summary>
/// <param name="Query">The search, in its parts.</param>
/// <param name="Photos">What matches, latest first, no more than <see cref="PhotoSearch.Most"/>.</param>
/// <param name="Total">How many match in all.</param>
/// <param name="ProNeeded">It named things in photos, which only Pro searches; the free plan matched its date and place alone.</param>
/// <param name="Videos">How many of those that match are videos; the rest are pictures.</param>
public sealed record SearchResult(SearchQuery Query, IReadOnlyList<Photo> Photos, int Total, bool ProNeeded, int Videos);

/// <summary>
/// Search on the phone, over what the scans recorded: by date and place on any plan, and by what is in a photo, from the
/// phone's own labels, on Pro. Nothing searched for leaves the device.
/// </summary>
public sealed class PhotoSearch
{
    /// <summary>The most photos a search answers with.</summary>
    public const int Most = 500;

    private readonly IPhotoRepository _repo;
    private readonly IPlaceNameResolver _places;
    private readonly Subscription _subscription;

    /// <summary>Search over what <paramref name="repo"/> recorded.</summary>
    /// <param name="repo">What the scans recorded.</param>
    /// <param name="places">The towns, regions and landmarks positions are at.</param>
    /// <param name="subscription">The plan, as searching for what is in photos is part of Pro.</param>
    public PhotoSearch(IPhotoRepository repo, IPlaceNameResolver places, Subscription subscription)
    {
        _repo = repo;
        _places = places;
        _subscription = subscription;
    }

    /// <summary>The photos that match <paramref name="text"/>, latest first.</summary>
    public async Task<SearchResult> SearchAsync(string text, CancellationToken ct)
    {
        var photos = (await _repo.GetAllAsync(ct)).Where(p => !(p.IsVideo && p.LivePhotoId is not null)).ToList();
        // Each photo's place names, worked out once.
        var placed = photos.ToDictionary(p => p.Id, PlacesOf);
        var query = SearchQuery.Parse(text, [.. placed.Values.SelectMany(n => n).Distinct(StringComparer.OrdinalIgnoreCase)]);
        var pro = (await _subscription.CurrentAsync(ct)).Pro;
        var words = pro ? query.Words : [];
        var proNeeded = !pro && query.Words.Count > 0;
        var asked = query.Year is not null || query.Month is not null || query.Places.Count > 0 || words.Count > 0;
        var found = !asked ? [] : photos
            .Where(p => query.Year is not { } year || When(p)?.Year == year)
            .Where(p => query.Month is not { } month || When(p)?.Month == month)
            .Where(p => query.Places.All(place => placed[p.Id].Contains(place, StringComparer.OrdinalIgnoreCase)))
            .Where(p => words.All(word => p.Labels is { } labels && SearchQuery.Names(labels, word)))
            .OrderByDescending(When)
            .ToList();
        return new SearchResult(query, [.. found.Take(Most)], found.Count, proNeeded, found.Count(p => p.IsVideo));
    }

    private static DateTime? When(Photo photo) => photo.TakenOn ?? photo.FileModifiedUtc;

    // The town, its region and any landmark the photo was taken at.
    private string[] PlacesOf(Photo photo)
        => photo.Latitude is { } latitude && photo.Longitude is { } longitude && _places.Locate(latitude, longitude) is { } at
            ? [.. new[] { at.Town, at.Region, at.Landmark }.OfType<string>()]
            : [];
}
