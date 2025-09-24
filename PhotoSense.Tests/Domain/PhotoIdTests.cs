using PhotoSense.Domain.ValueObjects;
using Xunit;

namespace PhotoSense.Tests.Domain;

public class PhotoIdTests
{
    [Fact]
    public void New_Generates_Unique()
    {
        var a = PhotoId.New();
        var b = PhotoId.New();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToString_Not_Empty()
    {
        var id = PhotoId.New();
        Assert.False(string.IsNullOrWhiteSpace(id.ToString()));
    }
}
