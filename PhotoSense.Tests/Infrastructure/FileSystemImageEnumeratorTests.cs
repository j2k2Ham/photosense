using PhotoSense.Infrastructure.Scanning;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public class FileSystemImageEnumeratorTests
{
    [Fact]
    public void Enumerate_Recursive_Finds_Nested()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "nested"));
            File.WriteAllBytes(Path.Combine(root.FullName, "a.jpg"), new byte[]{1});
            File.WriteAllBytes(Path.Combine(nested.FullName, "b.png"), new byte[]{1});
            var files = FileSystemImageEnumerator.Enumerate(root.FullName, true).Select(Path.GetFileName).OrderBy(x=>x).ToList();
            Assert.Equal(new[]{"a.jpg","b.png"}, files);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Enumerate_Invalid_ReturnsEmpty()
    {
        var files = FileSystemImageEnumerator.Enumerate("nope_dir_123", false).ToList();
        Assert.Empty(files);
    }
}
