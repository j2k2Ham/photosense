using System.Net;
using System.Text.Json;
using Trovolve.Api.Http;
using Trovolve.Application.Locations;
using Trovolve.Contracts.Organize;
using Trovolve.Contracts.Transfer;
using Trovolve.Domain.Services;

namespace Trovolve.Api.Endpoints;

/// <summary>The places a phone backs up to: the computer, the cloud services, and those Connect to server adds. Only a phone has them.</summary>
internal sealed class LocationEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly BackupLocations? _locations;

    /// <summary>The locations, where this program has them.</summary>
    /// <param name="locations">The locations; null on a computer.</param>
    public LocationEndpoints(BackupLocations? locations) => _locations = locations;

    /// <summary>GET /api/locations: every location, the Safe to delete rule, and what they are doing.</summary>
    public async Task<ApiResponse> ListAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        var state = await _locations!.StateAsync(ct);
        return await Answers.JsonAsync(req, new LocationsDto
        {
            Locations = [.. state.Locations.Select(Dto)], CountAt = state.CountAt, Pro = state.Pro, Held = state.Held,
            Activity = state.Activity is { } a ? new LocationActivityDto
            {
                Task = Camel(a.Task), Stage = Camel(a.Stage),
                Locations = [.. a.Locations.Select(p => new LocationProgressDto { Id = p.Id, Done = p.Done, Total = p.Total, Bytes = p.Bytes, TotalBytes = p.TotalBytes, Problem = p.Problem })],
                Sent = a.Sent is { } sent ? new SendResultDto { LocationId = sent.LocationId, Items = sent.Items, Matched = sent.Matched, Bytes = sent.Bytes } : null,
            } : null,
        }, ct);
    }

    /// <summary>GET /api/locations/{id}: a location in full, with how much it holds, asked of it now.</summary>
    public Task<ApiResponse> DetailsAsync(ApiRequest req, string id, CancellationToken ct) => DoAsync(req, async locations =>
    {
        var details = await locations.DetailsAsync(id, ct);
        return await Answers.JsonAsync(req, new LocationDetailsDto
        {
            Location = Dto(details.Location), Where = details.Where, Problem = details.Problem,
            Space = details.Space is { } space ? new LocationSpaceDto { Total = space.Total, Used = space.Used } : null,
        }, ct);
    }, ct);

    /// <summary>POST /api/locations/{id}/on: turns a location on, signing in to a cloud service first if need be; 402 on the free plan with another on.</summary>
    public Task<ApiResponse> TurnOnAsync(ApiRequest req, string id, CancellationToken ct) => NoContentAsync(req, l => l.TurnOnAsync(id, ct), ct);

    /// <summary>POST /api/locations/{id}/off: turns a location off. Nothing there is deleted.</summary>
    public Task<ApiResponse> TurnOffAsync(ApiRequest req, string id, CancellationToken ct) => NoContentAsync(req, l => l.TurnOffAsync(id, ct), ct);

    /// <summary>POST /api/locations/{id}/instead: on the free plan, turns this location on in place of the one that is on.</summary>
    public Task<ApiResponse> InsteadAsync(ApiRequest req, string id, CancellationToken ct) => NoContentAsync(req, l => l.UseInsteadAsync(id, ct), ct);

    /// <summary>POST /api/locations/{id}/disconnect: signs out of a cloud service. Nothing there is deleted.</summary>
    public Task<ApiResponse> DisconnectAsync(ApiRequest req, string id, CancellationToken ct) => NoContentAsync(req, l => l.DisconnectAsync(id, ct), ct);

    /// <summary>POST /api/locations/{id}/remove: removes a location Connect to server added. Nothing there is deleted.</summary>
    public Task<ApiResponse> RemoveAsync(ApiRequest req, string id, CancellationToken ct) => NoContentAsync(req, l => l.RemoveAsync(id, ct), ct);

    /// <summary>POST /api/locations/{id}/name: names a location Connect to server added.</summary>
    public async Task<ApiResponse> RenameAsync(ApiRequest req, string id, CancellationToken ct)
    {
        var name = (await req.ReadJsonAsync<LocationNameDto>(Json, ct))?.Name ?? string.Empty;
        return await NoContentAsync(req, l => l.RenameAsync(id, name, ct), ct);
    }

    /// <summary>POST /api/locations/count: on how many locations a copy must be checked before a photo counts as backed up.</summary>
    public async Task<ApiResponse> CountAtAsync(ApiRequest req, CancellationToken ct)
    {
        var countAt = (await req.ReadJsonAsync<CountAtDto>(Json, ct))?.CountAt ?? 0;
        return await NoContentAsync(req, l => l.SetCountAtAsync(countAt, ct), ct);
    }

    /// <summary>POST /api/locations/connect: Connect to server (Pro): checks the location, then adds or changes it.</summary>
    public async Task<ApiResponse> ConnectAsync(ApiRequest req, CancellationToken ct)
    {
        var asked = await req.ReadJsonAsync<ServerConnectionDto>(Json, ct) ?? new ServerConnectionDto();
        return await DoAsync(req, async locations =>
        {
            if (!Enum.TryParse<LocationKind>(asked.Kind, ignoreCase: true, out var kind) || !LocationKinds.IsServer(kind))
                return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Choose the kind of server.", ct);
            var id = await locations.ConnectServerAsync(new ServerConnection(asked.Id, kind, asked.Name, asked.Settings, asked.Secrets), ct);
            return await Answers.JsonAsync(req, new LocationIdDto { Id = id }, ct);
        }, ct);
    }

    /// <summary>POST /api/locations/backup: backs up to every location that is on; it goes on by itself.</summary>
    public Task<ApiResponse> BackupAsync(ApiRequest req, CancellationToken ct) => AcceptedAsync(req, l => l.BackupAsync(ct), ct);

    /// <summary>POST /api/locations/auto: backs up to the locations other than the computer when one is due.</summary>
    public Task<ApiResponse> AutomaticAsync(ApiRequest req, CancellationToken ct) => AcceptedAsync(req, l => l.AutomaticAsync(ct), ct);

    /// <summary>POST /api/locations/check: Check all backups; it goes on by itself.</summary>
    public Task<ApiResponse> CheckAsync(ApiRequest req, CancellationToken ct) => AcceptedAsync(req, l => l.CheckAllAsync(ct), ct);

    /// <summary>POST /api/locations/pause: pauses what the locations are doing.</summary>
    public Task<ApiResponse> PauseAsync(ApiRequest req, CancellationToken ct) => NoContentAsync(req, l =>
    {
        l.Pause();
        return Task.CompletedTask;
    }, ct);

    /// <summary>POST /api/locations/{id}/send: Send to: sends Library's pictures and videos chosen to one location; it goes on by itself.</summary>
    public async Task<ApiResponse> SendAsync(ApiRequest req, string id, CancellationToken ct)
    {
        var ids = (await req.ReadJsonAsync<SendToDto>(Json, ct))?.Ids ?? [];
        return await AcceptedAsync(req, l => l.SendAsync(id, ids, ct), ct);
    }

    /// <summary>POST /api/locations/sent/delete: deletes from the phone what was sent and matched, the largest first, after the phone's own question.</summary>
    public async Task<ApiResponse> DeleteSentAsync(ApiRequest req, CancellationToken ct)
    {
        var atMost = (await req.ReadJsonAsync<SafeRemoveRequest>(Json, ct))?.AtMost;
        return await DoAsync(req, async locations =>
        {
            var removed = await locations.DeleteSentAsync(atMost, ct);
            return await Answers.JsonAsync(req, new SafeRemovalDto
            {
                Removed = removed.Removed, Bytes = removed.Bytes, Left = removed.Left, StorageTotal = removed.Before.Total, StorageFreeBefore = removed.Before.Free,
            }, ct);
        }, ct);
    }

    private static LocationDto Dto(LocationView l) => new()
    {
        Id = l.Id, Kind = Camel(l.Kind), Name = l.Name, On = l.On, Connected = l.Connected, Available = l.Available, Account = l.Account, Settings = l.Settings,
        Copies = l.Copies, LastBackup = l.LastBackup, LastChecked = l.LastChecked, Problem = l.Problem,
    };

    private static string Camel<T>(T value) where T : Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private Task<ApiResponse> NoContentAsync(ApiRequest req, Func<BackupLocations, Task> run, CancellationToken ct) => DoAsync(req, async locations =>
    {
        await run(locations);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }, ct);

    private Task<ApiResponse> AcceptedAsync(ApiRequest req, Func<BackupLocations, Task> run, CancellationToken ct) => DoAsync(req, async locations =>
    {
        await run(locations);
        return req.CreateResponse(HttpStatusCode.Accepted);
    }, ct);

    // Each refusal as the page needs it: Pro (402), no such location (404), a sign-in closed or something else in the
    // way (409), the location saying no (502).
    private async Task<ApiResponse> DoAsync(ApiRequest req, Func<BackupLocations, Task<ApiResponse>> run, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        (HttpStatusCode Status, string Why) refusal;
        try
        {
            return await run(_locations!);
        }
        catch (LocationLimitException ex) { refusal = (HttpStatusCode.PaymentRequired, ex.Message); }
        catch (OnlyLocationException ex) { refusal = (HttpStatusCode.PaymentRequired, ex.Message); }
        catch (ProNeededException ex) { refusal = (HttpStatusCode.PaymentRequired, ex.Message); }
        catch (KeyNotFoundException ex) { refusal = (HttpStatusCode.NotFound, ex.Message); }
        catch (ArgumentOutOfRangeException) { refusal = (HttpStatusCode.BadRequest, "Choose any location, or 2 locations."); }
        catch (InvalidOperationException ex) { refusal = (HttpStatusCode.Conflict, ex.Message); }
        // The person closed the sign-in sheet; were the page gone instead, no one reads the answer.
        catch (OperationCanceledException) { refusal = (HttpStatusCode.Conflict, "Signing in was closed. Nothing changed."); }
        catch (IOException ex) { refusal = (HttpStatusCode.BadGateway, ex.Message); }
        // Said whatever happens: a page gone simply never reads it.
        return await Answers.RefuseAsync(req, refusal.Status, refusal.Why, CancellationToken.None);
    }

    private Task<ApiResponse>? Refused(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return _locations is null ? Answers.RefuseAsync(req, HttpStatusCode.NotFound, "Only a phone backs up to locations.", ct) : null;
    }
}
