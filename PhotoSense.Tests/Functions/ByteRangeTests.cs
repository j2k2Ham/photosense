using PhotoSense.Functions.Api;

namespace PhotoSense.Tests.Functions;

public class ByteRangeTests
{
    private const long FileLength = 1000;

    [Test]
    [Arguments(null, 0, 999)]                 // no header: from the beginning
    [Arguments("", 0, 999)]
    [Arguments("   ", 0, 999)]
    [Arguments("bytes=0-", 0, 999)]
    [Arguments("bytes=200-", 200, 999)]
    [Arguments("bytes=200-299", 200, 299)]
    [Arguments("bytes=0-0", 0, 0)]
    [Arguments("bytes=999-", 999, 999)]       // the last byte
    [Arguments("bytes=900-5000", 900, 999)]   // an end past the file is cut back to it
    [Arguments("bytes=-100", 900, 999)]       // the last hundred bytes
    [Arguments("bytes=-5000", 0, 999)]        // more than there is: all of it
    [Arguments("BYTES=10-19", 10, 19)]
    [Arguments("  bytes= 10 - 19 ", 10, 19)]
    public async Task Reads_The_Part_Of_The_File_A_Player_Asked_For(string? header, long start, long end)
    {
        var range = ByteRange.Parse(header, FileLength);
        await Assert.That(range).IsEqualTo(new ByteRange(start, end));
        await Assert.That(range!.Value.Length).IsEqualTo(end - start + 1);
    }

    [Test]
    [Arguments("bytes=1000-")]        // starts where the file ends
    [Arguments("bytes=5000-6000")]
    [Arguments("bytes=300-200")]      // ends before it starts
    [Arguments("bytes=-0")]           // the last zero bytes
    [Arguments("bytes=-")]
    [Arguments("bytes=abc-")]
    [Arguments("bytes=10-xyz")]
    [Arguments("bytes=-xyz")]
    [Arguments("bytes=+10-20")]
    [Arguments("bytes=10")]           // no dash
    [Arguments("bytes=0-1,5-9")]      // several ranges at once
    [Arguments("items=0-10")]         // not a byte range
    public async Task Refuses_What_It_Cannot_Serve(string header)
        => await Assert.That(ByteRange.Parse(header, FileLength)).IsNull();

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task An_Empty_File_Has_No_Part_To_Send(long length)
        => await Assert.That(ByteRange.Parse(null, length)).IsNull();

    [Test]
    public async Task One_Response_Never_Carries_More_Than_The_Limit()
    {
        const long big = 400L * 1024 * 1024;
        await Assert.That(ByteRange.Parse(null, big)).IsEqualTo(new ByteRange(0, ByteRange.MaxLength - 1));
        await Assert.That(ByteRange.Parse("bytes=1000000-", big)).IsEqualTo(new ByteRange(1_000_000, 1_000_000 + ByteRange.MaxLength - 1));
        await Assert.That(ByteRange.Parse("bytes=50-", FileLength, maxLength: 100)).IsEqualTo(new ByteRange(50, 149));
        await Assert.That(ByteRange.Parse("bytes=50-59", FileLength, maxLength: 100)).IsEqualTo(new ByteRange(50, 59)); // a short request stays short
    }

    [Test]
    public async Task Says_Which_Part_Of_How_Much_It_Is_Sending()
        => await Assert.That(new ByteRange(200, 299).ContentRange(FileLength)).IsEqualTo("bytes 200-299/1000");
}
