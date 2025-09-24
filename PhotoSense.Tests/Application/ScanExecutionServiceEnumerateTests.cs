using PhotoSense.Application.Scanning.Services;
using Xunit;

namespace PhotoSense.Tests.Application;

public class ScanExecutionServiceEnumerateTests
{
    [Fact]
    public void EnumerateFiles_Filters_Supported_Extensions()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var allowed = new[]{"a.jpg","b.jpeg","c.png","d.gif","e.bmp","f.tiff","g.webp"};
            var disallowed = new[]{"h.txt","i.mov","j.raw"};
            foreach (var f in allowed) File.WriteAllBytes(Path.Combine(dir.FullName, f), new byte[]{1});
            foreach (var f in disallowed) File.WriteAllBytes(Path.Combine(dir.FullName, f), new byte[]{1});
            var files = ScanExecutionService.EnumerateFiles(dir.FullName, false).Select(Path.GetFileName).OrderBy(x=>x).ToList();
            Assert.Equal(allowed.OrderBy(x=>x), files);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void EnumerateFiles_ReturnsEmpty_When_InvalidRoot()
    {
        var files = ScanExecutionService.EnumerateFiles("nonexistent_root_123", false).ToList();
        Assert.Empty(files);
    }
}
