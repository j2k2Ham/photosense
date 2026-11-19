using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Provider;
using AndroidX.Media3.Common;
using AndroidX.Media3.Common.Audio;
using AndroidX.Media3.Effect;
using AndroidX.Media3.Transformer;
using Microsoft.Win32.SafeHandles;
using PhotoSense.Domain.Services;
using Android.Webkit;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Library;
using AndroidUri = Android.Net.Uri;
using MediaStore = Android.Provider.MediaStore;
using Path = System.IO.Path;
using Stream = System.IO.Stream;
using Presentation = AndroidX.Media3.Effect.Presentation;

namespace PhotoSense.AndroidApp;

/// <summary>
/// The phone's pictures and videos as Android's media store keeps them. A photo another app made is changed only once
/// the person says yes to Android's own question, asked once for all the photos changed together; those PhotoSense
/// made, as the self-check does, it may change itself.
/// </summary>
public sealed class AndroidMediaStore(Context context) : IMediaStore
{
    private const string Declined = "You chose not to let PhotoSense change these photos.";
    private static readonly AndroidUri Images = MediaStore.Images.Media.GetContentUri(MediaStore.VolumeExternal)!;
    private static readonly AndroidUri Videos = MediaStore.Video.Media.GetContentUri(MediaStore.VolumeExternal)!;

    private ContentResolver Resolver => context.ContentResolver!;

    /// <summary>Every picture and video the app has been allowed to see.</summary>
    public IReadOnlyList<MediaEntry> Entries() => [.. Read(Images, video: false), .. Read(Videos, video: true)];

    private IEnumerable<MediaEntry> Read(AndroidUri collection, bool video)
    {
        string[] columns = [IBaseColumns.Id, MediaStore.IMediaColumns.RelativePath, MediaStore.IMediaColumns.DisplayName, MediaStore.IMediaColumns.Size, MediaStore.IMediaColumns.DateModified,
            MediaStore.IMediaColumns.IsFavorite];
        using var rows = Resolver.Query(collection, columns, null, null, null);
        if (rows is null) yield break;
        while (rows.MoveToNext())
            yield return new MediaEntry($"{(video ? "video" : "image")}/{rows.GetLong(0)}", rows.GetString(1) ?? "", rows.GetString(2) ?? "", rows.GetLong(3),
                DateTimeOffset.FromUnixTimeSeconds(rows.GetLong(4)).UtcDateTime, video, rows.GetInt(5) == 1);
    }

    // "image/12" is the twelfth picture; "video/7", a video.
    private static AndroidUri UriOf(string id)
    {
        var (kind, number) = (id[..id.IndexOf('/')], long.Parse(id[(id.IndexOf('/') + 1)..]));
        return ContentUris.WithAppendedId(kind == "video" ? Videos : Images, number);
    }

    /// <summary>
    /// The file as it was taken, place and all, which Android hands out only to an app allowed to see where photos were
    /// taken; for any other app, the file as Android gives it.
    /// </summary>
    private static Android.Net.Uri WithItsPlace(Android.Net.Uri uri)
    {
        try { return MediaStore.SetRequireOriginal(uri)!; }
        catch (Java.Lang.UnsupportedOperationException) { return uri; }
    }

    /// <inheritdoc />
    public Stream Open(string id)
    {
        var uri = WithItsPlace(UriOf(id));
        var file = Resolver.OpenFileDescriptor(uri, "r") ?? throw new FileNotFoundException("The photo is no longer on the phone.");
        return new FileStream(new SafeFileHandle(file.DetachFd(), ownsHandle: true), FileAccess.Read);
    }

    /// <summary>Makes the changes, after Android asks the person once for the moves and once for the bin: all of them, or none.</summary>
    public void Change(MediaChange change)
    {
        if (change.Move.Count > 0) Move(change.Move);
        if (change.Trash.Count > 0) Trash(change.Trash);
    }

    private void Move(IReadOnlyList<(string Id, string Folder, string Name)> moves)
    {
        var uris = moves.Select(m => UriOf(m.Id)).ToList();
        if (!Mine(uris) && !MainActivity.Ask(MediaStore.CreateWriteRequest(Resolver, uris)!)) throw new IOException(Declined);
        var done = new List<(AndroidUri Uri, string Folder, string Name)>();
        try
        {
            foreach (var ((_, folder, name), uri) in moves.Zip(uris))
            {
                var was = Where(uri);
                Put(uri, folder, name);
                done.Add((uri, was.Folder, was.Name));
            }
        }
        // All of them or none: those already moved go back.
        catch (Exception e) when (e is Java.Lang.Exception or IOException)
        {
            foreach (var (uri, folder, name) in done) Put(uri, folder, name);
            throw new IOException($"The phone would not move the photos: {e.Message}");
        }
    }

    private void Trash(IReadOnlyList<string> ids)
    {
        var uris = ids.Select(UriOf).ToList();
        if (Mine(uris))
        {
            foreach (var uri in uris)
            {
                var trashed = new ContentValues();
                trashed.Put(MediaStore.IMediaColumns.IsTrashed, 1);
                Resolver.Update(uri, trashed, null, null);
            }
            return;
        }
        // Android puts them in the bin itself once the person says yes.
        if (!MainActivity.Ask(MediaStore.CreateTrashRequest(Resolver, uris, true)!)) throw new IOException("You chose not to delete these photos.");
    }

    private void Put(AndroidUri uri, string folder, string name)
    {
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.RelativePath, folder);
        values.Put(MediaStore.IMediaColumns.DisplayName, name);
        if (Resolver.Update(uri, values, null, null) != 1) throw new IOException($"{name} could not be moved.");
    }

    private (string Folder, string Name) Where(AndroidUri uri)
    {
        using var row = Resolver.Query(uri, [MediaStore.IMediaColumns.RelativePath, MediaStore.IMediaColumns.DisplayName], null, null, null);
        if (row?.MoveToFirst() != true) throw new IOException("The photo is no longer on the phone.");
        return (row.GetString(0) ?? "", row.GetString(1) ?? "");
    }

    /// <summary>
    /// Re-encodes a video as HEVC at the frame and bitrate given, with Google's Media3 Transformer, and keeps the copy
    /// in the same folder with the original's date, as "<c>name (smaller).mp4</c>". The original is left as it is.
    /// </summary>
    public async Task<long> CompressAsync(string id, VideoTarget target, IProgress<double> progress, CancellationToken ct)
    {
        var source = UriOf(id);
        var (folder, name) = Where(source);
        var taken = TakenOn(source);
        var encoded = Path.Combine(context.CacheDir!.AbsolutePath, $"compressing-{Guid.NewGuid():N}.mp4");
        try
        {
            await EncodeAsync(source, encoded, target, progress, ct);
            return Keep(encoded, folder, Path.GetFileNameWithoutExtension(name) + " (smaller).mp4", taken);
        }
        // The encoded file is the app's own until it is kept: nothing of it stays.
        finally { File.Delete(encoded); }
    }

    // Media3 runs on the main thread's looper: it is made, started, asked how far it has got and cancelled there.
    private async Task EncodeAsync(AndroidUri source, string encoded, VideoTarget target, IProgress<double> progress, CancellationToken ct)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var main = new Handler(Looper.MainLooper!);
        Transformer? transformer = null;
        main.Post(() =>
        {
            try
            {
                var settings = new VideoEncoderSettings.Builder().SetBitrate((int)Math.Min(int.MaxValue, target.VideoBitsPerSecond))!.Build()!;
                var encoders = new DefaultEncoderFactory.Builder(context).SetRequestedVideoEncoderSettings(settings)!.Build()!;
                var made = new Transformer.Builder(context).SetVideoMimeType(MimeTypes.VideoH265)!.SetEncoderFactory(encoders)!.AddListener(new Ended(done))!.Build()!;
                transformer = made;
                var frame = Presentation.CreateForWidthAndHeight(target.Width, target.Height, Presentation.LayoutScaleToFit)!;
                var item = new EditedMediaItem.Builder(MediaItem.FromUri(source)!).SetEffects(new Effects(new List<IAudioProcessor>(), new List<IEffect> { frame }))!.Build()!;
                made.Start(item, encoded);
            }
            catch (Java.Lang.Exception e) { done.TrySetException(new IOException($"The phone could not compress the video: {e.Message}")); }
        });
        using var stopping = ct.Register(() => main.Post(() =>
        {
            transformer?.Cancel();
            done.TrySetCanceled(ct);
        }));
        while (!done.Task.IsCompleted)
        {
            await Task.WhenAny(done.Task, Task.Delay(500, CancellationToken.None));
            main.Post(() =>
            {
                var holder = new ProgressHolder();
                if (transformer?.GetProgress(holder) == Transformer.ProgressStateAvailable) progress.Report(holder.Progress / 100d);
            });
        }
        await done.Task;
    }

    // The copy, written into the same folder as a video PhotoSense made, hidden from other apps until it is all there.
    private long Keep(string encoded, string folder, string name, long? taken)
    {
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, name);
        values.Put(MediaStore.IMediaColumns.MimeType, "video/mp4");
        values.Put(MediaStore.IMediaColumns.RelativePath, folder);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        if (taken is { } when) values.Put(MediaStore.IMediaColumns.DateTaken, when);
        var uri = Resolver.Insert(Videos, values) ?? throw new IOException("The phone would not keep the smaller copy.");
        try
        {
            using (var into = Resolver.OpenOutputStream(uri) ?? throw new IOException("The phone would not keep the smaller copy."))
            using (var from = File.OpenRead(encoded))
                from.CopyTo(into);
            var kept = new ContentValues();
            kept.Put(MediaStore.IMediaColumns.IsPending, 0);
            Resolver.Update(uri, kept, null, null);
            return new FileInfo(encoded).Length;
        }
        // Not kept whole: nothing of it is left.
        catch (Exception e) when (e is Java.Lang.Exception or IOException)
        {
            Resolver.Delete(uri, null, null);
            throw new IOException($"The phone would not keep the smaller copy: {e.Message}");
        }
    }

    /// <summary>
    /// Adds a picture or video to a folder as a new file, hidden from other apps until it is all there; dated as
    /// given, and, for a JPEG, PNG or WebP, with its date and place written in its file too.
    /// </summary>
    public async Task<string> ImportAsync(string folder, string name, Stream content, DateTime? taken, PlaceChange? place, CancellationToken ct)
    {
        var video = MediaFiles.IsVideo(name);
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, name);
        values.Put(MediaStore.IMediaColumns.MimeType, MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? (video ? "video/mp4" : "image/jpeg"));
        values.Put(MediaStore.IMediaColumns.RelativePath, folder);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        if (taken is { } when) values.Put(MediaStore.IMediaColumns.DateTaken, new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Local)).ToUnixTimeMilliseconds());
        AndroidUri? uri = null;
        var whole = false;
        try
        {
            uri = Resolver.Insert(video ? Videos : Images, values) ?? throw new IOException($"The phone would not take {name} back.");
            await using (var into = Resolver.OpenOutputStream(uri) ?? throw new IOException($"The phone would not take {name} back."))
                await content.CopyToAsync(into, ct);
            if (!video && extension is "jpg" or "jpeg" or "png" or "webp" && (taken is not null || place is not null)) WriteDetails(uri, name, taken, place);
            var kept = new ContentValues();
            kept.Put(MediaStore.IMediaColumns.IsPending, 0);
            Resolver.Update(uri, kept, null, null);
            whole = true;
            return $"{(video ? "video" : "image")}/{ContentUris.ParseId(uri)}";
        }
        catch (Java.Lang.Exception e) { throw new IOException($"The phone would not take {name} back: {e.Message}"); }
        // Not taken whole: nothing of it is left.
        finally { if (!whole && uri is not null) Resolver.Delete(uri, null, null); }
    }

    // When the video was taken, in milliseconds since 1970, as the media store has it.
    private long? TakenOn(AndroidUri uri)
    {
        using var row = Resolver.Query(uri, [MediaStore.IMediaColumns.DateTaken], null, null, null);
        return row?.MoveToFirst() == true && !row.IsNull(0) ? row.GetLong(0) : null;
    }

    // Says the copy is made, or why not, once Media3 has finished.
    private sealed class Ended(TaskCompletionSource done) : Java.Lang.Object, Transformer.IListener
    {
        public void OnCompleted(Composition? composition, ExportResult? exportResult) => done.TrySetResult();

        public void OnError(Composition? composition, ExportResult? exportResult, ExportException? exportException)
            => done.TrySetException(new IOException($"The phone could not compress the video: {exportException?.Message}"));
    }

    /// <summary>Marks a picture or video a favorite, or takes the mark away, after Android asks the person when it is another app's.</summary>
    public void SetFavorite(string id, bool favorite)
    {
        var uri = UriOf(id);
        if (Mine([uri]))
        {
            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.IsFavorite, favorite ? 1 : 0);
            Resolver.Update(uri, values, null, null);
            return;
        }
        if (!MainActivity.Ask(MediaStore.CreateFavoriteRequest(Resolver, new List<AndroidUri> { uri }, favorite)!)) throw new IOException("You chose not to let PhotoSense change this photo.");
    }

    /// <summary>
    /// Gives pictures and videos the dates and places asked, all together, after Android asks when they are another
    /// app's. A JPEG, PNG or WebP has its own details written again, the picture untouched; a date is also Android's
    /// record of when it was taken, which is all a video or a HEIC gets.
    /// </summary>
    public void ChangeDetails(IReadOnlyList<(string Id, DateTime? Taken, PlaceChange? Place)> changes)
    {
        var uris = changes.Select(c => UriOf(c.Id)).ToList();
        if (!Mine(uris) && !MainActivity.Ask(MediaStore.CreateWriteRequest(Resolver, uris)!)) throw new IOException(Declined);
        foreach (var ((id, taken, place), uri) in changes.Zip(uris))
        {
            var (_, name) = Where(uri);
            if (!id.StartsWith("video/", StringComparison.Ordinal) && Path.GetExtension(name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp")
                WriteDetails(uri, name, taken, place);
            if (taken is not { } when) continue;
            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DateTaken, new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Local)).ToUnixTimeMilliseconds());
            Resolver.Update(uri, values, null, null);
        }
    }

    // A picture's own date and place written again in its file, through its descriptor; the picture is not touched.
    private void WriteDetails(AndroidUri uri, string name, DateTime? taken, PlaceChange? place)
    {
        try
        {
            using var file = Resolver.OpenFileDescriptor(uri, "rw") ?? throw new IOException($"{name} could not be opened.");
            var details = new ExifInterface(file.FileDescriptor!);
            if (taken is { } when)
                foreach (var tag in new[] { ExifInterface.TagDatetime, ExifInterface.TagDatetimeOriginal, ExifInterface.TagDatetimeDigitized })
                    details.SetAttribute(tag, DetailRules.ExifDate(when));
            if (place is { Latitude: { } lat, Longitude: { } lon })
            {
                var (latitude, north) = DetailRules.ExifDegrees(lat, "N", "S");
                var (longitude, east) = DetailRules.ExifDegrees(lon, "E", "W");
                details.SetAttribute(ExifInterface.TagGpsLatitude, latitude);
                details.SetAttribute(ExifInterface.TagGpsLatitudeRef, north);
                details.SetAttribute(ExifInterface.TagGpsLongitude, longitude);
                details.SetAttribute(ExifInterface.TagGpsLongitudeRef, east);
            }
            else if (place is not null)
                foreach (var tag in SharingDetails.ExifPlace) details.SetAttribute(tag, null);
            details.SaveAttributes();
        }
        catch (Java.IO.IOException ex) { throw new IOException($"{name} could not be changed: {ex.Message}"); }
    }

    /// <summary>
    /// A copy of a picture or video for sharing, in the app's cache, without where it was taken and the camera's details
    /// as asked; the copy shared before is deleted first. A JPEG, PNG or WebP keeps its format, with its details taken
    /// out by <c>ExifInterface</c>; any other picture, a HEIC among them, is made again as a JPEG with only the details
    /// kept. A video is re-written as an MP4 as it is, not encoded again, keeping only where it was taken when asked.
    /// </summary>
    public Task<SharedCopy> CopyForSharingAsync(string id, bool removeLocation, bool removeCamera, CancellationToken ct) => Task.Run(() =>
    {
        var uri = UriOf(id);
        var (_, name) = Where(uri);
        var folder = Path.Combine(context.CacheDir!.AbsolutePath, "shared");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        try
        {
            return id.StartsWith("video/", StringComparison.Ordinal)
                ? VideoCopy(WithItsPlace(uri), Path.Combine(folder, Path.ChangeExtension(name, ".mp4")), removeLocation, ct)
                : PictureCopy(WithItsPlace(uri), Resolver.GetType(uri), folder, name, removeLocation, removeCamera);
        }
        catch (Java.Lang.Exception e) { throw new IOException($"The copy of {name} could not be made: {e.Message}"); }
    }, ct);

    private SharedCopy PictureCopy(AndroidUri source, string? mime, string folder, string name, bool removeLocation, bool removeCamera)
    {
        byte[] bytes;
        using (var from = Resolver.OpenInputStream(source) ?? throw new IOException($"{name} is no longer on the phone."))
        using (var read = new MemoryStream())
        {
            from.CopyTo(read);
            bytes = read.ToArray();
        }
        IEnumerable<string> leaveOut = [.. removeLocation ? SharingDetails.ExifPlace : [], .. removeCamera ? SharingDetails.ExifCamera : []];
        if (mime is "image/jpeg" or "image/png" or "image/webp")
        {
            var file = Path.Combine(folder, name);
            File.WriteAllBytes(file, bytes);
            var details = new ExifInterface(file);
            foreach (var detail in leaveOut) details.SetAttribute(detail, null);
            details.SaveAttributes();
            return new SharedCopy(file, mime);
        }
        // Android cannot write a HEIC's details: it is made again as a JPEG, upright, with what is kept copied across.
        var jpeg = Path.Combine(folder, Path.ChangeExtension(name, ".jpg"));
        using (var image = ImageDecoder.DecodeBitmap(ImageDecoder.CreateSource(Java.Nio.ByteBuffer.Wrap(bytes)), new InMemory()))
        using (var output = File.Create(jpeg))
            if (!image.Compress(Bitmap.CompressFormat.Jpeg!, 95, output)) throw new IOException($"{name} could not be made into a JPEG.");
        var was = new ExifInterface(new MemoryStream(bytes));
        var made = new ExifInterface(jpeg);
        foreach (var detail in SharingDetails.ExifWhen.Concat(SharingDetails.ExifPlace).Concat(SharingDetails.ExifCamera).Except(leaveOut))
            if (was.GetAttribute(detail) is { } value) made.SetAttribute(detail, value);
        made.SaveAttributes();
        return new SharedCopy(jpeg, "image/jpeg");
    }

    // A picture decoded whole into memory, where it can be written out again.
    private sealed class InMemory : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source) => decoder.Allocator = ImageDecoderAllocator.Software;
    }

    // Every sample of the video and its sound written into a new MP4 as it is. Android's muxer writes no details of its
    // own but the turn of the picture and, when asked, where it was taken.
    private SharedCopy VideoCopy(AndroidUri source, string file, bool removeLocation, CancellationToken ct)
    {
        using var extractor = new MediaExtractor();
        extractor.SetDataSource(context, source, null);
        var muxer = new MediaMuxer(file, MuxerOutputType.Mpeg4);
        try
        {
            var tracks = new Dictionary<int, int>();
            var largest = 1 << 20;
            for (var i = 0; i < extractor.TrackCount; i++)
            {
                var format = extractor.GetTrackFormat(i);
                if (format.GetString(MediaFormat.KeyMime) is not { } kind || !(kind.StartsWith("video/", StringComparison.Ordinal) || kind.StartsWith("audio/", StringComparison.Ordinal))) continue;
                extractor.SelectTrack(i);
                tracks[i] = muxer.AddTrack(format);
                if (format.ContainsKey(MediaFormat.KeyMaxInputSize)) largest = Math.Max(largest, format.GetInteger(MediaFormat.KeyMaxInputSize));
            }
            using (var told = new MediaMetadataRetriever())
            {
                told.SetDataSource(context, source);
                if (int.TryParse(told.ExtractMetadata(MetadataKey.VideoRotation), out var turned)) muxer.SetOrientationHint(turned);
                if (!removeLocation && SharingDetails.Iso6709(told.ExtractMetadata(MetadataKey.Location)) is { } at) muxer.SetLocation((float)at.Latitude, (float)at.Longitude);
            }
            muxer.Start();
            var buffer = Java.Nio.ByteBuffer.Allocate(largest)!;
            var sample = new MediaCodec.BufferInfo();
            for (int size; (size = extractor.ReadSampleData(buffer, 0)) >= 0; extractor.Advance())
            {
                ct.ThrowIfCancellationRequested();
                sample.Set(0, size, extractor.SampleTime, extractor.SampleFlags.HasFlag(MediaExtractorSampleFlags.Sync) ? MediaCodecBufferFlags.KeyFrame : MediaCodecBufferFlags.None);
                muxer.WriteSampleData(tracks[extractor.SampleTrackIndex], buffer, sample);
            }
            muxer.Stop();
            return new SharedCopy(file, "video/mp4");
        }
        finally { muxer.Release(); }
    }

    // Whether PhotoSense made all of them itself, and so may change them without asking.
    private bool Mine(IReadOnlyList<AndroidUri> uris) => uris.All(uri =>
    {
        using var row = Resolver.Query(uri, [MediaStore.IMediaColumns.OwnerPackageName], null, null, null);
        return row?.MoveToFirst() == true && row.GetString(0) == context.PackageName;
    });
}
