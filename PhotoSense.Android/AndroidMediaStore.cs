using Android.Content;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using PhotoSense.Infrastructure.Library;
using AndroidUri = Android.Net.Uri;

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

    public IReadOnlyList<MediaEntry> Entries() => [.. Read(Images, video: false), .. Read(Videos, video: true)];

    private IEnumerable<MediaEntry> Read(AndroidUri collection, bool video)
    {
        string[] columns = [IBaseColumns.Id, MediaStore.IMediaColumns.RelativePath, MediaStore.IMediaColumns.DisplayName, MediaStore.IMediaColumns.Size, MediaStore.IMediaColumns.DateModified];
        using var rows = Resolver.Query(collection, columns, null, null, null);
        if (rows is null) yield break;
        while (rows.MoveToNext())
            yield return new MediaEntry($"{(video ? "video" : "image")}/{rows.GetLong(0)}", rows.GetString(1) ?? "", rows.GetString(2) ?? "", rows.GetLong(3),
                DateTimeOffset.FromUnixTimeSeconds(rows.GetLong(4)).UtcDateTime, video);
    }

    // "image/12" is the twelfth picture; "video/7", a video.
    private static AndroidUri UriOf(string id)
    {
        var (kind, number) = (id[..id.IndexOf('/')], long.Parse(id[(id.IndexOf('/') + 1)..]));
        return ContentUris.WithAppendedId(kind == "video" ? Videos : Images, number);
    }

    public Stream Open(string id)
    {
        var uri = UriOf(id);
        // The file as it was taken, place and all, which Android hands out only to an app allowed to see where photos were taken.
        try { uri = MediaStore.SetRequireOriginal(uri)!; }
        catch (Java.Lang.UnsupportedOperationException) { }
        var file = Resolver.OpenFileDescriptor(uri, "r") ?? throw new FileNotFoundException("The photo is no longer on the phone.");
        return new FileStream(new SafeFileHandle(file.DetachFd(), ownsHandle: true), FileAccess.Read);
    }

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

    // Whether PhotoSense made all of them itself, and so may change them without asking.
    private bool Mine(IReadOnlyList<AndroidUri> uris) => uris.All(uri =>
    {
        using var row = Resolver.Query(uri, [MediaStore.IMediaColumns.OwnerPackageName], null, null, null);
        return row?.MoveToFirst() == true && row.GetString(0) == context.PackageName;
    });
}
