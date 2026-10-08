using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Domain;

public class PhotoIdTests
{
    [Test]
    public async Task New_Generates_Unique()
    {
        var a = PhotoId.New();
        var b = PhotoId.New();
        await Assert.That(b).IsNotEqualTo(a);
    }

    [Test]
    public async Task ToString_Not_Empty()
    {
        var id = PhotoId.New();
        await Assert.That(string.IsNullOrWhiteSpace(id.ToString())).IsFalse();
    }
}
