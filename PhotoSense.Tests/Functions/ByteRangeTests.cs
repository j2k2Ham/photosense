using PhotoSense.Functions.Api;
using Xunit;

namespace PhotoSense.Tests.Functions;

public class ByteRangeTests
{
    private const long FileLength = 1000;

    [Theory]
    [InlineData(null, 0, 999)]                 // no header: from the beginning
    [InlineData("", 0, 999)]
    [InlineData("   ", 0, 999)]
    [InlineData("bytes=0-", 0, 999)]
    [InlineData("bytes=200-", 200, 999)]
    [InlineData("bytes=200-299", 200, 299)]
    [InlineData("bytes=0-0", 0, 0)]
    [InlineData("bytes=999-", 999, 999)]       // the last byte
    [InlineData("bytes=900-5000", 900, 999)]   // an end past the file is cut back to it
    [InlineData("bytes=-100", 900, 999)]       // the last hundred bytes
    [InlineData("bytes=-5000", 0, 999)]        // more than there is: all of it
    [InlineData("BYTES=10-19", 10, 19)]
    [InlineData("  bytes= 10 - 19 ", 10, 19)]
    public void Reads_The_Part_Of_The_File_A_Player_Asked_For(string? header, long start, long end)
    {
        var range = ByteRange.Parse(header, FileLength);
        Assert.Equal(new ByteRange(start, end), range);
        Assert.Equal(end - start + 1, range!.Value.Length);
    }

    [Theory]
    [InlineData("bytes=1000-")]        // starts where the file ends
    [InlineData("bytes=5000-6000")]
    [InlineData("bytes=300-200")]      // ends before it starts
    [InlineData("bytes=-0")]           // the last zero bytes
    [InlineData("bytes=-")]
    [InlineData("bytes=abc-")]
    [InlineData("bytes=10-xyz")]
    [InlineData("bytes=-xyz")]
    [InlineData("bytes=+10-20")]
    [InlineData("bytes=10")]           // no dash
    [InlineData("bytes=0-1,5-9")]      // several ranges at once
    [InlineData("items=0-10")]         // not a byte range
    public void Refuses_What_It_Cannot_Serve(string header)
        => Assert.Null(ByteRange.Parse(header, FileLength));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_Empty_File_Has_No_Part_To_Send(long length)
        => Assert.Null(ByteRange.Parse(null, length));

    [Fact]
    public void One_Response_Never_Carries_More_Than_The_Limit()
    {
        const long big = 400L * 1024 * 1024;
        Assert.Equal(new ByteRange(0, ByteRange.MaxLength - 1), ByteRange.Parse(null, big));
        Assert.Equal(new ByteRange(1_000_000, 1_000_000 + ByteRange.MaxLength - 1), ByteRange.Parse("bytes=1000000-", big));
        Assert.Equal(new ByteRange(50, 149), ByteRange.Parse("bytes=50-", FileLength, maxLength: 100));
        Assert.Equal(new ByteRange(50, 59), ByteRange.Parse("bytes=50-59", FileLength, maxLength: 100)); // a short request stays short
    }

    [Fact]
    public void Says_Which_Part_Of_How_Much_It_Is_Sending()
        => Assert.Equal("bytes 200-299/1000", new ByteRange(200, 299).ContentRange(FileLength));
}
