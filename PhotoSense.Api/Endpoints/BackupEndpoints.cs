using System.Globalization;
using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Application.Organizing;
using PhotoSense.Application.Transfer;
using PhotoSense.Contracts.Transfer;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Api.Endpoints;

/// <summary>
/// Backing up from a phone to a computer, as each sees it: a phone backs up, and says what is on the computer and
/// what is safe to delete; a computer lists the phones that back up to it. Each program answers its own half; asked
/// for the other, it says it is not the one.
/// </summary>
internal sealed class BackupEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private const string OnlyAPhone = "Only a phone backs up.";
    private const string OnlyAComputer = "Only a computer keeps phones' backups.";
    private readonly PhoneBackup? _phone;
    private readonly ComputerBackup? _computer;
    private readonly OrganizeLibrary _library;

    /// <summary>The phone's half or the computer's, whichever this program has.</summary>
    /// <param name="phone">The phone's half.</param>
    /// <param name="computer">The computer's half.</param>
    /// <param name="library">Gives the files that arrived on the computer ids, for their previews.</param>
    public BackupEndpoints(PhoneBackup? phone, ComputerBackup? computer, OrganizeLibrary library)
    {
        _phone = phone;
        _computer = computer;
        _library = library;
    }

    /// <summary>GET /api/backup: on a phone, how backing up stands, with what waits counted afresh.</summary>
    public async Task<ApiResponse> StateAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        return await AnswerAsync(req, Dto(await _phone!.StateAsync(ct)), ct);
    }

    /// <summary>POST /api/backup/start: on a phone, backs up what waits, or carries on a backup paused; or sends the pictures and videos chosen (Send now).</summary>
    public async Task<ApiResponse> StartAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        var chosen = (await req.ReadJsonAsync<BackupStartRequest>(Json, ct))?.Files;
        try { await _phone!.StartAsync(chosen, ct); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        return await AnswerAsync(req, Dto(await _phone.StateAsync(ct)), ct);
    }

    /// <summary>POST /api/backup/pause: on a phone, pauses the backup going on.</summary>
    public async Task<ApiResponse> PauseAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        _phone!.Pause();
        return await AnswerAsync(req, Dto(await _phone.StateAsync(ct)), ct);
    }

    /// <summary>POST /api/backup/auto: on a phone, backs up by itself if a backup is due, as the app opens or the system wakes it.</summary>
    public async Task<ApiResponse> AutomaticAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        await _phone!.AutomaticAsync(ct);
        return await AnswerAsync(req, Dto(await _phone.StateAsync(ct)), ct);
    }

    /// <summary>POST /api/backup/settings: on a phone, how it backs up by itself, and what Safe to delete keeps.</summary>
    public async Task<ApiResponse> SettingsAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        if (await req.ReadJsonAsync<BackupSettingsDto>(Json, ct) is not { } asked)
            return await RefuseAsync(req, HttpStatusCode.BadRequest, "Say how the phone backs up.", ct);
        try { await _phone!.SaveSettingsAsync(new BackupSettings(asked.Automatic, asked.OnlyWhileCharging, asked.IncludeVideos, asked.KeepFavorites, asked.KeepRecent), ct); }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        return await AnswerAsync(req, Dto(await _phone.StateAsync(ct)), ct);
    }

    /// <summary>GET /api/backup/library: on a phone, which of the library's pictures and videos are on the computer, and which wait.</summary>
    public async Task<ApiResponse> LibraryAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        var library = await _phone!.LibraryAsync(ct);
        return await AnswerAsync(req, new LibraryBackupDto { Computer = library.Computer, OnComputer = library.OnComputer, Waiting = library.Waiting, LastChecked = Time(library.LastChecked) }, ct);
    }

    /// <summary>GET /api/backup/safe: on a phone, what could be deleted from it as its copies are on the computer, and what the keep rules keep.</summary>
    public async Task<ApiResponse> SafeAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        var safe = await _phone!.SafeAsync(ct);
        return await AnswerAsync(req, new SafeReviewDto
        {
            Computer = safe.Computer, Folder = safe.Folder, LastChecked = Time(safe.LastChecked), Checked = Dto(safe.Checked), Favorites = Dto(safe.Favorites),
            Recent = Dto(safe.Recent), Removable = Dto(safe.Removable), StorageTotal = safe.Storage.Total, StorageFree = safe.Storage.Free, CloudPhotos = safe.CloudPhotos,
            Settings = Dto(safe.Settings),
        }, ct);
    }

    /// <summary>
    /// POST /api/backup/remove: on a phone, Safe to delete: sends to the phone's Recently Deleted or Trash what the
    /// computer holds, checked, less the keep rules, the largest first, after the phone's own question.
    /// </summary>
    public async Task<ApiResponse> RemoveAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _phone, OnlyAPhone, ct) is { } refused) return await refused;
        var atMost = (await req.ReadJsonAsync<SafeRemoveRequest>(Json, ct))?.AtMost;
        try
        {
            var removed = await _phone!.RemoveAsync(atMost, ct);
            return await AnswerAsync(req, new SafeRemovalDto
            {
                Removed = removed.Removed, Bytes = removed.Bytes, Left = removed.Left, StorageTotal = removed.Before.Total, StorageFreeBefore = removed.Before.Free,
            }, ct);
        }
        catch (InvalidOperationException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
        // The person said no to the phone's question, or the phone could not delete them.
        catch (IOException ex) { return await RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    /// <summary>GET /api/backup/phones: on a computer, every phone that backs up to it, with how its backups stand.</summary>
    public async Task<ApiResponse> PhonesAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _computer, OnlyAComputer, ct) is { } refused) return await refused;
        var phones = new List<BackupPhoneDto>();
        foreach (var phone in await _computer!.PhonesAsync(ct)) phones.Add(await DtoAsync(phone, ct));
        return await AnswerAsync(req, phones, ct);
    }

    /// <summary>POST /api/backup/phones/{id}/folder: on a computer, sends a phone's backups to another folder from now on.</summary>
    public async Task<ApiResponse> FolderAsync(ApiRequest req, string id, CancellationToken ct)
    {
        if (Refused(req, _computer, OnlyAComputer, ct) is { } refused) return await refused;
        var folder = (await req.ReadJsonAsync<BackupFolderRequest>(Json, ct))?.Folder;
        if (string.IsNullOrWhiteSpace(folder)) return await RefuseAsync(req, HttpStatusCode.BadRequest, "Choose a folder.", ct);
        try { return await AnswerAsync(req, await DtoAsync(await _computer!.MoveToAsync(id, folder, ct), ct), ct); }
        catch (ArgumentException ex) { return await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message, ct); }
        catch (DirectoryNotFoundException ex) { return await RefuseAsync(req, HttpStatusCode.BadRequest, ex.Message, ct); }
    }

    /// <summary>POST /api/backup/phones/{id}/pause: on a computer, pauses a phone's backups, or lets them run again.</summary>
    public async Task<ApiResponse> PausePhoneAsync(ApiRequest req, string id, CancellationToken ct)
    {
        if (Refused(req, _computer, OnlyAComputer, ct) is { } refused) return await refused;
        var paused = (await req.ReadJsonAsync<BackupPauseRequest>(Json, ct))?.Paused == true;
        try { return await AnswerAsync(req, await DtoAsync(await _computer!.PauseAsync(id, paused, ct), ct), ct); }
        catch (ArgumentException ex) { return await RefuseAsync(req, HttpStatusCode.NotFound, ex.Message, ct); }
    }

    private async Task<BackupPhoneDto> DtoAsync(BackupPhone phone, CancellationToken ct)
    {
        var (held, heldBytes) = await _computer!.HeldAsync(phone.Id, ct);
        ArrivingBackupDto? arriving = null;
        if (_computer.Arriving(phone.Id) is { } now)
        {
            var ids = new List<string>();
            foreach (var path in now.Arrived)
                if (await _library.DescribeAsync(path, ct) is { } file) ids.Add(file.Id);
            arriving = new ArrivingBackupDto { Done = now.Done, Total = now.Total, Bytes = now.Bytes, TotalBytes = now.TotalBytes, Started = Time(now.Started)!, Arrived = ids };
        }
        return new BackupPhoneDto
        {
            Id = phone.Id, Name = phone.Name, Kind = phone.Kind, Folder = _computer.Named(phone.Folder), Path = phone.Folder, Paused = phone.Paused, Paired = Time(phone.Paired)!,
            LastBackup = Time(phone.LastBackup), LastChecked = Time(phone.LastChecked), Held = held, HeldBytes = heldBytes, Safe = phone.Safe, SafeBytes = phone.SafeBytes,
            Arriving = arriving, Events = [.. (await _computer.EventsAsync(phone.Id, ct)).Select(e => new BackupEventDto { At = Time(e.At)!, Text = e.Text })],
        };
    }

    private static BackupStateDto Dto(BackupState s) => new()
    {
        Stage = s.Stage.ToString().ToLowerInvariant(), Computer = s.Computer, Folder = s.Folder, Waiting = s.Waiting, WaitingBytes = s.WaitingBytes, OnComputer = s.OnComputer,
        Done = s.Done, Total = s.Total, Bytes = s.Bytes, TotalBytes = s.TotalBytes, LastChecked = Time(s.LastChecked), LastBackup = Time(s.LastBackup),
        Strip = [.. s.Strip.Select(t => new BackupTileDto { Id = t.Id, Done = t.Done })], Problem = s.Problem, Settings = Dto(s.Settings),
    };

    private static BackupSettingsDto Dto(BackupSettings s) => new()
    {
        Automatic = s.Automatic, OnlyWhileCharging = s.OnlyWhileCharging, IncludeVideos = s.IncludeVideos, KeepFavorites = s.KeepFavorites, KeepRecent = s.KeepRecent,
    };

    private static ItemCountDto Dto(ItemCount c) => new() { Count = c.Count, Bytes = c.Bytes };

    private static string? Time(DateTimeOffset? at) => at?.ToString("O", CultureInfo.InvariantCulture);

    private static Task<ApiResponse>? Refused(ApiRequest req, object? service, string why, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return service is null ? RefuseAsync(req, HttpStatusCode.NotFound, why, ct) : null;
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
