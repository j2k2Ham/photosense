using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Application.Transfer;
using PhotoSense.Contracts.Transfer;

namespace PhotoSense.Api.Endpoints;

/// <summary>
/// Sending photos from a phone to a computer, as each sees it: the phone offers files and the computer receives
/// them. Each program answers its own half; asked for the other, it says it is not the one for that.
/// </summary>
internal sealed class TransferEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly PhoneSending? _sending;
    private readonly PhoneReceiving? _receiving;

    /// <summary>The half this program has: <paramref name="sending"/> on a phone, <paramref name="receiving"/> on a computer.</summary>
    public TransferEndpoints(PhoneSending? sending = null, PhoneReceiving? receiving = null)
    {
        _sending = sending;
        _receiving = receiving;
    }

    // ---- the phone's half

    /// <summary>POST /api/send/start: offers the files chosen, until stopped.</summary>
    public async Task<ApiResponse> StartSendingAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _sending, "Only a phone sends photos.", ct) is { } refused) return await refused;
        var request = await req.ReadJsonAsync<SendStartRequest>(Json, ct);
        if (request is null || request.Files.Count == 0) return await RefuseAsync(req, HttpStatusCode.BadRequest, "Choose the photos to send first.", ct);
        try { return await SendStateAsync(req, await _sending!.StartAsync(request.Files, ct), ct); }
        catch (ArgumentException ex) { return await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message, ct); }
    }

    /// <summary>GET /api/send/state: how sending stands.</summary>
    public async Task<ApiResponse> SendingAsync(ApiRequest req, CancellationToken ct)
        => Refused(req, _sending, "Only a phone sends photos.", ct, guarded: false) is { } refused ? await refused : await SendStateAsync(req, _sending!.State, ct);

    /// <summary>POST /api/send/confirm: the person's answer to whether the numbers on the two match.</summary>
    public async Task<ApiResponse> ConfirmSendingAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _sending, "Only a phone sends photos.", ct) is { } refused) return await refused;
        _sending!.Confirm((await req.ReadJsonAsync<TransferConfirmRequest>(Json, ct))?.Same == true);
        return await SendStateAsync(req, _sending.State, ct);
    }

    /// <summary>POST /api/send/stop: stops offering.</summary>
    public async Task<ApiResponse> StopSendingAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _sending, "Only a phone sends photos.", ct) is { } refused) return await refused;
        _sending!.Stop();
        return await SendStateAsync(req, _sending.State, ct);
    }

    /// <summary>POST /api/send/delete-sent: the photos safely on the computer go to the phone's Recently Deleted, after the phone asks.</summary>
    public async Task<ApiResponse> DeleteSentAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _sending, "Only a phone sends photos.", ct) is { } refused) return await refused;
        try
        {
            var resp = req.CreateResponse();
            await resp.WriteAsJsonAsync(new DeleteSentDto { Deleted = _sending!.DeleteSent() }, ct);
            return resp;
        }
        catch (IOException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    // ---- the computer's half

    /// <summary>GET /api/phones: the phones offering photos now, found over the network and the cable.</summary>
    public async Task<ApiResponse> PhonesAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _receiving, "Only a computer receives photos.", ct) is { } refused) return await refused;
        var found = await _receiving!.FindAsync(ct);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new FoundPhonesDto { Phones = [.. found.Select(p => new FoundPhoneDto { Key = p.Key, Name = p.Name, Via = p.Via })] }, ct);
        return resp;
    }

    /// <summary>POST /api/receive/start: starts receiving from a phone found, into a folder.</summary>
    public async Task<ApiResponse> StartReceivingAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _receiving, "Only a computer receives photos.", ct) is { } refused) return await refused;
        var request = await req.ReadJsonAsync<ReceiveStartRequest>(Json, ct);
        if (string.IsNullOrWhiteSpace(request?.Phone) || string.IsNullOrWhiteSpace(request.Folder) || !Path.IsPathFullyQualified(request.Folder))
            return await RefuseAsync(req, HttpStatusCode.BadRequest, "Choose the phone, and the folder the photos go to.", ct);
        try { _receiving!.Start(request.Phone, request.Folder); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        catch (ArgumentException ex) { return await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message, ct); }
        catch (DirectoryNotFoundException ex) { return await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message, ct); }
        return await ReceiveStateAsync(req, _receiving.State, ct);
    }

    /// <summary>GET /api/receive/state: how receiving stands.</summary>
    public async Task<ApiResponse> ReceivingAsync(ApiRequest req, CancellationToken ct)
        => Refused(req, _receiving, "Only a computer receives photos.", ct, guarded: false) is { } refused ? await refused : await ReceiveStateAsync(req, _receiving!.State, ct);

    /// <summary>POST /api/receive/confirm: the person's answer to whether the numbers on the two match.</summary>
    public async Task<ApiResponse> ConfirmReceivingAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _receiving, "Only a computer receives photos.", ct) is { } refused) return await refused;
        _receiving!.Confirm((await req.ReadJsonAsync<TransferConfirmRequest>(Json, ct))?.Same == true);
        return await ReceiveStateAsync(req, _receiving.State, ct);
    }

    /// <summary>POST /api/receive/stop: stops receiving; what has arrived stays.</summary>
    public async Task<ApiResponse> StopReceivingAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _receiving, "Only a computer receives photos.", ct) is { } refused) return await refused;
        _receiving!.Stop();
        return await ReceiveStateAsync(req, _receiving.State, ct);
    }

    // Asked of the program that is not the one for it, or by a page other than PhotoSense's own about someone's photos.
    private static Task<ApiResponse>? Refused(ApiRequest req, object? service, string why, CancellationToken ct, bool guarded = true)
    {
        if (guarded && !RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return service is null ? RefuseAsync(req, HttpStatusCode.NotFound, why, ct) : null;
    }

    private async Task<ApiResponse> SendStateAsync(ApiRequest req, SendState state, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new SendStateDto
        {
            Stage = state.Stage.ToString().ToLowerInvariant(), Code = state.Code, Computer = state.Computer, Done = state.Done, Total = state.Total, Bytes = state.Bytes,
            TotalBytes = state.TotalBytes, Deletable = [.. state.Deletable], Where = state.Where, Phone = _sending!.Name, Problem = state.Problem, Current = state.Current,
        }, ct);
        return resp;
    }

    private static async Task<ApiResponse> ReceiveStateAsync(ApiRequest req, ReceiveState state, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new ReceiveStateDto
        {
            Stage = state.Stage.ToString().ToLowerInvariant(), Code = state.Code, Phone = state.Phone, Done = state.Done, Total = state.Total, Bytes = state.Bytes,
            TotalBytes = state.TotalBytes, AlreadyThere = state.AlreadyThere, Folder = state.Folder, Problem = state.Problem,
        }, ct);
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
