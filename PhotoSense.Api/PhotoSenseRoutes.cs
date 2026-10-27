using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;
using PhotoSense.Api.Scanning;

namespace PhotoSense.Api;

/// <summary>Every address of the service below /api, with the endpoint that answers it.</summary>
public sealed class PhotoSenseRoutes
{
    public PhotoSenseRoutes(HealthEndpoints health, FoldersEndpoints folders, PhotosEndpoints photos, DuplicateGroupsEndpoints groups, ScanStatusEndpoints status,
        ScanStartEndpoints start, ScanResetEndpoints reset, ScanProgressEndpoints progress, ScanLogsEndpoints logs, OrganizeEndpoints organize,
        RemovedEndpoints removed, OutboxEndpoints outbox)
    {
        Router = new ApiRouter()
            .Get("health", (r, _) => health.GetAsync(r))
            .Get("folders", (r, _) => folders.BrowseAsync(r))
            .Get("audit", (r, _) => photos.GetAuditAsync(r))
            .Get("photos", (r, _) => photos.GetPhotosAsync(r))
            .Get("photos/{id:guid}", (r, v) => photos.GetPhotoAsync(r, v["id"]))
            .Get("photos/{id:guid}/thumbnail", (r, v) => photos.GetThumbnailAsync(r, v["id"]))
            .Get("photos/{id:guid}/image", (r, v) => photos.GetImageAsync(r, v["id"]))
            .Get("photos/{id:guid}/video", (r, v) => photos.GetVideoAsync(r, v["id"]))
            .Post("photos/{id:guid}/open", (r, v) => photos.OpenPhotoAsync(r, v["id"]))
            .Delete("photos/{id:guid}", (r, v) => photos.DeletePhotoAsync(r, v["id"]))
            .Post("photos/{id:guid}/keep", (r, v) => photos.KeepPhotoAsync(r, v["id"]))
            .Post("photos/{id:guid}/move", (r, v) => photos.MovePhotoAsync(r, v["id"]))
            .Post("photos/bulk/remove-duplicates", (r, _) => photos.RemoveDuplicatesAsync(r))
            .Get("scan/status", (r, _) => status.GetStatus(r))
            .Get("scan/groups", (r, _) => groups.GetDuplicateGroups(r))
            .Post("scan/start", (r, _) => start.StartAsync(r))
            .Post("scan/reset", (r, _) => reset.ResetAsync(r))
            .Get("scan/progress/{instanceId}", (r, v) => progress.Get(r, v["instanceId"]))
            // Before the address of one scan's lines, which would take these too.
            .Get("scan/logs", (r, _) => logs.GetPendingAsync(r))
            .Get("scan/logs/stream/{instanceId?}", (r, v) => logs.GetLogsStream(r, v.GetValueOrDefault("instanceId")))
            .Get("scan/logs/{instanceId?}", (r, v) => logs.GetLogs(r, v.GetValueOrDefault("instanceId")))
            .Get("organize/files", (r, _) => organize.FilesAsync(r))
            .Get("organize/progress", (r, _) => organize.ProgressAsync(r))
            .Post("organize/plan", (r, _) => organize.PlanAsync(r))
            .Post("organize/apply", (r, _) => organize.ApplyAsync(r))
            .Post("organize/remove", (r, _) => organize.RemoveAsync(r))
            .Post("organize/undo/{batchId}", (r, v) => organize.UndoAsync(r, v["batchId"]))
            .Get("organize/batches", (r, _) => organize.BatchesAsync(r))
            .Get("organize/files/{id}/thumbnail", (r, v) => organize.ThumbnailAsync(r, v["id"]))
            .Get("organize/files/{id}/image", (r, v) => organize.ImageAsync(r, v["id"]))
            .Post("organize/files/{id}/open", (r, v) => organize.OpenAsync(r, v["id"]))
            .Get("removed", (r, _) => removed.FindAsync(r))
            .Post("removed/erase", (r, _) => removed.EraseAsync(r))
            .Post("outbox/dispatch", (r, _) => outbox.RunManualAsync(r));
    }

    public ApiRouter Router { get; }
}
