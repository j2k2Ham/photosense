using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using System.Net;
using PhotoSense.Infrastructure.Scanning;

namespace PhotoSense.Functions.Realtime;

public static class SignalRLogFunctions
{
    [Function("NegotiateScanLogs")]
    public static async Task<HttpResponseData> Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "negotiate")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "scanlogs")] SignalRConnectionInfo connectionInfo)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(connectionInfo);
        return resp;
    }

    // Timer flush to push any pending log lines to clients
    [Function("BroadcastScanLogs")]
    public static async Task Broadcast(
        [TimerTrigger("*/5 * * * * *")] TimerInfo timer,
        [SignalR(HubName = "scanlogs")] IAsyncCollector<SignalRMessage> signalR)
    {
        var batch = new List<(string instanceId, DateTime ts, string level, string message)>();
        while (InMemoryScanLogSink.PendingBroadcast.TryDequeue(out var item)) batch.Add(item);
        if (batch.Count == 0) return;
        foreach (var g in batch)
        {
            await signalR.AddAsync(new SignalRMessage
            {
                Target = "log",
                Arguments = new object[] { g.instanceId, g.ts.ToString("o"), g.level, g.message }
            });
        }
    }
}