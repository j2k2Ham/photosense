using Trovolve.Application.Scanning.Interfaces;
using Trovolve.Domain.Repositories;

namespace Trovolve.Application.Scanning.Services;

/// <summary>What there is to free of one kind: how many files, and how much they take.</summary>
/// <param name="Kind">"duplicates", "similar" (the shots other than the best of each group) or "largeVideos".</param>
/// <param name="Count">How many files.</param>
/// <param name="Bytes">What they take together.</param>
public sealed record Finding(string Kind, int Count, long Bytes);

/// <summary>
/// What the scans so far have found to free, by kind, as the first scan and its result show it: the copies that can go,
/// the similar shots other than the best of each group, and videos over 200 MB. A file is counted once, under the
/// first of those it is in, so that the kinds add up to what can really be freed. It is asked again as a scan goes on.
/// </summary>
public sealed class LibraryFindings
{
    /// <summary>A video larger than this is a large video: 200 MB.</summary>
    public const long LargeVideoBytes = 200L * 1000 * 1000;

    private readonly IDuplicateAnalysisService _analysis;
    private readonly IPhotoRepository _repo;

    /// <summary>Findings from the groups <paramref name="analysis"/> makes of what <paramref name="repo"/> holds.</summary>
    public LibraryFindings(IDuplicateAnalysisService analysis, IPhotoRepository repo)
    {
        _analysis = analysis;
        _repo = repo;
    }

    /// <summary>The copies, similar shots and large videos found so far, each with how many and how much.</summary>
    public async Task<IReadOnlyList<Finding>> FindAsync(CancellationToken ct)
    {
        var analysis = await _analysis.GetAsync(ct);
        var copies = analysis.Duplicates.SelectMany(g => g.Removable).ToList();
        var counted = copies.Select(p => p.Id).ToHashSet();
        var similar = analysis.Similar.SelectMany(g => g.Removable).Where(p => counted.Add(p.Id)).ToList();
        var videos = (await _repo.GetAllAsync(ct)).Where(p => p.IsVideo && p.FileSizeBytes > LargeVideoBytes && counted.Add(p.Id)).ToList();
        return
        [
            new Finding("duplicates", copies.Count, copies.Sum(p => p.FileSizeBytes)),
            new Finding("similar", similar.Count, similar.Sum(p => p.FileSizeBytes)),
            new Finding("largeVideos", videos.Count, videos.Sum(p => p.FileSizeBytes)),
        ];
    }
}
