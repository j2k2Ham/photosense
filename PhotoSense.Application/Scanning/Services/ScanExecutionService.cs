using System.Collections.Concurrent;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Scanning.Services;

public class ScanExecutionService : IScanExecutionService
{
    /// <summary>
    /// Raised whenever a scan starts recording something new about a file. Records written by an earlier
    /// version are read again rather than trusted as "unchanged".
    /// </summary>
    public const int AnalysisVersion = 2;

    private readonly IPhotoRepository _repo;
    private readonly IImageHashingService _hash;
    private readonly IImageAnalyzer _analyzer;
    private readonly IPhotoMetadataExtractor _meta;
    private readonly IThumbnailStore _thumbnails;
    private readonly IScanProgressStore _progress;
    private readonly IScanLogSink? _log;
    private readonly IScanHistory? _history;
    private readonly TimeProvider _time;
    private readonly int _parallelism;

    /// <param name="history">Where what each scan took is kept, so that the next can say how long it will be.</param>
    public ScanExecutionService(IPhotoRepository repo, IImageHashingService hash, IImageAnalyzer analyzer, IPhotoMetadataExtractor meta,
        IThumbnailStore thumbnails, IScanProgressStore progress, IScanLogSink? log = null, int parallelism = 0,
        IScanHistory? history = null, TimeProvider? time = null)
    {
        _repo = repo; _hash = hash; _analyzer = analyzer; _meta = meta; _thumbnails = thumbnails; _progress = progress; _log = log;
        _history = history;
        _time = time ?? TimeProvider.System;
        // Each decoded image is held in memory, so the number in flight is capped.
        _parallelism = parallelism > 0 ? parallelism : Math.Clamp(Environment.ProcessorCount - 2, 1, 8);
    }

    public async Task<ScanSummary> RunAsync(ScanRequest request, string instanceId, CancellationToken ct = default)
    {
        _progress.ScanStarted(instanceId);
        var startedUtc = _time.GetUtcNow().UtcDateTime;
        var began = _time.GetTimestamp();
        try
        {
            // Without this check a mistyped folder would look like an empty one and every record would be pruned.
            var missing = new[] { request.PrimaryPath, request.SecondaryPath }
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && !Directory.Exists(p));
            if (string.IsNullOrWhiteSpace(request.PrimaryPath) || missing is not null)
            {
                _log?.Log(instanceId, "Error", $"Scan not started: folder not found: {missing ?? "(none given)"}");
                return new ScanSummary(0, 0, 0, 0, 0);
            }

            // Only once the folders are known to be there: a mistyped path must not cost the earlier results.
            if (request.StartOver)
            {
                var forgotten = await _repo.ClearAsync(ct);
                await _thumbnails.ClearAsync(ct);
                _log?.Log(instanceId, "Info", $"Starting over: forgot {forgotten} files recorded by earlier scans");
            }

            // A file reachable from both folders is scanned once, as part of the primary set.
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var primary = Enumerate(request.PrimaryPath, request.Recursive, claimed);
            var secondary = string.IsNullOrWhiteSpace(request.SecondaryPath) ? [] : Enumerate(request.SecondaryPath, request.Recursive, claimed);
            _progress.SetTotals(instanceId, primary.Count, secondary.Count);
            _log?.Log(instanceId, "Info", $"Scanning {primary.Count + secondary.Count} files");

            // What a file took to read in earlier scans says how long this one will be: that much for each
            // file it has not seen before. The files it has seen are mostly skipped, in next to no time.
            if (_history is not null)
            {
                var seen = (await _repo.GetAllAsync(ct)).Select(p => PhotoPath.Key(p.SourcePath)).ToHashSet();
                var unseen = primary.Concat(secondary).Count(f => !seen.Contains(PhotoPath.Key(f)));
                _progress.Expect(instanceId, ScanEstimate.SecondsPerFileRead(await _history.RecentAsync(ct: ct)) * unseen);
            }

            // Identical files have identical sizes, so a video whose size no other video shares cannot have a
            // duplicate. Only the others are read in full; videos are large and most of them are unique.
            var sharedVideoSizes = primary.Concat(secondary).Where(MediaFiles.IsVideo)
                .GroupBy(SizeOf).Where(g => g.Key > 0 && g.Count() > 1).Select(g => g.Key).ToHashSet();

            var run = new Run(instanceId, primary.Count + secondary.Count, sharedVideoSizes);
            await ProcessAsync(primary, request.PrimaryPath, PhotoSet.Primary, run, ct);
            await ProcessAsync(secondary, request.SecondaryPath!, PhotoSet.Secondary, run, ct);

            var pruned = 0;
            foreach (var stale in (await _repo.GetAllAsync(ct)).Where(p => !run.Touched.ContainsKey(p.Id)))
            {
                await _repo.DeleteAsync(stale.Id, ct);
                pruned++;
            }

            var summary = new ScanSummary(run.Total, run.Analyzed, run.Unchanged, run.Unreadable, pruned);
            _log?.Log(instanceId, "Info",
                $"Scan complete: {summary.Analyzed} analyzed, {summary.Unchanged} unchanged, {summary.Unreadable} unreadable, {summary.Pruned} no longer present");
            if (_history is not null)
                await _history.AddAsync(new ScanRecord
                {
                    StartedUtc = startedUtc, Seconds = _time.GetElapsedTime(began).TotalSeconds,
                    Total = summary.Total, Read = summary.Analyzed + summary.Unreadable, Unchanged = summary.Unchanged
                }, ct);
            return summary;
        }
        finally
        {
            _progress.ScanCompleted(instanceId);
        }
    }

    private static long SizeOf(string file)
    {
        try { return new FileInfo(file).Length; }
        catch (IOException) { return -1; }
    }

    private static List<string> Enumerate(string root, bool recursive, HashSet<string> claimed)
        => PhotoFileEnumerator.Enumerate(root, recursive).Where(f => claimed.Add(PhotoPath.Key(f))).ToList();

    private Task ProcessAsync(List<string> files, string root, PhotoSet set, Run run, CancellationToken ct)
        => Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = ct }, async (file, token) =>
        {
            try
            {
                await ProcessFileAsync(file, root, set, run, token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref run.Unreadable);
                _log?.Log(run.InstanceId, "Warn", $"Could not read {file}: {ex.Message}");
                // The file is there but could not be opened this time; an earlier record of it stays.
                if (await _repo.GetByPathAsync(file, token) is { } known) run.Touched[known.Id] = true;
            }
            _progress.IncrementProcessed(run.InstanceId, set == PhotoSet.Primary);
            var done = Interlocked.Increment(ref run.Done);
            if (done % 100 == 0) _log?.Log(run.InstanceId, "Info", $"Processed {done}/{run.Total} files");
        });

    private async Task ProcessFileAsync(string file, string root, PhotoSet set, Run run, CancellationToken ct)
    {
        var info = new FileInfo(file);
        var isVideo = MediaFiles.IsVideo(file);
        // An empty file (a transfer that never completed) is the same as every other empty file without
        // being a copy of any of them, so it is recorded but never matched.
        var needsHash = info.Length > 0 && (!isVideo || run.SharedVideoSizes.Contains(info.Length));
        var existing = await _repo.GetByPathAsync(file, ct);
        if (existing is not null && existing.AnalysisVersion == AnalysisVersion
            && existing.FileSizeBytes == info.Length && existing.FileModifiedUtc == info.LastWriteTimeUtc
            && (existing.ContentHash is not null || !needsHash))
        {
            if (existing.Set != set || existing.ScanRoot != root)
            {
                existing.Set = set;
                existing.ScanRoot = root;
                await _repo.AddOrUpdateAsync(existing, ct);
            }
            run.Touched[existing.Id] = true;
            Interlocked.Increment(ref run.Unchanged);
            return;
        }

        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        var photo = new Photo
        {
            Id = existing?.Id ?? PhotoId.New(),
            SourcePath = file,
            FileName = Path.GetFileName(file),
            FileSizeBytes = info.Length,
            FileModifiedUtc = info.LastWriteTimeUtc,
            ScanRoot = root,
            ContentHash = needsHash ? await _hash.ComputeHashAsync(stream, ct) : null,
            AnalysisVersion = AnalysisVersion,
            Set = set,
            IsKept = existing?.IsKept ?? false
        };

        if (isVideo)
        {
            // Videos are matched as identical files only, so there is nothing to decode.
            photo.Format = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
            Interlocked.Increment(ref run.Analyzed);
        }
        else try
        {
            stream.Position = 0;
            var analysis = await _analyzer.AnalyzeAsync(stream, ct);
            photo.Width = analysis.Width;
            photo.Height = analysis.Height;
            photo.Format = analysis.Format;
            photo.EncodedQuality = analysis.EncodedQuality;
            photo.PerceptualHash = analysis.PerceptualHash.ToString("X16");
            photo.Signature = analysis.Signature;
            await _thumbnails.SaveAsync(photo.ContentHash!, analysis.ThumbnailJpeg, ct);
            Interlocked.Increment(ref run.Analyzed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Recorded anyway: a file that cannot be decoded can still be matched as an identical file.
            Interlocked.Increment(ref run.Unreadable);
            _log?.Log(run.InstanceId, "Warn", $"Could not decode {file}: {ex.Message}");
        }

        stream.Position = 0;
        await _meta.ExtractAsync(photo, stream, ct);
        await _repo.AddOrUpdateAsync(photo, ct);
        run.Touched[photo.Id] = true;
    }

    private sealed class Run(string instanceId, int total, HashSet<long> sharedVideoSizes)
    {
        public readonly string InstanceId = instanceId;
        public readonly int Total = total;
        public readonly HashSet<long> SharedVideoSizes = sharedVideoSizes;
        public readonly ConcurrentDictionary<PhotoId, bool> Touched = new();
        public int Done, Analyzed, Unchanged, Unreadable;
    }
}
