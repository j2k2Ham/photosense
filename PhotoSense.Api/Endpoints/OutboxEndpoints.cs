using System.Net;
using PhotoSense.Domain.Services;
using PhotoSense.Api.Http;

namespace PhotoSense.Api.Endpoints;

public class OutboxEndpoints
{
    private readonly IOutboxStore _store;
    public OutboxEndpoints(IOutboxStore store) => _store = store;

    public async Task<ApiResponse> RunManualAsync(
        ApiRequest req)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
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