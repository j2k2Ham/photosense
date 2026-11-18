using System.Globalization;
using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Transfer;
using PhotoSense.Contracts.Organize;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Api.Endpoints;

/// <summary>Trips, the map, home, and a trip's folder on the computer: only a phone has them.</summary>
internal sealed class TripEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly Trips? _trips;
    private readonly PhoneBackup? _backup;
    private readonly PhotoDtoMapper _mapper;

    /// <summary>Trips, where this program has them.</summary>
    /// <param name="trips">Trips; null on a computer.</param>
    /// <param name="backup">Backing up to the computer, which makes the trip folders; null on a computer.</param>
    /// <param name="mapper">Makes a photo into what the page is sent.</param>
    public TripEndpoints(Trips? trips, PhoneBackup? backup, PhotoDtoMapper mapper)
    {
        _trips = trips;
        _backup = backup;
        _mapper = mapper;
    }

    /// <summary>GET /api/trips: home, the trips, the latest first, the computer, and the trip folder being made.</summary>
    public async Task<ApiResponse> ListAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        var found = await _trips!.FindAsync(ct);
        var made = await _backup!.FoldersMadeAsync(ct);
        var computer = await _backup.ComputerAsync(ct);
        var folder = _backup.TripFolder() is { } run && found.Found.FirstOrDefault(t => t.Folder == run.Folder) is { } making
            ? new TripFolderDto { Id = making.Id, Stage = JsonNamingPolicy.CamelCase.ConvertName(run.Stage.ToString()), Problem = run.Problem, Made = run.Made }
            : null;
        return await Answers.JsonAsync(req, new TripsDto
        {
            Home = found.Home is { } home ? Town(home.Town, 0) : null, HomeChosen = found.Home?.Chosen ?? false,
            Trips = [.. found.Found.Select(t => Dto(t, made))], Computer = computer?.Name, TripsFolder = computer?.Trips, Folder = folder,
        }, ct);
    }

    /// <summary>GET /api/trips/towns: the towns photos were taken in, those on the most days first, to choose home from.</summary>
    public async Task<ApiResponse> TownsAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        return await Answers.JsonAsync(req, (await _trips!.TownsAsync(ct)).Select(t => Town(t.Town, t.Days)).ToList(), ct);
    }

    /// <summary>POST /api/trips/home: makes a town home, or with none lets PhotoSense work it out again.</summary>
    public async Task<ApiResponse> HomeAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<HomeRequest>(Json, ct) is not { } asked || asked.Town is { Town.Length: 0 })
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Say which town is home.", ct);
        await _trips!.ChooseHomeAsync(asked.Town is { } t ? new HomeTown(t.Town, t.Region, t.Country, t.Latitude, t.Longitude) : null, ct);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    /// <summary>GET /api/trips/{id}: a trip with its days.</summary>
    public async Task<ApiResponse> TripAsync(ApiRequest req, string id, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await FindAsync(id, ct) is not { } trip) return await Gone(req, ct);
        var made = await _backup!.FoldersMadeAsync(ct);
        return await Answers.JsonAsync(req, new TripDetailDto
        {
            Trip = Dto(trip, made),
            Days = [.. trip.Days.Select(d => new TripDayDto { Date = d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Photos = [.. d.Photos.Select(_mapper.Map)] })],
        }, ct);
    }

    /// <summary>
    /// POST /api/trips/{id}/folder: makes the trip's folder on the computer, sending first what is not there yet. It goes
    /// on by itself; GET /api/trips says how it stands.
    /// </summary>
    public async Task<ApiResponse> FolderAsync(ApiRequest req, string id, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await FindAsync(id, ct) is not { } trip) return await Gone(req, ct);
        try
        {
            await _backup!.MakeFolderAsync(trip.Folder, [.. trip.Photos.Select(p => p.SourcePath)], ct);
            return req.CreateResponse(HttpStatusCode.Accepted);
        }
        catch (InvalidOperationException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>POST /api/trips/{id}/name: names a trip as the person wants it, its folder too; with no name, PhotoSense's own again.</summary>
    public async Task<ApiResponse> RenameAsync(ApiRequest req, string id, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await FindAsync(id, ct) is null) return await Gone(req, ct);
        var asked = await req.ReadJsonAsync<TripNameRequest>(Json, ct);
        try
        {
            await _trips!.RenameAsync(id, asked?.Name, ct);
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (ArgumentException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, ex.Message, ct); }
    }

    private async Task<Trip?> FindAsync(string id, CancellationToken ct) => (await _trips!.FindAsync(ct)).Found.FirstOrDefault(t => t.Id == id);

    private static Task<ApiResponse> Gone(ApiRequest req, CancellationToken ct)
        => Answers.RefuseAsync(req, HttpStatusCode.NotFound, "That trip is no longer there. Its photos may have changed since.", ct);

    private TripDto Dto(Trip t, IReadOnlySet<string> made) => new()
    {
        Id = t.Id, Name = t.Name, Folder = t.Folder, Start = Day(t.Start), End = Day(t.End), Photos = t.Photos.Count, Videos = t.Videos,
        Latitude = t.Latitude, Longitude = t.Longitude, Cover = _mapper.Map(t.Cover), FolderMade = made.Contains(t.Folder), Suggested = t.Suggested,
    };

    private static string Day(DateTime when) => when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TownDto Town(HomeTown t, int days) => new() { Town = t.Town, Region = t.Region, Country = t.Country, Latitude = t.Latitude, Longitude = t.Longitude, Days = days };

    private Task<ApiResponse>? Refused(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return _trips is null ? Answers.RefuseAsync(req, HttpStatusCode.NotFound, "Only a phone has Trips.", ct) : null;
    }
}
