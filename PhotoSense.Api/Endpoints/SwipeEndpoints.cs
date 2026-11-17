using System.Globalization;
using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Application.Organizing;
using PhotoSense.Contracts.Organizing;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Api.Endpoints;

/// <summary>Swipe to clean, which only a phone has: today's set, each decision, Undo, sparing on the summary, and deleting what is marked.</summary>
internal sealed class SwipeEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const string OnlyAPhone = "Only a phone has Swipe to clean.";
    private readonly SwipeToClean? _swipe;

    /// <summary>Swipe to clean, where this program has it.</summary>
    /// <param name="swipe">Swipe to clean; null on a computer.</param>
    public SwipeEndpoints(SwipeToClean? swipe) => _swipe = swipe;

    /// <summary>GET /api/swipe: how Swipe to clean stands today; the first time on a day, a new set is drawn.</summary>
    public async Task<ApiResponse> StateAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        return await AnswerAsync(req, Dto(await _swipe!.StateAsync(ct)), ct);
    }

    /// <summary>POST /api/swipe/decide: keeps a photo of today's set, or marks it to delete; nothing is deleted yet.</summary>
    public async Task<ApiResponse> DecideAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<SwipeDecideRequest>(Json, ct) is not { Id.Length: > 0 } asked)
            return await RefuseAsync(req, HttpStatusCode.BadRequest, "Say which photo, and whether it is kept or deleted.", ct);
        return await ChangeAsync(req, () => _swipe!.DecideAsync(asked.Id, asked.Delete, ct), ct);
    }

    /// <summary>POST /api/swipe/undo: takes back the last decision of today.</summary>
    public async Task<ApiResponse> UndoAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        return await ChangeAsync(req, () => _swipe!.UndoAsync(ct), ct);
    }

    /// <summary>POST /api/swipe/spare: on the summary, keeps a photo marked to delete after all, or marks it again.</summary>
    public async Task<ApiResponse> SpareAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<SwipeSpareRequest>(Json, ct) is not { Id.Length: > 0 } asked)
            return await RefuseAsync(req, HttpStatusCode.BadRequest, "Say which photo, and whether it is kept.", ct);
        return await ChangeAsync(req, () => _swipe!.SpareAsync(asked.Id, asked.Spared, ct), ct);
    }

    /// <summary>
    /// POST /api/swipe/delete: deletes what is marked, the largest first and no more than asked, to the phone's
    /// Recently Deleted or bin after its own question; the rest stay marked for another day.
    /// </summary>
    public async Task<ApiResponse> DeleteAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        var atMost = (await req.ReadJsonAsync<SwipeDeleteRequest>(Json, ct))?.AtMost;
        try
        {
            var freed = await _swipe!.DeleteAsync(atMost, ct);
            return await AnswerAsync(req, Dto(freed), ct);
        }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        // The person said no to the phone's question, or the phone could not delete them.
        catch (IOException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    private async Task<ApiResponse> ChangeAsync(ApiRequest req, Func<Task<SwipeState>> change, CancellationToken ct)
    {
        try { return await AnswerAsync(req, Dto(await change()), ct); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    private static SwipeStateDto Dto(SwipeState s) => new()
    {
        Date = Day(s.Date), Set = [.. s.Set.Select(Dto)], Earlier = [.. s.Earlier.Select(Dto)], Week = [.. s.Week.Select(d => new SwipeDayDto { Date = Day(d.Date), Swiped = d.Swiped })],
        Streak = s.Streak, Freed = s.Freed is { } f ? Dto(f) : null, Unswiped = s.Unswiped,
    };

    private static SwipeCardDto Dto(SwipeCard c) => new() { Id = c.Id, Bytes = c.Bytes, OnComputer = c.OnComputer, Delete = c.Delete, Spared = c.Spared };

    private static SwipeFreedDto Dto(SwipeFreed f) => new() { Deleted = f.Deleted, Bytes = f.Bytes, Left = f.Left };

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Task<ApiResponse>? Refused(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return _swipe is null ? RefuseAsync(req, HttpStatusCode.NotFound, OnlyAPhone, ct) : null;
    }

    private static async Task<ApiResponse> AnswerAsync<T>(ApiRequest req, T value, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(value, ct);
        return resp;
    }

    private static async Task<ApiResponse> RefuseAsync(ApiRequest req, HttpStatusCode status, string why, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteStringAsync(why, ct);
        resp.StatusCode = status;
        return resp;
    }
}
