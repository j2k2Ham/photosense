using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trovolve.Application.Usage;
using Trovolve.Domain.Configuration;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Services;

namespace Trovolve.Tests.Application;

/// <summary>Which features are used, noted and sent only when usage metrics are on, and kept until the server takes them.</summary>
public class UsageMetricsTests
{
    private const string Server = "https://usage.example.com/uses";
    private static readonly UsageSource Source = new("server", "1.2.0.57", "Windows");

    /// <summary>The usage server as the test sets it up: what it was sent, and whether it takes it.</summary>
    private sealed class FakeUsageServer : IUsageSender
    {
        /// <summary>Every report sent, in order, with where it went.</summary>
        public List<(Uri To, UsageReport Report)> Sent { get; } = [];

        /// <summary>The server cannot be reached.</summary>
        public bool Down { get; set; }

        /// <summary>When set, each sending waits for it before the server answers.</summary>
        public TaskCompletionSource? Hold { get; set; }

        /// <summary>Done once a report has reached the server, before it answers.</summary>
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Done once the server has taken a report.</summary>
        public TaskCompletionSource Taken { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public async Task SendAsync(Uri to, UsageReport report, CancellationToken ct)
        {
            Reached.TrySetResult();
            if (Hold is { } hold) await hold.Task.WaitAsync(ct);
            if (Down) throw new IOException("The usage server could not be reached: No route to host.");
            Sent.Add((to, report));
            Taken.TrySetResult();
        }
    }

    private sealed record Setup(UsageMetrics Usage, MemoryUsageStore Store, FakeUsageServer Server, TestClock Clock, ListLogger<UsageMetrics> Log);

    private static Setup Make(bool enabled = true, string endpoint = Server, int batch = 500, int maxKept = 10_000)
    {
        var store = new MemoryUsageStore();
        var server = new FakeUsageServer();
        var clock = new TestClock();
        var log = new ListLogger<UsageMetrics>();
        var options = Options.Create(new UsageMetricsOptions { Enabled = enabled, Endpoint = endpoint, BatchSize = batch, MaxKept = maxKept });
        return new Setup(new UsageMetrics(store, server, options, Source, clock, log), store, server, clock, log);
    }

    [Test]
    public async Task While_Off_Nothing_Is_Noted_Kept_Or_Sent(CancellationToken ct)
    {
        var (usage, store, server, _, _) = Make(enabled: false);
        await usage.RecordAsync("POST scan/start", 200, ct);

        await Assert.That((usage.On, await store.CountAsync(ct), await usage.SendDueAsync(ct), server.Sent.Count)).IsEqualTo((false, 0, 0, 0));
        // The computer's sending ends at once, and waits for nothing.
        await Assert.That(usage.SendWhileRunningAsync(ct).IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task On_With_No_Server_To_Send_To_Uses_Are_Kept_And_Wait(CancellationToken ct)
    {
        var (usage, store, server, _, _) = Make(endpoint: "");
        await usage.RecordAsync("POST scan/start", 200, ct);

        await Assert.That((await store.CountAsync(ct), await usage.SendDueAsync(ct), server.Sent.Count)).IsEqualTo((1, 0, 0));
    }

    [Test]
    public async Task Uses_Are_Sent_Oldest_First_In_Batches_With_When_In_What_Order_And_How_Each_Ended(CancellationToken ct)
    {
        var (usage, store, server, clock, log) = Make(batch: 2);
        string[] features = ["POST scan/start", "GET scan/groups", "POST photos/bulk/remove-duplicates", "GET trips"];
        foreach (var feature in features)
        {
            await usage.RecordAsync(feature, feature == "GET trips" ? 402 : 200, ct);
            clock.Now = clock.Now.AddSeconds(30);
        }

        await Assert.That(await usage.SendDueAsync(ct)).IsEqualTo(4);
        await Assert.That(server.Sent.Select(s => s.Report.Uses.Count)).IsEquivalentTo([2, 2], CollectionOrdering.Matching);
        var uses = server.Sent.SelectMany(s => s.Report.Uses).ToList();
        await Assert.That(uses.Select(u => (u.Sequence, u.Feature, u.Status, u.At))).IsEquivalentTo([
            (1L, "POST scan/start", 200, TestClock.Day), (2L, "GET scan/groups", 200, TestClock.Day.AddSeconds(30)),
            (3L, "POST photos/bulk/remove-duplicates", 200, TestClock.Day.AddSeconds(60)), (4L, "GET trips", 402, TestClock.Day.AddSeconds(90)),
        ], CollectionOrdering.Matching);
        // One run of the program, one installation, one program, and the server named in the settings.
        await Assert.That((uses.Select(u => u.Session).Distinct().Count(), server.Sent.Select(s => s.Report.Install).Distinct().Single(), server.Sent[0].Report.Source, server.Sent[0].To.AbsoluteUri))
            .IsEqualTo((1, await store.InstallAsync(ct), Source, Server));
        await Assert.That(await store.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(log.Lines.Single()).IsEqualTo(new LoggedLine(LogLevel.Debug, 1600, "Sent 4 uses to the usage server", null));
    }

    [Test]
    public async Task Uses_Are_Sent_At_Most_Once_In_Each_Period(CancellationToken ct)
    {
        var (usage, _, server, clock, _) = Make(batch: 2);
        await usage.RecordAsync("POST scan/start", 200, ct);
        await Assert.That(await usage.SendDueAsync(ct)).IsEqualTo(1);

        await usage.RecordAsync("GET trips", 200, ct);
        clock.Now = clock.Now.AddMinutes(59);
        await Assert.That(await usage.SendDueAsync(ct)).IsEqualTo(0);
        clock.Now = clock.Now.AddMinutes(1);
        await Assert.That((await usage.SendDueAsync(ct), server.Sent.Count)).IsEqualTo((1, 2));
    }

    [Test]
    public async Task A_Server_That_Cannot_Be_Reached_Loses_Nothing_And_Is_Said_In_The_Log(CancellationToken ct)
    {
        var (usage, store, server, clock, log) = Make();
        server.Down = true;
        await usage.RecordAsync("POST scan/start", 200, ct);
        await usage.RecordAsync("GET trips", 200, ct);

        await Assert.That((await usage.SendDueAsync(ct), await store.CountAsync(ct))).IsEqualTo((0, 2));
        var said = log.Lines.Single();
        await Assert.That((said.Level, said.EventId, said.Message, said.Error?.Message)).IsEqualTo((LogLevel.Warning, 1601,
            "The usage server could not be reached; 2 uses wait for the next time", "The usage server could not be reached: No route to host."));

        server.Down = false;
        clock.Now = clock.Now.AddHours(1);
        await Assert.That((await usage.SendDueAsync(ct), await store.CountAsync(ct))).IsEqualTo((2, 0));
    }

    [Test]
    public async Task Past_The_Most_Kept_The_Oldest_Uses_Are_Forgotten_And_At_Least_One_Is_Kept(CancellationToken ct)
    {
        var (usage, store, _, _, _) = Make(maxKept: 0);
        await usage.RecordAsync("POST scan/start", 200, ct);
        await usage.RecordAsync("GET trips", 200, ct);

        await Assert.That((await store.OldestAsync(10, ct)).Select(u => u.Feature)).IsEquivalentTo(["GET trips"]);
    }

    [Test]
    public async Task One_Sending_At_A_Time_So_That_Nothing_Is_Sent_Twice(CancellationToken ct)
    {
        var (usage, _, server, _, _) = Make();
        server.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await usage.RecordAsync("POST scan/start", 200, ct);

        var first = usage.SendDueAsync(ct);
        await server.Reached.Task.WaitAsync(ct);
        await Assert.That(await usage.SendDueAsync(ct)).IsEqualTo(0);
        server.Hold.SetResult();
        await Assert.That((await first, server.Sent.Count)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task The_Computer_Sends_From_Its_Start_Until_It_Stops(CancellationToken ct)
    {
        var (usage, _, server, _, _) = Make();
        await usage.RecordAsync("POST scan/start", 200, ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var running = usage.SendWhileRunningAsync(stop.Token);
        await server.Taken.Task.WaitAsync(ct);
        stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => running);
        await Assert.That(server.Sent.Single().Report.Uses.Single().Feature).IsEqualTo("POST scan/start");
    }

    [Test]
    public async Task A_Program_Stopped_Before_Sending_Begins_Sends_Nothing(CancellationToken ct)
    {
        var (usage, store, server, _, _) = Make();
        await usage.RecordAsync("POST scan/start", 200, ct);

        await usage.SendWhileRunningAsync(new CancellationToken(canceled: true));
        await Assert.That((server.Sent.Count, await store.CountAsync(ct))).IsEqualTo((0, 1));
    }
}
