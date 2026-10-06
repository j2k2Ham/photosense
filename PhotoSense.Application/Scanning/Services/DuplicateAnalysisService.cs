using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Scanning.Services;

public class DuplicateAnalysisService : IDuplicateAnalysisService
{
    private readonly IPhotoRepository _repo;
    private readonly PhotoRanking _ranking;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (long Version, DuplicateAnalysis Analysis)? _cached;

    public DuplicateAnalysisService(IPhotoRepository repo, PhotoRanking? ranking = null)
    {
        _repo = repo;
        _ranking = ranking ?? new PhotoRanking();
    }

    public async Task<DuplicateAnalysis> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // The version is read first: a change made while analysing leaves the cache stale, never wrongly fresh.
            var version = _repo.Version;
            if (_cached is { } cached && cached.Version == version) return cached.Analysis;
            var analysis = Analyze(await _repo.GetAllAsync(ct), _ranking);
            _cached = (version, analysis);
            return analysis;
        }
        finally { _gate.Release(); }
    }

    public static DuplicateAnalysis Analyze(IReadOnlyList<Photo> photos, PhotoRanking? ranking = null)
    {
        var bestFirst = (ranking ?? new PhotoRanking()).BestFirst;

        // A Live Photo's video belongs to its picture and goes wherever the picture goes. Matched on its own
        // it could be removed as a copy in one folder while its twin is removed with its picture in another.
        var livePictures = photos.Where(p => !p.IsVideo && p.LivePhotoId is not null).Select(LivePhotoKey).ToHashSet();

        // Identical files are one picture; the best of them stands for the rest when comparing pictures.
        var units = photos
            .Where(p => !string.IsNullOrWhiteSpace(p.ContentHash))
            .Where(p => !(p.IsVideo && p.LivePhotoId is not null && livePictures.Contains(LivePhotoKey(p))))
            .GroupBy(p => p.ContentHash!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Unit(g.OrderBy(p => p, bestFirst).ToList()))
            .OrderBy(u => u.Best, bestFirst)
            .ToList();

        var links = FindLinks(units);

        // Best first, each unclaimed picture becomes a keeper and claims the unclaimed pictures confirmed
        // against it. Whatever is claimed was therefore compared with the very photo that is kept, and is
        // never better than it.
        var duplicates = new List<DuplicateGroup>();
        var keepers = new List<int>();
        var claimed = new bool[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            if (claimed[i]) continue;
            claimed[i] = true;
            keepers.Add(i);
            var keeper = units[i].Best;
            var members = units[i].Photos.Skip(1).Select(p => new DuplicateMember(p, MatchKind.Identical, 0, 0)).ToList();
            foreach (var (other, verdict) in links[i])
            {
                if (claimed[other] || verdict.Kind != MatchKind.SamePicture) continue;
                claimed[other] = true;
                members.AddRange(units[other].Photos.Select(p => new DuplicateMember(p, MatchKind.SamePicture, verdict.HashDistance, verdict.Difference)));
            }
            if (members.Count > 0) duplicates.Add(new DuplicateGroup(keeper, members));
        }

        // Look-alikes are gathered the same way among the keepers only; a photo already set to go
        // with its keeper is not offered again.
        var similar = new List<DuplicateGroup>();
        var isKeeper = new bool[units.Count];
        foreach (var k in keepers) isKeeper[k] = true;
        var grouped = new bool[units.Count];
        foreach (var i in keepers)
        {
            if (grouped[i]) continue;
            var members = new List<DuplicateMember>();
            foreach (var (other, verdict) in links[i])
            {
                if (!isKeeper[other] || grouped[other]) continue;
                grouped[other] = true;
                members.Add(new DuplicateMember(units[other].Best, MatchKind.Similar, verdict.HashDistance, verdict.Difference));
            }
            if (members.Count == 0) continue;
            grouped[i] = true;
            similar.Add(new DuplicateGroup(units[i].Best, members));
        }

        return new DuplicateAnalysis(
            duplicates.OrderByDescending(g => g.ReclaimableBytes).ToList(),
            similar.OrderByDescending(g => g.Members.Count).ToList());
    }

    // Both halves of a Live Photo sit in one folder, share a name and carry the same identifier.
    private static (string Folder, string Item, string Id) LivePhotoKey(Photo p)
    {
        // A record holding a bare file name has no folder to spell out in full.
        var folder = Path.GetDirectoryName(p.SourcePath);
        return (string.IsNullOrEmpty(folder) ? string.Empty : PhotoPath.Key(folder), PhotoNaming.ItemOf(p.FileName), p.LivePhotoId!.ToUpperInvariant());
    }

    // Every pair of pictures whose hashes are close, checked directly. links[i] holds the pictures matched with i.
    private static List<(int Other, PhotoMatcher.Verdict Verdict)>[] FindLinks(List<Unit> units)
    {
        var links = new List<(int, PhotoMatcher.Verdict)>[units.Count];
        var hashes = new ulong[units.Count];
        var hashed = new bool[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            links[i] = [];
            hashed[i] = units[i].Best.Signature is not null && PhotoMatcher.TryParseHash(units[i].Best.PerceptualHash, out hashes[i]);
        }
        for (int i = 0; i < units.Count; i++)
        {
            if (!hashed[i]) continue;
            for (int j = i + 1; j < units.Count; j++)
            {
                if (!hashed[j] || PhotoMatcher.HashDistance(hashes[i], hashes[j]) > PhotoMatcher.CandidateBits) continue;
                if (PhotoMatcher.Compare(units[i].Best, units[j].Best) is not { } verdict) continue;
                links[i].Add((j, verdict));
                links[j].Add((i, verdict));
            }
        }
        return links;
    }

    private sealed class Unit(List<Photo> photos)
    {
        public List<Photo> Photos { get; } = photos;
        public Photo Best => Photos[0];
    }
}
