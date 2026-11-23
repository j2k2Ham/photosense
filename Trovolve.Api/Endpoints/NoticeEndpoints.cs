using System.Net;
using System.Text.Json;
using Trovolve.Api.Http;
using Trovolve.Application.Plans;
using Trovolve.Contracts.Cleaning;

namespace Trovolve.Api.Endpoints;

/// <summary>A phone's notifications: which are on, turning them on or off, and what one tapped asked to open.</summary>
internal sealed class NoticeEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const string OnlyAPhone = "Only a phone sends notifications.";
    private readonly PhoneNotices? _notices;

    /// <summary>The notifications, where this program has them.</summary>
    /// <param name="notices">The phone's notifications; null on a computer.</param>
    public NoticeEndpoints(PhoneNotices? notices) => _notices = notices;

    /// <summary>GET /api/notices: which notifications are on, and whether the phone lets Trovolve send any.</summary>
    public async Task<ApiResponse> StateAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        return await Answers.JsonAsync(req, Dto(await _notices!.StateAsync(ct)), ct);
    }

    /// <summary>
    /// POST /api/notices: turns low-storage alerts or the trial's reminder on or off. Turning one on asks the phone's own
    /// question when Trovolve may not send notifications yet; refused, it stays off, and the answer says so.
    /// </summary>
    public async Task<ApiResponse> ChangeAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<NoticeChangeRequest>(Json, ct) is not { } asked || (asked.LowStorage is null && asked.TrialReminder is null))
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Say which notifications to turn on or off.", ct);
        return await Answers.JsonAsync(req, Dto(await _notices!.ChangeAsync(asked.LowStorage, asked.TrialReminder, ct)), ct);
    }

    /// <summary>GET /api/notices/opened: what the last notification tapped asked to open, once.</summary>
    public async Task<ApiResponse> OpenedAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        ct.ThrowIfCancellationRequested();
        return await Answers.JsonAsync(req, new NoticeOpenedDto { Opens = _notices!.TakeOpened() }, ct);
    }

    private static NoticeStateDto Dto(NoticeState s) => new() { LowStorage = s.LowStorage, TrialReminder = s.TrialReminder, Allowed = s.Allowed };

    private Task<ApiResponse>? Refused(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return _notices is null ? Answers.RefuseAsync(req, HttpStatusCode.NotFound, OnlyAPhone, ct) : null;
    }
}
