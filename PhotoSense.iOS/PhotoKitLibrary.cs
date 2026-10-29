using System.Runtime.InteropServices;
using ObjCRuntime;
using Photos;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.iOS;

/// <summary>The phone's photo library through Apple's photo framework, as much of it as the app has been allowed to see.</summary>
/// <param name="dataFolder">Where the app keeps the ids of the albums and folders it made, so that the person's own are never touched.</param>
public sealed class PhotoKitLibrary(string dataFolder) : IPhotoLibrary
{
    private readonly string _made = Path.Combine(dataFolder, "albums.txt");

    // The framework's code for a change the person said no to (PHPhotosError.userCancelled).
    private const nint SaidNo = 3072;

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
            items.Add(new LibraryItem(asset.LocalIdentifier, created, asset.ModificationDate is { } changed ? (DateTime)changed : created, files));
        }
        return items;
    }

    public Stream Open(string id, LibraryFileKind kind)
    {
        var found = PHAsset.FetchAssetsUsingLocalIdentifiers([id], null);
        var resource = found.Count > 0 && found[0] is PHAsset asset
            ? PHAssetResource.GetAssetResources(asset).FirstOrDefault(r => KindOf(r.ResourceType) == kind)
            : null;
        if (resource is null) throw new FileNotFoundException("The picture or video is no longer in the photo library.", id);

        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(resource.OriginalFilename));
        var written = new TaskCompletionSource<NSError?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // One kept in iCloud only is downloaded first.
        using var options = new PHAssetResourceRequestOptions { NetworkAccessAllowed = true };
        PHAssetResourceManager.DefaultManager.WriteData(resource, NSUrl.FromFilename(file), options, error => written.SetResult(error));
        if (written.Task.GetAwaiter().GetResult() is { } error)
            throw new IOException($"{resource.OriginalFilename} could not be read from the photo library: {error.LocalizedDescription}");
        return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.DeleteOnClose);
    }

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
