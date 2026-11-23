using System.Text.Json.Nodes;
using PhotoSense.Application.Transfer;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Tests.Application;

/// <summary>The plan a phone tells a computer of inside its offer.</summary>
public class PlanMessageTests
{
    [Test]
    public async Task A_Plan_Is_Read_Back_As_It_Was_Written(CancellationToken ct)
    {
        var trial = new Entitlement(PlanTier.Trial, PlanKind.Yearly, TestClock.Day, TestClock.Day.AddDays(7));
        var lifetime = new Entitlement(PlanTier.Pro, PlanKind.Lifetime);

        await Assert.That(PlanMessage.Read(PlanMessage.Write(trial))).IsEqualTo(trial);
        await Assert.That(PlanMessage.Read(PlanMessage.Write(lifetime))).IsEqualTo(lifetime);
        await Assert.That(PlanMessage.Write(trial).ToJsonString()).Contains("\"tier\":\"trial\",\"kind\":\"yearly\"");
    }

    [Test]
    [Arguments(null)]
    [Arguments("\"pro\"")]
    [Arguments("{}")]
    [Arguments("{\"tier\":\"pro\"}")]
    [Arguments("{\"tier\":2,\"kind\":\"yearly\"}")]
    [Arguments("{\"tier\":\"2\",\"kind\":\"yearly\"}")]
    [Arguments("{\"tier\":\"free, trial\",\"kind\":\"yearly\"}")]
    [Arguments("{\"tier\":\"gold\",\"kind\":\"yearly\"}")]
    [Arguments("{\"tier\":\"pro\",\"kind\":\"weekly\"}")]
    [Arguments("{\"tier\":\"pro\",\"kind\":\"yearly\",\"until\":\"soon\"}")]
    [Arguments("{\"tier\":\"pro\",\"kind\":\"yearly\",\"started\":5}")]
    public async Task What_Is_Not_A_Plan_Is_Read_As_None(string? json, CancellationToken ct)
    {
        await Assert.That(PlanMessage.Read(json is null ? null : JsonNode.Parse(json))).IsNull();
    }
}
