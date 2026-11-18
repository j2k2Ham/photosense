using System.Globalization;
using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Transfer;
using PhotoSense.Contracts.Organize;
using PhotoSense.Domain.Entities;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Api.Endpoints;

/// <summary>Fix dates and places, and naming the phone's copies on the computer by date: only a phone has them.</summary>
internal sealed class FixEndpoints
{
    /// <summary>How many photos each kind of fix shows.</summary>
    public const int Shown = 6;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly PhotoFixes? _fixes;
    private readonly PhoneBackup? _backup;
    private readonly IPhotoRepository _repo;
    private readonly PhotoDtoMapper _mapper;

    /// <summary>Fixes, where this program has them.</summary>
    /// <param name="fixes">Fix dates and places; null on a computer.</param>
    /// <param name="backup">Backing up to the computer, which renames the copies; null on a computer.</param>
    /// <param name="repo">What the scans recorded, for the example of a name by date.</param>
    /// <param name="mapper">Makes a photo into what the page is sent.</param>
    public FixEndpoints(PhotoFixes? fixes, PhoneBackup? backup, IPhotoRepository repo, PhotoDtoMapper mapper)
    {
        _fixes = fixes;
        _backup = backup;
        _repo = repo;
        _mapper = mapper;
    }

    /// <summary>GET /api/fixes: what needs a date or a place, what can be undone, and how the computer names the phone's copies.</summary>
    public async Task<ApiResponse> ReportAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        var report = await _fixes!.ReportAsync(ct);
        static bool Once(Photo p) => !(p.IsVideo && p.LivePhotoId is not null);
        var dates = report.WrongDates.Where(Once).ToList();
        var places = report.Places.Select(p => p.Photo).Where(Once).ToList();
        return await Answers.JsonAsync(req, new FixesDto
        {
            Total = dates.Concat(places).Select(p => p.Id).Distinct().Count(),
            Dates = new FixGroupDto
            {
                Count = dates.Count, Photos = [.. dates.Take(Shown).Select(_mapper.Map)], Undo = report.UndoDates,
                Date = report.CommonestWrongDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            },
            Places = new FixGroupDto
            {
                Count = places.Count, Photos = [.. places.Take(Shown).Select(_mapper.Map)], Place = report.TopPlace, PlaceCount = report.TopPlaceCount, Undo = report.UndoPlaces,
            },
            Names = await ComputerNamesAsync(ct),
        }, ct);
    }

    /// <summary>POST /api/fixes/dates: gives every photo with a wrong date the day asked, each keeping its time of day, after the phone asks.</summary>
    public async Task<ApiResponse> DatesAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<FixDatesRequest>(Json, ct) is not { } asked || !DateOnly.TryParseExact(asked.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Choose the date they were taken.", ct);
        return await ChangedAsync(req, () => _fixes!.FixDatesAsync(day, ct), ct);
    }

    /// <summary>POST /api/fixes/places: gives every photo with no place that of the one taken nearest in time, within 30 minutes, after the phone asks.</summary>
    public async Task<ApiResponse> PlacesAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        return await ChangedAsync(req, () => _fixes!.AddPlacesAsync(ct), ct);
    }

    /// <summary>POST /api/fixes/undo: puts back what the last fix of a kind changed, after the phone asks.</summary>
    public async Task<ApiResponse> UndoAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<FixKindRequest>(Json, ct) is not { } asked || !Enum.TryParse<FixKind>(asked.Kind, ignoreCase: true, out var kind))
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Say which fix to undo.", ct);
        return await ChangedAsync(req, () => _fixes!.UndoAsync(kind, ct), ct);
    }

    /// <summary>POST /api/fixes/names: has the computer name the phone's copies by date, and those that arrive from then on; it goes on by itself.</summary>
    public async Task<ApiResponse> NamesAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<FixKindRequest>(Json, ct) is not { } asked || !Enum.TryParse<FileNaming>(asked.Kind, ignoreCase: true, out var naming))
            return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Say how to name the files.", ct);
        try
        {
            await _backup!.RenameOnComputerAsync(naming, ct);
            return req.CreateResponse(HttpStatusCode.Accepted);
        }
        catch (InvalidOperationException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>A naming as the page writes it: "asOnPhone", "date", "datePlace".</summary>
    public static string Style(FileNaming naming) => JsonNamingPolicy.CamelCase.ConvertName(naming.ToString());

    private static async Task<ApiResponse> ChangedAsync(ApiRequest req, Func<Task<int>> fix, CancellationToken ct)
    {
        try { return await Answers.JsonAsync(req, new FixResultDto { Changed = await fix() }, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    // The copies on the computer, and an example of a name by date from the latest photo that says when it was taken.
    private async Task<ComputerNamesDto?> ComputerNamesAsync(CancellationToken ct)
    {
        if (await _backup!.ComputerAsync(ct) is not { } computer) return null;
        var latest = (await _repo.GetAllAsync(ct)).Where(p => p.TakenOn is not null && !(p.IsVideo && p.LivePhotoId is not null)).MaxBy(p => p.TakenOn);
        var run = _backup.NamesRun();
        return new ComputerNamesDto
        {
            Computer = computer.Name, Files = await _backup.HeldAsync(ct), Style = Style(computer.Names),
            Now = latest?.FileName, After = latest is null ? null : DetailRules.NameByDate(latest.TakenOn!.Value, null, Path.GetExtension(latest.FileName)),
            Stage = run is null ? null : JsonNamingPolicy.CamelCase.ConvertName(run.Stage.ToString()), Problem = run?.Problem, Renamed = run?.Renamed,
        };
    }

    private Task<ApiResponse>? Refused(ApiRequest req, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return _fixes is null ? Answers.RefuseAsync(req, HttpStatusCode.NotFound, "Only a phone fixes dates and places.", ct) : null;
    }
}
