using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using PhotoSense.Core.Domain.Services;

namespace PhotoSense.Functions.Api;

public class OutboxDispatchFunctions
{
    private readonly IOutboxStore _store;
    public OutboxDispatchFunctions(IOutboxStore store) => _store = store;

    [Function("OutboxDispatchManual")]
    public async Task<HttpResponseData> RunManualAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "outbox/dispatch")] HttpRequestData req)
    {
        await LogPendingAsync();
        var resp = req.CreateResponse(HttpStatusCode.Accepted);
        await resp.WriteStringAsync("Logged pending outbox messages (simulation)." );
        return resp;
    }

    private async Task LogPendingAsync()
    {
        var pending = await _store.GetUnprocessedAsync(100);
        foreach (var msg in pending)
        {
            // Simulate dispatch by marking processed
            await _store.MarkProcessedAsync(msg.Id);
        }
    }
}