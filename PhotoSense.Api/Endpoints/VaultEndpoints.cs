using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Application.Vault;
using PhotoSense.Contracts.Organize;

namespace PhotoSense.Api.Endpoints;

/// <summary>The hidden vault: only a phone has one.</summary>
internal sealed class VaultEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HiddenVault? _vault;

    /// <summary>The hidden vault, where this program has one.</summary>
    /// <param name="vault">The hidden vault; null on a computer.</param>
    public VaultEndpoints(HiddenVault? vault) => _vault = vault;

    /// <summary>GET /api/vault: how the vault stands, and, once unlocked, what it holds.</summary>
    public async Task<ApiResponse> StateAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        var state = await _vault!.StateAsync(ct);
        return await Answers.JsonAsync(req, new VaultDto
        {
            Stage = Camel(state.Stage), Count = state.Count, NotBackedUp = state.NotBackedUp, Computer = state.Computer,
            Items = [.. state.Items.Select(i => new VaultItemDto { Id = i.Id, Name = i.Name, Taken = i.Taken, IsVideo = i.IsVideo, Bytes = i.Bytes, OnComputer = i.OnComputer })],
            Restore = state.Restore is { } run ? new VaultRestoreDto { Stage = Camel(run.Stage), Problem = run.Problem, Restored = run.Restored } : null,
        }, ct);
    }

    /// <summary>POST /api/vault/on: turns the vault on (Pro), and gives its recovery key, to write down.</summary>
    public async Task<ApiResponse> TurnOnAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        try { return await Answers.JsonAsync(req, new RecoveryKeyDto { RecoveryKey = await _vault!.TurnOnAsync(ct) }, ct); }
        catch (InvalidOperationException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>POST /api/vault/recover: takes a recovery key, and brings the vault's items back from the computer; it goes on by itself.</summary>
    public async Task<ApiResponse> RecoverAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        var asked = await req.ReadJsonAsync<RecoveryKeyDto>(Json, ct);
        try
        {
            await _vault!.RecoverAsync(asked?.RecoveryKey ?? string.Empty, ct);
            return req.CreateResponse(HttpStatusCode.Accepted);
        }
        catch (ArgumentException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, ex.Message, ct); }
        catch (InvalidOperationException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>POST /api/vault/unlock: asks the phone's own question, and unlocks the vault when the person passes it.</summary>
    public async Task<ApiResponse> UnlockAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        try { return await Answers.JsonAsync(req, new VaultUnlockDto { Unlocked = await _vault!.UnlockAsync(ct) }, ct); }
        catch (InvalidOperationException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>POST /api/vault/lock: locks the vault.</summary>
    public async Task<ApiResponse> LockAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        _vault!.Lock();
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    /// <summary>GET /api/vault/key: the recovery key, to write down again, while the vault is unlocked.</summary>
    public async Task<ApiResponse> KeyAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        try { return await Answers.JsonAsync(req, new RecoveryKeyDto { RecoveryKey = _vault!.RecoveryKeyShown() }, ct); }
        catch (InvalidOperationException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>POST /api/vault/hide: seals Library's pictures and videos into the vault, then the phone takes them out of the library after its question.</summary>
    public Task<ApiResponse> HideAsync(ApiRequest req, CancellationToken ct) => ChangeAsync(req, (vault, ids) => vault.HideAsync(ids, ct), ct);

    /// <summary>POST /api/vault/unhide: puts items back in the library, each as a new item with its date and place.</summary>
    public Task<ApiResponse> UnhideAsync(ApiRequest req, CancellationToken ct) => ChangeAsync(req, (vault, ids) => vault.UnhideAsync(ids, ct), ct);

    /// <summary>POST /api/vault/delete: puts items back, then sends them to the phone's Recently Deleted or bin after its question.</summary>
    public Task<ApiResponse> DeleteAsync(ApiRequest req, CancellationToken ct) => ChangeAsync(req, (vault, ids) => vault.DeleteAsync(ids, ct), ct);

    /// <summary>GET /api/vault/{id}/thumbnail: an item's small preview, while the vault is unlocked.</summary>
    public Task<ApiResponse> ThumbnailAsync(ApiRequest req, string id, CancellationToken ct) => PreviewAsync(req, id, VaultBox.ThumbnailPart, ct);

    /// <summary>GET /api/vault/{id}/image: the picture as the viewer shows it, while the vault is unlocked.</summary>
    public Task<ApiResponse> ImageAsync(ApiRequest req, string id, CancellationToken ct) => PreviewAsync(req, id, VaultBox.ViewPart, ct);

    private async Task<ApiResponse> PreviewAsync(ApiRequest req, string id, string kind, CancellationToken ct)
    {
        if (_vault is null || await _vault.PreviewAsync(id, kind, ct) is not { } jpeg) return req.CreateResponse(HttpStatusCode.NotFound);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "image/jpeg");
        // Never kept by the page once the vault locks.
        resp.Headers.Add("Cache-Control", "no-store");
        await resp.Body.WriteAsync(jpeg, ct);
        return resp;
    }

    private async Task<ApiResponse> ChangeAsync(ApiRequest req, Func<HiddenVault, IReadOnlyList<string>, Task<int>> change, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<VaultIdsRequest>(Json, ct) is not { Ids.Count: > 0 } asked)
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Choose the photos and videos first.", ct);
        try { return await Answers.JsonAsync(req, new FixResultDto { Changed = await change(_vault!, asked.Ids) }, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    private static string Camel<T>(T value) where T : Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private Task<ApiResponse>? Refused(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return _vault is null ? Answers.RefuseAsync(req, HttpStatusCode.NotFound, "Only a phone has a hidden vault.", ct) : null;
    }
}
