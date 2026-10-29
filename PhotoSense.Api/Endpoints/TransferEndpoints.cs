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
public sealed class TransferEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly PhoneSending? _sending;
    private readonly PhoneReceiving? _receiving;

    public TransferEndpoints(PhoneSending? sending = null, PhoneReceiving? receiving = null)
    {
        _sending = sending;
        _receiving = receiving;
    }

    // ---- the phone's half

    // POST /api/send/start
    public async Task<ApiResponse> StartSendingAsync(ApiRequest req)
    {
        if (Refused(req, _sending, "Only a phone sends photos.") is { } refused) return await refused;
        var request = await ReadAsync<SendStartRequest>(req);
        if (request is null || request.Files.Count == 0) return await RefuseAsync(req, HttpStatusCode.BadRequest, "Choose the photos to send first.");
        try { return await SendStateAsync(req, await _sending!.StartAsync(request.Files, req.Aborted)); }
        catch (ArgumentException ex) { return await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message); }
    }

    // GET /api/send/state
    public async Task<ApiResponse> SendingAsync(ApiRequest req)
        => Refused(req, _sending, "Only a phone sends photos.", guarded: false) is { } refused ? await refused : await SendStateAsync(req, _sending!.State);

    // POST /api/send/confirm
    public async Task<ApiResponse> ConfirmSendingAsync(ApiRequest req)
    {
        if (Refused(req, _sending, "Only a phone sends photos.") is { } refused) return await refused;
        _sending!.Confirm((await ReadAsync<TransferConfirmRequest>(req))?.Same == true);
        return await SendStateAsync(req, _sending.State);
    }

    // POST /api/send/stop
    public async Task<ApiResponse> StopSendingAsync(ApiRequest req)
    {
        if (Refused(req, _sending, "Only a phone sends photos.") is { } refused) return await refused;
        _sending!.Stop();
        return await SendStateAsync(req, _sending.State);
    }

    // POST /api/send/delete-sent: the photos safely on the computer go to the phone's Recently Deleted, after the phone asks.
    public async Task<ApiResponse> DeleteSentAsync(ApiRequest req)
    {
        if (Refused(req, _sending, "Only a phone sends photos.") is { } refused) return await refused;
        try
        {
            var resp = req.CreateResponse();
            await resp.WriteAsJsonAsync(new DeleteSentDto { Deleted = _sending!.DeleteSent() });
            return resp;
        }
        catch (IOException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message); }
    }

    // ---- the computer's half

    // GET /api/phones: the phones offering photos now, found over the network and the cable.
    public async Task<ApiResponse> PhonesAsync(ApiRequest req)
    {
        if (Refused(req, _receiving, "Only a computer receives photos.") is { } refused) return await refused;
        var found = await _receiving!.FindAsync(req.Aborted);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new FoundPhonesDto { Phones = [.. found.Select(p => new FoundPhoneDto { Key = p.Key, Name = p.Name, Via = p.Via })] });
        return resp;
    }

    // POST /api/receive/start
    public async Task<ApiResponse> StartReceivingAsync(ApiRequest req)
    {
        if (Refused(req, _receiving, "Only a computer receives photos.") is { } refused) return await refused;
        var request = await ReadAsync<ReceiveStartRequest>(req);
        if (string.IsNullOrWhiteSpace(request?.Phone) || string.IsNullOrWhiteSpace(request.Folder) || !Path.IsPathFullyQualified(request.Folder))
            return await RefuseAsync(req, HttpStatusCode.BadRequest, "Choose the phone, and the folder the photos go to.");
        try { _receiving!.Start(request.Phone, request.Folder); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message); }
        catch (ArgumentException ex) { return await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message); }
        catch (DirectoryNotFoundException ex) { return await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message); }
        return await ReceiveStateAsync(req, _receiving.State);
    }

    // GET /api/receive/state
    public async Task<ApiResponse> ReceivingAsync(ApiRequest req)
        => Refused(req, _receiving, "Only a computer receives photos.", guarded: false) is { } refused ? await refused : await ReceiveStateAsync(req, _receiving!.State);

    // POST /api/receive/confirm
    public async Task<ApiResponse> ConfirmReceivingAsync(ApiRequest req)
    {
        if (Refused(req, _receiving, "Only a computer receives photos.") is { } refused) return await refused;
        _receiving!.Confirm((await ReadAsync<TransferConfirmRequest>(req))?.Same == true);
        return await ReceiveStateAsync(req, _receiving.State);
    }

    // POST /api/receive/stop
    public async Task<ApiResponse> StopReceivingAsync(ApiRequest req)
    {
        if (Refused(req, _receiving, "Only a computer receives photos.") is { } refused) return await refused;
        _receiving!.Stop();
        return await ReceiveStateAsync(req, _receiving.State);
    }

    // Asked of the program that is not the one for it, or by a page other than PhotoSense's own about someone's photos.
    private static Task<ApiResponse>? Refused(ApiRequest req, object? service, string why, bool guarded = true)
    {
        if (guarded && !RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return service is null ? RefuseAsync(req, HttpStatusCode.NotFound, why) : null;
    }

    private static async Task<T?> ReadAsync<T>(ApiRequest req) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(await new StreamReader(req.Body).ReadToEndAsync(), Json); }
        catch (JsonException) { return null; }
    }

    private async Task<ApiResponse> SendStateAsync(ApiRequest req, SendState state)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new SendStateDto
        {
            Stage = state.Stage.ToString().ToLowerInvariant(), Code = state.Code, Computer = state.Computer, Done = state.Done, Total = state.Total, Bytes = state.Bytes,
            TotalBytes = state.TotalBytes, Deletable = [.. state.Deletable], Where = state.Where, Phone = _sending!.Name, Problem = state.Problem,
        });
        return resp;
    }

    private static async Task<ApiResponse> ReceiveStateAsync(ApiRequest req, ReceiveState state)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new ReceiveStateDto
        {
            Stage = state.Stage.ToString().ToLowerInvariant(), Code = state.Code, Phone = state.Phone, Done = state.Done, Total = state.Total, Bytes = state.Bytes,
            TotalBytes = state.TotalBytes, AlreadyThere = state.AlreadyThere, Folder = state.Folder, Problem = state.Problem,
        });
        return resp;
    }

    private static async Task<ApiResponse> RefuseAsync(ApiRequest req, HttpStatusCode status, string why)
    {
        var resp = req.CreateResponse();
        await resp.WriteStringAsync(why);
        resp.StatusCode = status;
        return resp;
    }
}
