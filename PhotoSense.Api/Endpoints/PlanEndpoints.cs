using System.Globalization;
using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Application.Plans;
using PhotoSense.Application.Transfer;
using PhotoSense.Contracts.Plans;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Services;

namespace PhotoSense.Api.Endpoints;

/// <summary>
/// The plan the person has, the removals left today, and buying, restoring and managing a plan through the phone's store;
/// and whether the person has accepted the terms of use.
/// </summary>
internal sealed class PlanEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly Subscription _subscription;
    private readonly DailyRemovals _removals;
    private readonly TermsAcceptance _terms;
    private readonly PhoneBackup? _phone;

    /// <summary>The plan <paramref name="subscription"/> holds, with the removals <paramref name="removals"/> counts.</summary>
    /// <param name="subscription">The plan.</param>
    /// <param name="removals">Today's removals.</param>
    /// <param name="terms">Whether the person accepted the note asking for a backup.</param>
    /// <param name="phone">On a phone, tells the computer it was paired with when the person accepts; null on a computer.</param>
    public PlanEndpoints(Subscription subscription, DailyRemovals removals, TermsAcceptance terms, PhoneBackup? phone)
    {
        _subscription = subscription;
        _removals = removals;
        _terms = terms;
        _phone = phone;
    }

    /// <summary>GET /api/terms: the day the person accepted the note asking for a backup, here or on a device paired with this one.</summary>
    public async Task<ApiResponse> TermsAsync(ApiRequest req, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new TermsDto { Accepted = TermsAcceptance.Write(await _terms.AcceptedAsync(ct)) }, ct);
        return resp;
    }

    /// <summary>POST /api/terms/accept: the person accepts; a phone tells the computer it was paired with, so that it does not ask there.</summary>
    public async Task<ApiResponse> AcceptAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var day = await _terms.AcceptAsync(ct);
        _phone?.TellAccepted();
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new TermsDto { Accepted = TermsAcceptance.Write(day) }, ct);
        return resp;
    }

    /// <summary>GET /api/plan: the plan, today's removals, and the plans for sale.</summary>
    public Task<ApiResponse> GetAsync(ApiRequest req, CancellationToken ct) => PlanAsync(req, ct);

    /// <summary>POST /api/plan/buy: puts the store's sheet for the plan named in front of the person, and answers how it ended.</summary>
    public async Task<ApiResponse> BuyAsync(ApiRequest req, CancellationToken ct)
    {
        // Only the page itself may put the store's sheet in front of someone.
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var request = await req.ReadJsonAsync<PlanBuyRequest>(Json, ct);
        if (request is null || !Enum.TryParse<PlanKind>(request.Kind, ignoreCase: true, out var kind) || !string.Equals(kind.ToString(), request.Kind, StringComparison.OrdinalIgnoreCase))
            return await RefuseAsync(req, HttpStatusCode.BadRequest, "Choose a plan first.", ct);
        StoreAnswer answer;
        try { answer = await _subscription.BuyAsync(kind, ct); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        catch (StoreException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new PlanBuyResultDto { Outcome = answer.ToString().ToLowerInvariant(), Plan = await DtoAsync(ct) }, ct);
        return resp;
    }

    /// <summary>POST /api/plan/restore: asks the store again for what was bought with the same account, and answers with the plan then.</summary>
    public async Task<ApiResponse> RestoreAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        try { await _subscription.RestoreAsync(ct); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        catch (StoreException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        return await PlanAsync(req, ct);
    }

    /// <summary>POST /api/plan/manage: opens the store's page for changing or cancelling the subscription.</summary>
    public async Task<ApiResponse> ManageAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        try { await _subscription.ManageAsync(ct); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        catch (StoreException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private async Task<ApiResponse> PlanAsync(ApiRequest req, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(await DtoAsync(ct), ct);
        return resp;
    }

    private async Task<PlanDto> DtoAsync(CancellationToken ct)
    {
        var plan = await _subscription.CurrentAsync(ct);
        var today = await _removals.TodayAsync(ct);
        var offers = await _subscription.OffersAsync(ct);
        return new PlanDto
        {
            Tier = plan.Tier.ToString().ToLowerInvariant(),
            Kind = plan.Kind == PlanKind.None ? null : plan.Kind.ToString().ToLowerInvariant(),
            Started = plan.Started?.ToString("O", CultureInfo.InvariantCulture),
            Until = plan.Until?.ToString("O", CultureInfo.InvariantCulture),
            SharedBy = plan.SharedBy,
            Store = _subscription.StoreName,
            RemovalsPerDay = today.PerDay,
            RemovedToday = today.Used,
            LeftToday = today.Left,
            Offers = [.. offers.Select(o => new PlanOfferDto { Kind = o.Kind.ToString().ToLowerInvariant(), Price = o.Product.Price, Amount = o.Product.Amount, Currency = o.Product.Currency })],
        };
    }

    private static async Task<ApiResponse> RefuseAsync(ApiRequest req, HttpStatusCode status, string why, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteStringAsync(why, ct);
        resp.StatusCode = status;
        return resp;
    }
}
