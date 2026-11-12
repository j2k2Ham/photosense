using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Domain;

/// <summary>A photo record's id.</summary>
public class PhotoIdTests
{
    [Test]
    public async Task New_Generates_Unique(CancellationToken ct)
    {
        var a = PhotoId.New();
        var b = PhotoId.New();
        await Assert.That(b).IsNotEqualTo(a);
    }

    [Test]
    public async Task ToString_Not_Empty(CancellationToken ct)
    {
        var id = PhotoId.New();
        await Assert.That(string.IsNullOrWhiteSpace(id.ToString())).IsFalse();
    }
}
