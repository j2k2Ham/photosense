using System.Text;
using System.Text.Json;
using Trovolve.Infrastructure.Locations;

namespace Trovolve.Tests.Infrastructure;

/// <summary>A location's keys, passwords and tokens as both phones write them before sealing them away.</summary>
public class LocationSecretsFormatTests
{
    private static readonly Dictionary<string, string> Secrets = new() { ["accessKey"] = "AKIA0000", ["secret"] = "s3crét \"quoted\"" };

    [Test]
    public async Task Secrets_Are_Written_As_They_Always_Were_And_Read_Back_Whole()
    {
        var written = LocationSecretsFormat.Write(Secrets);

        // Byte for byte what the phones wrote before, so keys kept by an earlier version still open.
        await Assert.That(written).IsEquivalentTo(JsonSerializer.SerializeToUtf8Bytes(Secrets), CollectionOrdering.Matching);
        await Assert.That(LocationSecretsFormat.Read(written)).IsEquivalentTo(Secrets);
        await Assert.That(LocationSecretsFormat.Read(LocationSecretsFormat.Write(new Dictionary<string, string>()))).IsEmpty();
    }

    [Test]
    public async Task Bytes_That_Say_Nothing_Are_No_Secrets_And_Anything_Else_Is_Refused()
    {
        await Assert.That(LocationSecretsFormat.Read(Encoding.UTF8.GetBytes("null"))).IsEmpty();
        await Assert.That(() => LocationSecretsFormat.Read(Encoding.UTF8.GetBytes("not json"))).Throws<JsonException>();
    }
}
