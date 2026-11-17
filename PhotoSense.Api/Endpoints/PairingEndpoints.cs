using System.Globalization;
using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Application.Transfer;
using PhotoSense.Contracts.Transfer;

namespace PhotoSense.Api.Endpoints;

/// <summary>
/// Pairing a phone with a computer, as each sees it: the computer shows a QR code and a code to type, and the phone
/// scans the one or types the other. Each program answers its own half; asked for the other, it says it is not the one.
/// </summary>
internal sealed class PairingEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const string OnlyAComputer = "Only a computer shows a code to pair with.";
    private const string OnlyAPhone = "Only a phone scans or types a computer's code.";
    private readonly ComputerPairing? _computer;
    private readonly PhonePairing? _phone;

    /// <summary>The computer's half or the phone's, whichever this program has.</summary>
    public PairingEndpoints(ComputerPairing? computer, PhonePairing? phone)
    {
        _computer = computer;
        _phone = phone;
    }

    /// <summary>POST /api/pairing/open: on a computer, starts waiting for a phone, with a new QR code and code.</summary>
    public async Task<ApiResponse> OpenAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _computer, OnlyAComputer, ct) is { } refused) return await refused;
        return await StateAsync(req, Dto(await _computer!.OpenAsync(ct)), ct);
    }

    /// <summary>POST /api/pairing/close: on a computer, stops waiting for a phone.</summary>
    public async Task<ApiResponse> CloseAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _computer, OnlyAComputer, ct) is { } refused) return await refused;
        _computer!.Close();
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    /// <summary>POST /api/pairing/scan: on a phone, opens the camera, and pairs with the computer whose QR code it sees.</summary>
    public async Task<ApiResponse> ScanAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        return await StateAsync(req, Dto(await _phone!.ScanAsync(ct)), ct);
    }

    /// <summary>POST /api/pairing/code: on a phone, finds the computer showing the code typed, and pairs once the numbers are compared.</summary>
    public async Task<ApiResponse> CodeAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        var request = await req.ReadJsonAsync<PairingCodeRequest>(Json, ct);
        try { return await StateAsync(req, Dto(_phone!.EnterCode(request?.Code ?? string.Empty)), ct); }
        catch (ArgumentException ex) { return await RefuseAsync(req, HttpStatusCode.BadRequest, ex.Message, ct); }
    }

    /// <summary>GET /api/pairing/state: where pairing has got to, on whichever this program is.</summary>
    public async Task<ApiResponse> StateAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        return await StateAsync(req, Current(), ct);
    }

    /// <summary>POST /api/pairing/confirm: the person's answer to whether the numbers on the two match.</summary>
    public async Task<ApiResponse> ConfirmAsync(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return req.CreateResponse(HttpStatusCode.Unauthorized);
        var same = (await req.ReadJsonAsync<TransferConfirmRequest>(Json, ct))?.Same == true;
        _computer?.Confirm(same);
        _phone?.Confirm(same);
        return await StateAsync(req, Current(), ct);
    }

    /// <summary>POST /api/pairing/stop: on a phone, stops pairing.</summary>
    public async Task<ApiResponse> StopAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        _phone!.Stop();
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // A program with neither half, such as one made for a test, is not pairing.
    private PairingStateDto Current() => _computer is not null ? Dto(_computer.State) : _phone is not null ? Dto(_phone.State) : new PairingStateDto();

    private static PairingStateDto Dto(ComputerPairingState s) => new()
    {
        Stage = s.Stage.ToString().ToLowerInvariant(), Link = s.Link, Code = s.Code, Expires = s.Expires?.ToString("O", CultureInfo.InvariantCulture),
        Check = s.Check, Other = s.Phone, Problem = s.Problem,
    };

    private static PairingStateDto Dto(PhonePairingState s) => new()
    {
        Stage = s.Stage.ToString().ToLowerInvariant(), Check = s.Check, Other = s.Computer, Folder = s.Folder, Problem = s.Problem,
    };

    private static Task<ApiResponse>? Refused(ApiRequest req, object? service, string why, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return service is null ? RefuseAsync(req, HttpStatusCode.NotFound, why, ct) : null;
    }

    private static async Task<ApiResponse> StateAsync(ApiRequest req, PairingStateDto state, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(state, ct);
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
