using System.Net;
using System.Text.Json.Nodes;
using Trovolve.Domain.Entities;
using Trovolve.Infrastructure.Usage;

namespace Trovolve.Tests.Infrastructure;

/// <summary>Uses sent to the usage server as JSON, any failure said as one the rest of Trovolve can catch.</summary>
public class HttpUsageSenderTests
{
    private static readonly Uri Server = new("https://usage.example.com/uses");
    private static readonly Guid Install = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid Session = Guid.Parse("66666666-7777-4888-9999-000000000000");

    private static UsageReport Report() => new(Install, new UsageSource("server", "1.2.0.57", "Windows"),
        [new FeatureUse(7, TestClock.Day, "POST scan/start", 200, Session), new FeatureUse(8, TestClock.Day.AddSeconds(5), "GET trips", 402, Session)]);

    /// <summary>A network that never answers, until the request is given up.</summary>
    private sealed class SilentNetwork : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task The_Uses_Are_Posted_As_Json_Named_As_The_Server_Expects(CancellationToken ct)
    {
        var network = new FakeNetwork().On(HttpMethod.Post, Server.AbsoluteUri, _ => FakeNetwork.Status(HttpStatusCode.Accepted));
        using var http = new HttpClient(network);

        await new HttpUsageSender(http).SendAsync(Server, Report(), ct);

        var heard = network.Asked.Single();
        await Assert.That((heard.Headers["Content-Type"], heard.Headers["Content-Length"])).IsEqualTo(("application/json; charset=utf-8", heard.Bytes.Length.ToString()));
        var sent = JsonNode.Parse(heard.Body)!;
        await Assert.That((sent["install"]!.GetValue<Guid>(), sent["source"]!["program"]!.GetValue<string>(), sent["source"]!["version"]!.GetValue<string>(), sent["source"]!["system"]!.GetValue<string>()))
            .IsEqualTo((Install, "server", "1.2.0.57", "Windows"));
        var uses = sent["uses"]!.AsArray();
        await Assert.That(uses.Select(u => (u!["sequence"]!.GetValue<long>(), u["feature"]!.GetValue<string>(), u["status"]!.GetValue<int>(), u["session"]!.GetValue<Guid>(), u["at"]!.GetValue<DateTimeOffset>())))
            .IsEquivalentTo([(7L, "POST scan/start", 200, Session, TestClock.Day), (8L, "GET trips", 402, Session, TestClock.Day.AddSeconds(5))], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Server_That_Does_Not_Take_Them_Or_Cannot_Be_Reached_Is_Said_As_An_IOException(CancellationToken ct)
    {
        var refusing = new FakeNetwork().On(HttpMethod.Post, Server.AbsoluteUri, _ => FakeNetwork.Status(HttpStatusCode.ServiceUnavailable));
        using (var http = new HttpClient(refusing))
        {
            var refused = await Assert.ThrowsAsync<IOException>(() => new HttpUsageSender(http).SendAsync(Server, Report(), ct));
            await Assert.That(refused!.Message).IsEqualTo("The usage server answered 503 and did not take the uses.");
        }

        using (var http = new HttpClient(new FakeNetwork { Down = true }))
        {
            var down = await Assert.ThrowsAsync<IOException>(() => new HttpUsageSender(http).SendAsync(Server, Report(), ct));
            await Assert.That(down!.Message).IsEqualTo("The usage server could not be reached: No route to host.");
        }
    }

    [Test]
    public async Task A_Server_That_Never_Answers_Is_Given_Up_But_A_Caller_Who_Stops_Is_Not_Told_It_Failed(CancellationToken ct)
    {
        using (var http = new HttpClient(new SilentNetwork()) { Timeout = TimeSpan.FromMilliseconds(50) })
        {
            var late = await Assert.ThrowsAsync<IOException>(() => new HttpUsageSender(http).SendAsync(Server, Report(), ct));
            await Assert.That(late!.Message).IsEqualTo("The usage server did not answer in time.");
        }

        using (var http = new HttpClient(new SilentNetwork()))
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stop.CancelAfter(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAsync<OperationCanceledException>(() => new HttpUsageSender(http).SendAsync(Server, Report(), stop.Token));
        }
    }
}
