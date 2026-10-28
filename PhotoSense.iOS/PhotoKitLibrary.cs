using Photos;
using PhotoSense.Infrastructure.Library;

namespace PhotoSense.iOS;

/// <summary>The phone's photo library through Apple's photo framework, as much of it as the app has been allowed to see.</summary>
public sealed class PhotoKitLibrary : IPhotoLibrary
{
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
