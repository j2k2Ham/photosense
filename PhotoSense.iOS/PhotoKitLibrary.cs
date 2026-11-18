using System.Runtime.InteropServices;
using AVFoundation;
using CoreLocation;
using ImageIO;
using ObjCRuntime;
using Photos;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.ValueObjects;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.iOS;

/// <summary>The phone's photo library through Apple's photo framework, as much of it as the app has been allowed to see.</summary>
/// <param name="dataFolder">Where the app keeps the ids of the albums and folders it made, so that the person's own are never touched.</param>
public sealed class PhotoKitLibrary(string dataFolder) : IPhotoLibrary
{
    private readonly string _made = Path.Combine(dataFolder, "albums.txt");

    // The framework's code for a change the person said no to (PHPhotosError.userCancelled).
    private const nint SaidNo = 3072;

    // A fetch from iCloud that goes this long without getting any further is given up: the phone may have lost its connection.
    private static readonly TimeSpan CloudSilence = TimeSpan.FromSeconds(60);

    /// <summary>Every picture and video the app has been allowed to see, oldest first.</summary>
    public IReadOnlyList<LibraryItem> Items()
    {
        using var options = new PHFetchOptions { SortDescriptors = [new NSSortDescriptor("creationDate", true)] };
        var found = PHAsset.FetchAssets(options);
        var items = new List<LibraryItem>((int)found.Count);
        for (nint i = 0; i < found.Count; i++)
        {
            if (found[i] is not PHAsset asset) continue;
            var files = new List<LibraryFile>();
            foreach (var resource in PHAssetResource.GetAssetResources(asset))
                if (KindOf(resource.ResourceType) is { } kind) files.Add(new LibraryFile(kind, resource.OriginalFilename, SizeOf(resource)));
            var created = asset.CreationDate is { } taken ? (DateTime)taken : DateTime.UnixEpoch;
            items.Add(new LibraryItem(asset.LocalIdentifier, created, asset.ModificationDate is { } changed ? (DateTime)changed : created, files, asset.Favorite,
                asset.MediaSubtypes.HasFlag(PHAssetMediaSubtype.Screenshot), string.IsNullOrEmpty(asset.BurstIdentifier) ? null : asset.BurstIdentifier));
        }
        return items;
    }

    /// <summary>One file of a picture or video as it was taken, fetched from iCloud first when it is kept there.</summary>
    public async Task<Stream> OpenAsync(string id, LibraryFileKind kind, CancellationToken ct)
    {
        var found = PHAsset.FetchAssetsUsingLocalIdentifiers([id], null);
        var resource = found.Count > 0 && found[0] is PHAsset asset
            ? PHAssetResource.GetAssetResources(asset).FirstOrDefault(r => KindOf(r.ResourceType) == kind)
            : null;
        if (resource is null) throw new FileNotFoundException("The picture or video is no longer in the photo library.", id);

        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(resource.OriginalFilename));
        var written = new TaskCompletionSource<NSError?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stalled.CancelAfter(CloudSilence);
        var kept = false;
        try
        {
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                // One kept in iCloud only is downloaded first. Each piece that arrives shows it is still coming.
                using var options = new PHAssetResourceRequestOptions { NetworkAccessAllowed = true };
                var request = PHAssetResourceManager.DefaultManager.RequestData(resource, options, data =>
                {
                    output.Write(data.ToArray());
                    stalled.CancelAfter(CloudSilence);
                }, error => written.TrySetResult(error));
                NSError? failed;
                // A fetch given up on still ends with its own answer, after which nothing more arrives.
                await using (stalled.Token.Register(() => PHAssetResourceManager.DefaultManager.CancelDataRequest(request)))
                    failed = await written.Task;
                ct.ThrowIfCancellationRequested();
                if (stalled.IsCancellationRequested)
                    throw new IOException($"{resource.OriginalFilename} stopped arriving from iCloud. Check the phone's connection and try again.");
                if (failed is not null)
                    throw new IOException($"{resource.OriginalFilename} could not be read from the photo library: {failed.LocalizedDescription}");
            }
            var read = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.DeleteOnClose);
            kept = true;
            return read;
        }
        finally
        {
            // A fetch that did not finish leaves nothing behind.
            if (!kept) File.Delete(file);
        }
    }

    /// <summary>The albums PhotoSense has made, empty ones too.</summary>
    public IReadOnlyList<LibraryAlbum> Albums()
    {
        var known = Look();
        var albums = new List<LibraryAlbum>();
        foreach (var (key, album) in known.Albums)
        {
            var assets = PHAsset.FetchAssets(album, null);
            var ids = new List<string>((int)assets.Count);
            for (nint i = 0; i < assets.Count; i++)
                if (assets[i] is PHAsset asset) ids.Add(asset.LocalIdentifier);
            albums.Add(new LibraryAlbum(known.Paths[key], ids));
        }
        return albums;
    }

    /// <summary>Makes every change given at once, after iOS asks the person when it asks: all of them, or none.</summary>
    public void Change(LibraryChange change)
    {
        var known = Look();
        var assets = Assets([.. change.Add.Select(a => a.Item), .. change.Take.Select(t => t.Item), .. change.Delete]);
        var made = new List<string>();
        // All in one block: the framework makes all of it or none, and asks the person once.
        var done = PHPhotoLibrary.SharedPhotoLibrary.PerformChangesAndWait(() =>
        {
            var albums = new Dictionary<string, PHAssetCollectionChangeRequest>();
            var folders = new Dictionary<string, PHCollectionListChangeRequest>();
            foreach (var take in change.Take.GroupBy(t => Key(t.Album)))
                if (known.Albums.GetValueOrDefault(take.Key) is { } album)
                    AlbumChange(take.Key, album).RemoveAssets([.. take.Select(t => assets.GetValueOrDefault(t.Item)).OfType<PHAsset>()]);
            foreach (var add in change.Add.GroupBy(a => Key(a.Album)))
                AlbumFor(add.First().Album).AddAssets([.. add.Select(a => assets.GetValueOrDefault(a.Item)).OfType<PHAsset>()]);
            if (change.Delete.Count > 0) PHAssetChangeRequest.DeleteAssets([.. change.Delete.Select(assets.GetValueOrDefault).OfType<PHAsset>()]);

            PHAssetCollectionChangeRequest AlbumChange(string key, PHAssetCollection album)
                => albums.TryGetValue(key, out var request) ? request : albums[key] = PHAssetCollectionChangeRequest.ChangeRequest(album)!;

            // The album at a path, made with the folders it is in when it is not there yet.
            PHAssetCollectionChangeRequest AlbumFor(IReadOnlyList<string> path)
            {
                var key = Key(path);
                if (known.Albums.GetValueOrDefault(key) is { } album) return AlbumChange(key, album);
                if (albums.TryGetValue(key, out var request)) return request;
                request = albums[key] = PHAssetCollectionChangeRequest.CreateAssetCollection(path[^1]);
                made.Add(request.PlaceholderForCreatedAssetCollection.LocalIdentifier);
                if (path.Count > 1) FileInto([.. path.Take(path.Count - 1)], request.PlaceholderForCreatedAssetCollection);
                return request;
            }

            PHCollectionListChangeRequest FolderFor(IReadOnlyList<string> path)
            {
                var key = Key(path);
                if (folders.TryGetValue(key, out var request)) return request;
                if (known.Folders.GetValueOrDefault(key) is { } folder) return folders[key] = PHCollectionListChangeRequest.ChangeRequest(folder)!;
                request = folders[key] = PHCollectionListChangeRequest.CreateAssetCollection(path[^1]);
                made.Add(request.PlaceholderForCreatedCollectionList.LocalIdentifier);
                if (path.Count > 1) FileInto([.. path.Take(path.Count - 1)], request.PlaceholderForCreatedCollectionList);
                return request;
            }

            // The bindings take only collections already made; the framework takes one made in this block as well.
            void FileInto(IReadOnlyList<string> folder, PHObjectPlaceholder child)
            {
                using var children = NSArray.FromNSObjects(child);
                Send(FolderFor(folder).Handle, Selector.GetHandle("addChildCollections:"), children.Handle);
            }
        }, out var error);
        if (!done)
            throw new IOException(error?.Code == SaidNo ? "You chose not to let PhotoSense make this change to your photos." : $"The photo library did not make the change: {error?.LocalizedDescription}");
        if (made.Count > 0) File.AppendAllLines(_made, made);
    }

    /// <summary>
    /// Writes a smaller copy of a video as HEVC with AVFoundation, fetched from iCloud first when it is kept there, and
    /// keeps it as a new item with the original's date, place and favorite mark. The original is left as it is. iOS's
    /// export picks its own bitrate for the frame size chosen; at the smallest size it writes H.264, as iOS has no HEVC
    /// preset of that size.
    /// </summary>
    public async Task<long> CompressAsync(string id, VideoTarget target, IProgress<double> progress, CancellationToken ct)
    {
        var found = PHAsset.FetchAssetsUsingLocalIdentifiers([id], null);
        if (found.Count == 0 || found[0] is not PHAsset original || original.MediaType != PHAssetMediaType.Video)
            throw new IOException("The video is no longer in the photo library.");
        var video = await VideoAsync(original, ct);
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mov");
        try
        {
            using var export = new AVAssetExportSession(video, PresetFor(original, target))
            {
                OutputUrl = NSUrl.FromFilename(file), OutputFileType = AVFileTypes.QuickTimeMovie.GetConstant()!, ShouldOptimizeForNetworkUse = false,
            };
            using (ct.Register(export.CancelExport))
            {
                var exporting = export.ExportTaskAsync();
                while (!exporting.IsCompleted)
                {
                    await Task.WhenAny(exporting, Task.Delay(500, CancellationToken.None));
                    progress.Report(export.Progress);
                }
                await exporting;
            }
            ct.ThrowIfCancellationRequested();
            if (export.Status != AVAssetExportSessionStatus.Completed)
                throw new IOException($"The phone could not compress the video: {export.Error?.LocalizedDescription}");
            var bytes = new FileInfo(file).Length;
            await KeepAsync(original, file);
            return bytes;
        }
        // The exported file is the app's own: nothing of it stays.
        finally { File.Delete(file); }
    }

    // The video as iOS plays it, edits and all, fetched from iCloud when it is kept there.
    private static async Task<AVAsset> VideoAsync(PHAsset original, CancellationToken ct)
    {
        var got = new TaskCompletionSource<AVAsset>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var options = new PHVideoRequestOptions { NetworkAccessAllowed = true, DeliveryMode = PHVideoRequestOptionsDeliveryMode.HighQualityFormat };
        var request = PHImageManager.DefaultManager.RequestAVAsset(original, options, (asset, _, _) =>
        {
            if (asset is null) got.TrySetException(new IOException("The video could not be fetched from iCloud. Check the phone's connection and try again."));
            else got.TrySetResult(asset);
        });
        await using (ct.Register(() =>
        {
            PHImageManager.DefaultManager.CancelImageRequest(request);
            got.TrySetCanceled(ct);
        }))
            return await got.Task;
    }

    // iOS's presets by frame: the original's own size, 1080 lines as HEVC, or 720 lines.
    private static AVAssetExportSessionPreset PresetFor(PHAsset original, VideoTarget target)
    {
        var shortSide = Math.Min(target.Width, target.Height);
        if (shortSide >= Math.Min((int)original.PixelWidth, (int)original.PixelHeight)) return AVAssetExportSessionPreset.HevcHighestQuality;
        return shortSide <= 720 ? AVAssetExportSessionPreset.Preset1280x720 : AVAssetExportSessionPreset.Hevc1920x1080;
    }

    // The copy, as a new item of the library with the original's date, place and favorite mark.
    private static Task KeepAsync(PHAsset original, string file)
    {
        var kept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(() =>
        {
            var copy = PHAssetCreationRequest.CreationRequestForAsset();
            copy.AddResource(PHAssetResourceType.Video, NSUrl.FromFilename(file), null);
            copy.CreationDate = original.CreationDate;
            copy.Location = original.Location;
            copy.Favorite = original.Favorite;
        }, (done, error) =>
        {
            if (done) kept.TrySetResult();
            else kept.TrySetException(new IOException(error?.Code == SaidNo ? "You chose not to let PhotoSense add the smaller copy." : $"The photo library would not keep the smaller copy: {error?.LocalizedDescription}"));
        });
        return kept.Task;
    }

    /// <summary>Marks a picture or video a favorite in Photos, or takes the mark away.</summary>
    public void SetFavorite(string id, bool favorite)
    {
        var asset = AssetOf(id);
        var done = PHPhotoLibrary.SharedPhotoLibrary.PerformChangesAndWait(() => PHAssetChangeRequest.ChangeRequest(asset).Favorite = favorite, out var error);
        if (!done)
            throw new IOException(error?.Code == SaidNo ? "You chose not to let PhotoSense change this photo." : $"The photo library did not change it: {error?.LocalizedDescription}");
    }

    /// <summary>
    /// Gives pictures and videos the dates and places asked, all together, after iOS asks. Photos keeps them with each
    /// item; the files are left as they are.
    /// </summary>
    public void ChangeDetails(IReadOnlyList<(string Id, DateTime? Taken, PlaceChange? Place)> changes)
    {
        var assets = changes.Select(c => (Asset: AssetOf(c.Id), c.Taken, c.Place)).ToList();
        var done = PHPhotoLibrary.SharedPhotoLibrary.PerformChangesAndWait(() =>
        {
            foreach (var (asset, taken, place) in assets)
            {
                var request = PHAssetChangeRequest.ChangeRequest(asset);
                // The date is as the clock showed it where it was taken; Photos keeps a moment, read in this phone's own zone.
                if (taken is { } when) request.CreationDate = (NSDate)DateTime.SpecifyKind(when, DateTimeKind.Local);
                if (place is not null) request.Location = place.Latitude is { } lat && place.Longitude is { } lon ? new CLLocation(lat, lon) : null;
            }
        }, out var error);
        if (!done)
            throw new IOException(error?.Code == SaidNo ? "You chose not to let PhotoSense change these photos." : $"The photo library did not change them: {error?.LocalizedDescription}");
    }

    /// <summary>
    /// A copy of a picture or video as Photos shows it, edits and all, in the app's temporary folder, without where it
    /// was taken and the camera's details as asked. A picture keeps its format and is not encoded again; a video is
    /// re-written as it is, not encoded again. The copy shared before is deleted first.
    /// </summary>
    public async Task<SharedCopy> CopyForSharingAsync(string id, LibraryFileKind kind, bool removeLocation, bool removeCamera, CancellationToken ct)
    {
        var asset = AssetOf(id);
        var name = PHAssetResource.GetAssetResources(asset).FirstOrDefault(r => KindOf(r.ResourceType) == kind)?.OriginalFilename ?? "Photo";
        var folder = Path.Combine(Path.GetTempPath(), "shared");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        return kind == LibraryFileKind.Photo
            ? await PictureCopyAsync(asset, Path.Combine(folder, Path.GetFileNameWithoutExtension(name)), removeLocation, removeCamera, ct)
            : await VideoCopyAsync(asset, Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + ".mov"), removeLocation, removeCamera, ct);
    }

    // The picture's own bytes written again with its details less those asked, by ImageIO, which copies the image
    // itself as it is.
    private static async Task<SharedCopy> PictureCopyAsync(PHAsset asset, string named, bool removeLocation, bool removeCamera, CancellationToken ct)
    {
        var (data, type) = await PictureAsync(asset, ct);
        using var source = CGImageSource.FromData(data) ?? throw new IOException("The picture could not be read.");
        type = source.TypeIdentifier ?? type;
        var file = named + ExtensionOf(type);
        var details = source.CopyMetadata(0, (NSDictionary?)null) is { } read ? new CGMutableImageMetadata(read) : new CGMutableImageMetadata();
        foreach (var tag in details.GetTags() ?? Array.Empty<CGImageMetadataTag>())
            if ($"{tag.Prefix}:{tag.Name}" is var path && (removeLocation && SharingDetails.IsPlace(path) || removeCamera && SharingDetails.IsCamera(path)))
                details.RemoveTag(null, new NSString(path));
        using var destination = CGImageDestination.Create(NSUrl.FromFilename(file), type, 1) ?? throw new IOException("The copy could not be made.");
        var options = new CGCopyImageSourceOptions { Metadata = details, NullableMergeMetadata = false, NullableShouldExcludeGps = removeLocation };
        if (!destination.CopyImageSource(source, options, out var error))
            throw new IOException($"The copy could not be made: {error?.LocalizedDescription}");
        return new SharedCopy(file, MimeOf(type));
    }

    // The picture as Photos shows it, with its kind, fetched from iCloud when it is kept there.
    private static async Task<(NSData Data, string Type)> PictureAsync(PHAsset asset, CancellationToken ct)
    {
        var got = new TaskCompletionSource<(NSData, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var options = new PHImageRequestOptions { NetworkAccessAllowed = true, Version = PHImageRequestOptionsVersion.Current, DeliveryMode = PHImageRequestOptionsDeliveryMode.HighQualityFormat };
        var request = PHImageManager.DefaultManager.RequestImageDataAndOrientation(asset, options, (data, type, _, _) =>
        {
            if (data is null) got.TrySetException(new IOException("The picture could not be fetched from iCloud. Check the phone's connection and try again."));
            else got.TrySetResult((data, type ?? "public.jpeg"));
        });
        await using (ct.Register(() =>
        {
            PHImageManager.DefaultManager.CancelImageRequest(request);
            got.TrySetCanceled(ct);
        }))
            return await got.Task;
    }

    // The video as iOS plays it, written again as it is, with its details less those asked.
    private static async Task<SharedCopy> VideoCopyAsync(PHAsset asset, string file, bool removeLocation, bool removeCamera, CancellationToken ct)
    {
        var video = await VideoAsync(asset, ct);
        using var export = new AVAssetExportSession(video, AVAssetExportSessionPreset.Passthrough)
        {
            OutputUrl = NSUrl.FromFilename(file), OutputFileType = AVFileTypes.QuickTimeMovie.GetConstant()!,
            Metadata = [.. video.Metadata.Where(m => m.MetadataIdentifier?.ToString() is not { } said || !(removeLocation && SharingDetails.IsPlace(said) || removeCamera && SharingDetails.IsCamera(said)))],
        };
        using (ct.Register(export.CancelExport)) await export.ExportTaskAsync();
        ct.ThrowIfCancellationRequested();
        if (export.Status != AVAssetExportSessionStatus.Completed)
            throw new IOException($"The copy could not be made: {export.Error?.LocalizedDescription}");
        return new SharedCopy(file, "video/quicktime");
    }

    private static string ExtensionOf(string type) => type switch
    {
        "public.heic" => ".HEIC",
        "public.png" => ".PNG",
        "public.tiff" => ".TIF",
        "com.compuserve.gif" => ".GIF",
        _ => ".JPG",
    };

    private static string MimeOf(string type) => type switch
    {
        "public.heic" => "image/heic",
        "public.png" => "image/png",
        "public.tiff" => "image/tiff",
        "com.compuserve.gif" => "image/gif",
        _ => "image/jpeg",
    };

    private static PHAsset AssetOf(string id)
    {
        var found = PHAsset.FetchAssetsUsingLocalIdentifiers([id], null);
        return found.Count > 0 && found[0] is PHAsset asset ? asset : throw new IOException("The picture or video is no longer in the photo library.");
    }

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    private sealed record Known(Dictionary<string, PHAssetCollection> Albums, Dictionary<string, PHCollectionList> Folders, Dictionary<string, IReadOnlyList<string>> Paths);

    // The albums and folders PhotoSense made that are still there, by path.
    private Known Look()
    {
        var known = new Known([], [], []);
        var made = File.Exists(_made) ? File.ReadAllLines(_made).Where(l => l.Length > 0).ToHashSet() : [];
        if (made.Count == 0) return known;
        string[] ids = [.. made];
        var albums = PHAssetCollection.FetchAssetCollections(ids, null);
        for (nint i = 0; i < albums.Count; i++)
        {
            if (albums[i] is not PHAssetCollection album) continue;
            var path = PathOf(album, made);
            if (known.Albums.TryAdd(Key(path), album)) known.Paths[Key(path)] = path;
        }
        var lists = PHCollectionList.FetchCollectionLists(ids, null);
        for (nint i = 0; i < lists.Count; i++)
            if (lists[i] is PHCollectionList list) known.Folders.TryAdd(Key(PathOf(list, made)), list);
        return known;
    }

    // Its own name, after the names of the folders of PhotoSense's it is in.
    private static List<string> PathOf(PHCollection collection, HashSet<string> made)
    {
        var path = new List<string> { collection.LocalizedTitle ?? string.Empty };
        for (var at = collection; Parent(at) is { } parent && made.Contains(parent.LocalIdentifier); at = parent)
            path.Insert(0, parent.LocalizedTitle ?? string.Empty);
        return path;
    }

    private static PHCollectionList? Parent(PHCollection collection)
    {
        var parents = PHCollectionList.FetchCollectionLists(collection, null);
        return parents.Count > 0 ? parents[0] as PHCollectionList : null;
    }

    private static string Key(IReadOnlyList<string> path) => string.Join('\u001f', path);

    private static Dictionary<string, PHAsset> Assets(string[] ids)
    {
        var found = new Dictionary<string, PHAsset>();
        if (ids.Length == 0) return found;
        var fetched = PHAsset.FetchAssetsUsingLocalIdentifiers([.. ids.Distinct()], null);
        for (nint i = 0; i < fetched.Count; i++)
            if (fetched[i] is PHAsset asset) found[asset.LocalIdentifier] = asset;
        return found;
    }

    private static LibraryFileKind? KindOf(PHAssetResourceType type) => type switch
    {
        PHAssetResourceType.Photo => LibraryFileKind.Photo,
        PHAssetResourceType.Video => LibraryFileKind.Video,
        PHAssetResourceType.PairedVideo => LibraryFileKind.PairedVideo,
        _ => null,
    };

    // The framework does not say how big a file is in so many words; this is the one way to know without fetching it.
    private static long SizeOf(PHAssetResource resource) => resource.ValueForKey(new NSString("fileSize")) is NSNumber size ? size.Int64Value : -1;
}
