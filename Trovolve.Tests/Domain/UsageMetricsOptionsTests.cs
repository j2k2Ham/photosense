using Trovolve.Domain.Configuration;

namespace Trovolve.Tests.Domain;

/// <summary>Whether uses are sent, and where: only when on, and only over HTTPS or to this machine itself.</summary>
public class UsageMetricsOptionsTests
{
    [Test]
    [Arguments(false, "https://usage.example.com/uses", null)]
    [Arguments(true, "", null)]
    [Arguments(true, "not an address", null)]
    [Arguments(true, "https://usage.example.com/uses", "https://usage.example.com/uses")]
    [Arguments(true, "http://127.0.0.1:5005/uses", "http://127.0.0.1:5005/uses")]
    [Arguments(true, "http://usage.example.com/uses", null)]
    [Arguments(true, "ftp://usage.example.com/uses", null)]
    public async Task Uses_Are_Sent_Only_When_On_And_Only_Over_Https_Or_To_This_Machine(bool enabled, string endpoint, string? sentTo)
    {
        var options = new UsageMetricsOptions { Enabled = enabled, Endpoint = endpoint };
        await Assert.That(options.SendTo()?.AbsoluteUri).IsEqualTo(sentTo);
    }

    [Test]
    public async Task Usage_Metrics_Are_Off_Unless_The_Settings_Turn_Them_On()
    {
        var options = new UsageMetricsOptions();
        await Assert.That((options.Enabled, options.Endpoint, options.SendEvery, options.BatchSize, options.MaxKept, UsageMetricsOptions.Section))
            .IsEqualTo((false, "", TimeSpan.FromHours(1), 500, 10_000, "UsageMetrics"));
    }
}
