using PhotoSense.Infrastructure.Thumbnails;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public sealed class FileSystemThumbnailStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("photosense-thumbs-");
    private const string Hash = "AB12CD34";

    public void Dispose() => _root.Delete(true);

    [Fact]
    public async Task Returns_What_Was_Saved_And_Nothing_For_An_Unknown_Hash()
    {
        var store = new FileSystemThumbnailStore(Path.Combine(_root.FullName, "not-created-yet"));
        Assert.Null(await store.GetAsync(Hash));
        await store.SaveAsync(Hash, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await store.GetAsync(Hash));
    }

    [Fact]
    public async Task The_First_Thumbnail_Saved_For_A_Hash_Stays()
    {
        var store = new FileSystemThumbnailStore(_root.FullName);
        await store.SaveAsync(Hash, [1]);
        await store.SaveAsync(Hash, [2]);
        Assert.Equal(new byte[] { 1 }, await store.GetAsync(Hash));
        Assert.Single(Directory.GetFiles(_root.FullName, "*", SearchOption.AllDirectories)); // no temporary files left behind
    }

    [Fact]
    public async Task Saving_The_Same_Hash_From_Many_Threads_Is_Safe()
    {
        var store = new FileSystemThumbnailStore(_root.FullName);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.SaveAsync(Hash, [7, 7, 7]))));
        Assert.Equal(new byte[] { 7, 7, 7 }, await store.GetAsync(Hash));
        Assert.Single(Directory.GetFiles(_root.FullName, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../../etc/passwd")]
    [InlineData("AB")]
    public async Task Only_A_Content_Hash_Is_Accepted_As_A_Key(string key)
    {
        var store = new FileSystemThumbnailStore(_root.FullName);
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(key, [1]));
    }
}
