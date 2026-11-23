using PhotoSense.Application.Plans;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Application.Removal;

/// <summary>A large video, with about what its copy would take at each quality; null where a copy is not worth making or its frame or length is not known.</summary>
/// <param name="Video">The video, as the scan recorded it.</param>
/// <param name="Estimates">About how many bytes the copy takes, by quality.</param>
public sealed record CompressibleVideo(Photo Video, IReadOnlyDictionary<CompressionQuality, long?> Estimates);

/// <summary>How far a compression has got.</summary>
public enum CompressionStage
{
    /// <summary>None has run since the app started.</summary>
    Idle,
    /// <summary>Running now.</summary>
    Running,
    /// <summary>Every video was compressed.</summary>
    Done,
    /// <summary>Cancelled; what was compressed before is kept.</summary>
    Stopped,
    /// <summary>A video could not be compressed; what was compressed before is kept.</summary>
    Failed,
}

/// <summary>A video compressed: what it took before, and what its copy takes.</summary>
/// <param name="Id">The original's record.</param>
/// <param name="FileName">The original's name.</param>
/// <param name="Before">What the original takes.</param>
/// <param name="After">What the copy takes.</param>
public sealed record CompressedVideo(PhotoId Id, string FileName, long Before, long After);

/// <summary>A compression as it stands.</summary>
/// <param name="Stage">How far it has got.</param>
/// <param name="Quality">The quality chosen.</param>
/// <param name="Total">How many videos it was given.</param>
/// <param name="Done">How many are compressed.</param>
/// <param name="Current">The video being compressed now; null when none is.</param>
/// <param name="Progress">How far through all of them, from 0 to 1.</param>
/// <param name="Results">Each video compressed.</param>
/// <param name="OriginalsDeleted">Once it has ended: whether the originals went to the phone's bin, after its question; null while running, or when none was compressed.</param>
/// <param name="Problem">Why it failed; null otherwise.</param>
public sealed record CompressionState(CompressionStage Stage, CompressionQuality Quality, int Total, int Done, string? Current, double Progress,
    IReadOnlyList<CompressedVideo> Results, bool? OriginalsDeleted, string? Problem)
{
    /// <summary>Nothing run yet.</summary>
    public static CompressionState Idle { get; } = new(CompressionStage.Idle, CompressionQuality.Balanced, 0, 0, null, 0, [], null, null);

    /// <summary>What the copies saved, all told.</summary>
    public long Saved => Results.Sum(r => r.Before - r.After);
}

/// <summary>
/// Compressing large videos on a phone, which is part of Pro: each is re-encoded by the phone as a smaller copy beside it
/// with its date and place, one at a time, and once they are done the originals go to the phone's Recently Deleted or bin
/// together, after its own question. Said no to, the originals and their copies are all kept. Compressing counts toward
/// no daily limit, as only Pro compresses.
/// </summary>
public sealed class VideoCompression
{
    private readonly CleanUpCategories _categories;
    private readonly IPhotoRepository _repo;
    private readonly IVideoCompressor _compressor;
    private readonly IPhotoChangesTogether _phone;
    private readonly Subscription _subscription;
    private readonly DailyRemovals _removals;
    private readonly Lock _gate = new();
    private CompressionState _state = CompressionState.Idle;
    private CancellationTokenSource? _stop;

    /// <summary>Compresses the phone's videos with <paramref name="compressor"/>, sending the originals to its bin through <paramref name="phone"/>.</summary>
    /// <param name="categories">Clean up's categories, whose large videos are offered.</param>
    /// <param name="repo">What the scans recorded.</param>
    /// <param name="compressor">The phone's own encoder.</param>
    /// <param name="phone">The phone's photos, which send the originals to its bin after its question.</param>
    /// <param name="subscription">The plan, as compressing is part of Pro.</param>
    /// <param name="removals">Where what the copies saved is noted, for Year in review; it counts toward no limit.</param>
    public VideoCompression(CleanUpCategories categories, IPhotoRepository repo, IVideoCompressor compressor, IPhotoChangesTogether phone, Subscription subscription,
        DailyRemovals removals)
    {
        _removals = removals;
        _categories = categories;
        _repo = repo;
        _compressor = compressor;
        _phone = phone;
        _subscription = subscription;
    }

    /// <summary>The compression as it stands now.</summary>
    public CompressionState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>Done once the compression running now has ended, originals and all; done at once when none is.</summary>
    public Task Finished { get; private set; } = Task.CompletedTask;

    /// <summary>The large videos, largest first, each with about what its copy would take at each quality.</summary>
    public async Task<IReadOnlyList<CompressibleVideo>> VideosAsync(CancellationToken ct)
        => [.. (await _categories.PhotosAsync(CleanUpCategory.LargeVideos, ct)).Select(v => new CompressibleVideo(v,
            Enum.GetValues<CompressionQuality>().ToDictionary(q => q, q => VideoTargets.Estimate(q, v.Width, v.Height, v.DurationSeconds, v.FileSizeBytes))))];

    /// <summary>
    /// Starts compressing the videos given at <paramref name="quality"/>, in the background, and answers how it stands.
    /// Videos whose frame the scan could not read are left out.
    /// </summary>
    /// <param name="ids">The videos.</param>
    /// <param name="quality">The quality chosen.</param>
    /// <param name="ct">Stops it before it starts; once started, only <see cref="Cancel"/> stops it.</param>
    /// <exception cref="InvalidOperationException">Not on Pro; none of them is a video PhotoSense can compress; or one is running already.</exception>
    public async Task<CompressionState> StartAsync(IReadOnlyCollection<PhotoId> ids, CompressionQuality quality, CancellationToken ct)
    {
        if (!(await _subscription.CurrentAsync(ct)).Pro) throw new InvalidOperationException("Video compression is part of Pro.");
        var videos = new List<Photo>();
        foreach (var id in ids)
            if (await _repo.GetAsync(id, ct) is { IsVideo: true, Width: > 0, Height: > 0 } video) videos.Add(video);
        if (videos.Count == 0) throw new InvalidOperationException("None of these is a video PhotoSense can compress. Scan again, then try again.");
        CancellationTokenSource stop;
        lock (_gate)
        {
            if (_state.Stage == CompressionStage.Running) throw new InvalidOperationException("Videos are being compressed already. Wait for them, or cancel.");
            _stop?.Dispose();
            _stop = stop = new CancellationTokenSource();
            _state = new CompressionState(CompressionStage.Running, quality, videos.Count, 0, videos[0].FileName, 0, [], null, null);
        }
        // It runs on after the request that started it: its own token, which Cancel uses, is what stops it.
        Finished = Task.Run(() => RunAsync(videos, quality, stop.Token), CancellationToken.None);
        return State;
    }

    /// <summary>Stops the compression running, after the video it is on; what was compressed before is kept. Answers how it stands.</summary>
    public CompressionState Cancel()
    {
        lock (_gate)
        {
            if (_state.Stage == CompressionStage.Running) _stop!.Cancel();
            return _state;
        }
    }

    private async Task RunAsync(List<Photo> videos, CompressionQuality quality, CancellationToken ct)
    {
        var results = new List<CompressedVideo>();
        var done = new List<Photo>();
        var stage = CompressionStage.Failed;
        string? problem = null;
        bool? deleted = null;
        try
        {
            try
            {
                foreach (var video in videos)
                {
                    ct.ThrowIfCancellationRequested();
                    var at = done.Count;
                    Update(s => s with { Current = video.FileName, Done = at, Progress = (double)at / videos.Count });
                    var report = new Reporter(p => Update(s => s with { Progress = (at + Math.Clamp(p, 0, 1)) / videos.Count }));
                    var after = await _compressor.CompressAsync(video.SourcePath, VideoTargets.For(quality, video.Width, video.Height), report, ct);
                    results.Add(new CompressedVideo(video.Id, video.FileName, video.FileSizeBytes, after));
                    done.Add(video);
                }
                stage = CompressionStage.Done;
            }
            catch (OperationCanceledException) { stage = CompressionStage.Stopped; }
            // The phone could not read or write a video: what was compressed before is kept.
            catch (IOException ex) { problem = ex.Message; }
            if (done.Count > 0) deleted = await EraseOriginalsAsync(done);
            // The originals gone, what their copies saved is freed.
            if (deleted == true) await _removals.FreedAsync(results.Sum(r => r.Before - r.After), CancellationToken.None);
        }
        finally
        {
            // However it ended, it is no longer said to be running.
            var count = done.Count;
            Update(_ => new CompressionState(stage, quality, videos.Count, count, null, (double)count / videos.Count, [.. results], deleted, problem));
        }
    }

    // The originals of the videos compressed go to the phone's bin together, after its question.
    private async Task<bool> EraseOriginalsAsync(List<Photo> originals)
    {
        try { _phone.EraseAll([.. originals.Select(o => o.SourcePath)]); }
        // The person said no, or the phone could not: the originals and their copies are both kept.
        catch (IOException) { return false; }
        // Gone to the phone's bin: their records go too, whatever happens now.
        await _repo.DeleteAllAsync([.. originals.Select(o => o.Id)], CancellationToken.None);
        return true;
    }

    private void Update(Func<CompressionState, CompressionState> change)
    {
        lock (_gate) _state = change(_state);
    }

    // Reports straight to the state: Progress<T> would hand each report to another thread, out of order.
    private sealed class Reporter(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
