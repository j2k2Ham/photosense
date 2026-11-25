using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trovolve.Api;
using Trovolve.Api.Hosting;
using Trovolve.Api.Http;
using Trovolve.Domain.Configuration;
using Trovolve.Domain.Repositories;

namespace Trovolve.Tests.Endpoints;

/// <summary>Every route answered is written to the log, and noted as a use of a feature when usage metrics are on.</summary>
public class UsageAndLoggingTests
{
    // The service's addresses with usage metrics on or off, and the log the router writes to.
    private static (TestApi Api, ListLogger<ApiRouter> Log) Api(bool usage)
    {
        var log = new ListLogger<ApiRouter>();
        var api = new TestApi(s => s.AddSingleton(Options.Create(new UsageMetricsOptions { Enabled = usage })).AddSingleton<ILogger<ApiRouter>>(log));
        return (api, log);
    }

    [Test]
    public async Task Each_Use_Of_A_Feature_Is_Noted_By_Its_Route_Without_The_Values_In_Its_Address_And_With_How_It_Ended(CancellationToken ct)
    {
        var (api, _) = Api(usage: true);
        var about = new AboutApi(api);
        await about.HealthAsync(Http.Get("/api/health"), ct);
        await about.GetAsync(Http.Get("/api/about"), ct);
        await about.TextAsync(Http.Get("/api/about"), "privacy", ct);
        // Refused, as a web page other than Trovolve's own asked: noted all the same, with how it ended.
        await about.BrowseAsync(Http.Get("/api/folders"), ct);

        var uses = await api.Services.GetRequiredService<IUsageStore>().OldestAsync(10, ct);
        await Assert.That(uses.Select(u => (u.Sequence, u.Feature, u.Status))).IsEquivalentTo(
            [(1L, "GET about", 200), (2L, "GET about/{document}", 200), (3L, "GET folders", 401)], CollectionOrdering.Matching);
        await Assert.That(uses.Select(u => u.At).Distinct().Single()).IsEqualTo(TestClock.Day);
    }

    [Test]
    public async Task While_Usage_Metrics_Are_Off_No_Use_Is_Noted(CancellationToken ct)
    {
        var (api, _) = Api(usage: false);
        await new AboutApi(api).GetAsync(Http.Get("/api/about"), ct);

        await Assert.That(await api.Services.GetRequiredService<IUsageStore>().CountAsync(ct)).IsEqualTo(0);
    }

    [Test]
    public async Task Every_Route_Left_Out_Of_The_Uses_Is_One_The_Service_Answers()
    {
        var (api, _) = Api(usage: true);
        var routes = api.Services.GetRequiredService<TrovolveRoutes>().Router.Routes;

        await Assert.That(TrovolveRoutes.NotUses.Except(routes)).IsEmpty();
        await Assert.That(routes).Contains("POST scan/start").And.Contains("GET photos/{id:guid}/thumbnail");
    }

    [Test]
    public async Task Each_Answer_Is_Written_To_The_Log_At_Debug_With_Its_Route_And_Status(CancellationToken ct)
    {
        var (api, log) = Api(usage: false);
        await new AboutApi(api).HealthAsync(Http.Get("/api/health"), ct);

        var line = log.Lines.Single();
        await Assert.That((line.Level, line.EventId)).IsEqualTo((LogLevel.Debug, 2000));
        await Assert.That(line.Message).StartsWith("GET health answered 200 in ").And.EndsWith(" ms");
    }

    [Test]
    public async Task On_A_Phone_A_Failure_Nothing_Else_Would_Catch_Is_Written_To_The_Log_With_Its_Error(CancellationToken ct)
    {
        var log = new ListLogger<ApiRouter>();
        var router = new ApiRouter(log, new TestClock(), used: null)
            .Get("broken", (_, _, _) => throw new InvalidOperationException("The database could not be opened."));

        var answer = await router.AnswerAsync("GET", new Uri("app://trovolve/api/broken"), [], null, ct);

        await Assert.That((answer.Status, Encoding.UTF8.GetString(answer.Body))).IsEqualTo(((int)HttpStatusCode.InternalServerError, "The database could not be opened."));
        var line = log.Lines.Single();
        await Assert.That((line.Level, line.EventId, line.Message, line.Error?.Message))
            .IsEqualTo((LogLevel.Error, 2001, "GET /api/broken failed on an error nothing expected", "The database could not be opened."));
    }
}
