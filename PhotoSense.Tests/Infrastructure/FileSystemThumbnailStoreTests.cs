using PhotoSense.Infrastructure.Thumbnails;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>Previews kept as files, by the hash of the file each shows.</summary>
public sealed class FileSystemThumbnailStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-thumbs-");
    private const string Hash = "AB12CD34";

    public void Dispose() => _root.Delete(true);

    [Test]
    public async Task Returns_What_Was_Saved_And_Nothing_For_An_Unknown_Hash(CancellationToken ct)
    {
        var store = new FileSystemThumbnailStore(Path.Combine(_root.FullName, "not-created-yet"));
        await Assert.That(await store.GetAsync(Hash, ct)).IsNull();
        await store.SaveAsync(Hash, [1, 2, 3], ct);
        await Assert.That(await store.GetAsync(Hash, ct)).IsEquivalentTo(new byte[] { 1, 2, 3 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_First_Thumbnail_Saved_For_A_Hash_Stays(CancellationToken ct)
    {
        var store = new FileSystemThumbnailStore(_root.FullName);
        await store.SaveAsync(Hash, [1], ct);
        await store.SaveAsync(Hash, [2], ct);
        await Assert.That(await store.GetAsync(Hash, ct)).IsEquivalentTo(new byte[] { 1 }, CollectionOrdering.Matching);
        await Assert.That(Directory.GetFiles(_root.FullName, "*", SearchOption.AllDirectories)).HasSingleItem(); // no temporary files left behind
    }

    [Test]
    public async Task Saving_The_Same_Hash_From_Many_Threads_Is_Safe(CancellationToken ct)
    {
        var store = new FileSystemThumbnailStore(_root.FullName);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.SaveAsync(Hash, [7, 7, 7], ct))));
        await Assert.That(await store.GetAsync(Hash, ct)).IsEquivalentTo(new byte[] { 7, 7, 7 }, CollectionOrdering.Matching);
        await Assert.That(Directory.GetFiles(_root.FullName, "*", SearchOption.AllDirectories)).HasSingleItem();
    }

    [Test]
    [Arguments("..")]
    [Arguments("../../etc/passwd")]
    [Arguments("AB")]
    public async Task Only_A_Content_Hash_Is_Accepted_As_A_Key(string key, CancellationToken ct)
    {
        var store = new FileSystemThumbnailStore(_root.FullName);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.GetAsync(key, ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SaveAsync(key, [1], ct));
    }

    [Test]
    public async Task A_Thumbnail_Whose_Place_Is_Already_Taken_Leaves_No_Temporary_File_Behind(CancellationToken ct)
    {
        // A folder under the thumbnail's name stands in for an identical thumbnail saved between the check and the move.
        var store = new FileSystemThumbnailStore(_root.FullName);
        Directory.CreateDirectory(Path.Combine(_root.FullName, Hash[..2], Hash + ".jpg"));

        await store.SaveAsync(Hash, [1, 2, 3], ct);

        await Assert.That(Directory.GetFiles(_root.FullName, "*", SearchOption.AllDirectories)).IsEmpty();
    }

    [Test]
    public async Task Clearing_Discards_Every_Preview_And_New_Ones_Can_Be_Saved_After(CancellationToken ct)
    {
        var folder = Path.Combine(_root.FullName, "thumbs");
        var store = new FileSystemThumbnailStore(folder);
        await store.ClearAsync(ct);                         // nothing saved yet, no folder even: nothing to do
        await store.SaveAsync(Hash, [1, 2, 3], ct);
        await store.SaveAsync("CD34EF56", [4], ct);

        await store.ClearAsync(ct);

        await Assert.That(await store.GetAsync(Hash, ct)).IsNull();
        await Assert.That(Directory.Exists(folder)).IsFalse();
        await store.SaveAsync(Hash, [7], ct);
        await Assert.That(await store.GetAsync(Hash, ct)).IsEquivalentTo(new byte[] { 7 }, CollectionOrdering.Matching);
    }
}
