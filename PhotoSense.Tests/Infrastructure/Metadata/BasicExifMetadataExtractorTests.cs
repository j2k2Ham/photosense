using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Tests.Infrastructure.Metadata;

public class BasicExifMetadataExtractorTests
{
    [Test]
    public async Task Extract_Does_Not_Throw_On_NonImage()
    {
        var extractor = new BasicExifMetadataExtractor();
        var photo = new Photo { SourcePath = "x", FileName = "y", FileSizeBytes = 1, Set = PhotoSet.Primary };
        await using var ms = new MemoryStream(new byte[]{1,2,3,4}); // not a real image
        await extractor.ExtractAsync(photo, ms);
        await Assert.That(photo.TakenOn).IsNull();
    }

    [Test]
    public async Task A_Scan_Being_Stopped_Is_Not_Taken_For_A_File_Without_Details()
    {
        var photo = new Photo { SourcePath = "x", FileName = "y", FileSizeBytes = 1, Set = PhotoSet.Primary };
        await using var stopping = new StoppingStream();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new BasicExifMetadataExtractor().ExtractAsync(photo, stopping));
    }

    private sealed class StoppingStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new OperationCanceledException();
        public override int Read(Span<byte> buffer) => throw new OperationCanceledException();
    }
}
