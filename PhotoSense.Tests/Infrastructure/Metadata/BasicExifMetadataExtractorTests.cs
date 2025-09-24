using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Domain.Entities;
using Xunit;

namespace PhotoSense.Tests.Infrastructure.Metadata;

public class BasicExifMetadataExtractorTests
{
    [Fact]
    public async Task Extract_Does_Not_Throw_On_NonImage()
    {
        var extractor = new BasicExifMetadataExtractor();
        var photo = new Photo { SourcePath = "x", FileName = "y", FileSizeBytes = 1, Set = PhotoSet.Primary };
        await using var ms = new MemoryStream(new byte[]{1,2,3,4}); // not a real image
        await extractor.ExtractAsync(photo, ms);
        Assert.Null(photo.TakenOn);
    }
}
