using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;
using PhotoSense.Api.Scanning;

namespace PhotoSense.Api;

/// <summary>Every address of the service below /api, with the endpoint that answers it.</summary>
public sealed class PhotoSenseRoutes
{
    /// <summary>The addresses, each answered by one of the endpoints given; <see cref="PhotoSenseApiServices.AddPhotoSenseApi"/> makes it.</summary>
    internal PhotoSenseRoutes(HealthEndpoints health, AboutEndpoints about, FoldersEndpoints folders, PhotosEndpoints photos, DuplicateGroupsEndpoints groups, ScanStatusEndpoints status,
        ScanStartEndpoints start, ScanResetEndpoints reset, ScanProgressEndpoints progress, ScanLogsEndpoints logs, OrganizeEndpoints organize, LibraryEndpoints library,
        RemovedEndpoints removed, TransferEndpoints transfer)
    {
        Router = new ApiRouter()
            .Get("health", (r, _, ct) => health.GetAsync(r, ct))
            .Get("about", (r, _, ct) => about.GetAsync(r, ct))
            .Get("about/icon", (r, _, ct) => about.IconAsync(r, ct))
            .Get("about/{document}", (r, v, ct) => about.TextAsync(r, v["document"], ct))
            .Get("folders", (r, _, ct) => folders.BrowseAsync(r, ct))
            .Get("library/access", (r, _, ct) => library.AccessAsync(r, ct))
            .Post("library/ask", (r, _, ct) => library.AskAsync(r, ct))
            .Post("library/choose-more", (r, _, ct) => library.ChooseMoreAsync(r, ct))
            // Opening the settings is done at once, with nothing to wait for or stop.
            .Post("library/open-settings", (r, _, _) => Task.FromResult(library.OpenSettings(r)))
            .Get("photos/{id:guid}/thumbnail", (r, v, ct) => photos.GetThumbnailAsync(r, v["id"], ct))
            .Get("photos/{id:guid}/image", (r, v, ct) => photos.GetImageAsync(r, v["id"], ct))
            .Get("photos/{id:guid}/video", (r, v, ct) => photos.GetVideoAsync(r, v["id"], ct))
            .Post("photos/{id:guid}/open", (r, v, ct) => photos.OpenPhotoAsync(r, v["id"], ct))
            .Delete("photos/{id:guid}", (r, v, ct) => photos.DeletePhotoAsync(r, v["id"], ct))
            .Post("photos/{id:guid}/keep", (r, v, ct) => photos.KeepPhotoAsync(r, v["id"], ct))
            .Post("photos/bulk/remove-duplicates", (r, _, ct) => photos.RemoveDuplicatesAsync(r, ct))
            .Get("scan/status", (r, _, ct) => status.GetStatusAsync(r, ct))
            .Get("scan/groups", (r, _, ct) => groups.GetDuplicateGroupsAsync(r, ct))
            .Post("scan/start", (r, _, ct) => start.StartAsync(r, ct))
            .Post("scan/stop/{instanceId}", (r, v, ct) => start.StopAsync(r, v["instanceId"], ct))
            .Post("scan/reset", (r, _, ct) => reset.ResetAsync(r, ct))
            .Get("scan/progress/{instanceId}", (r, v, ct) => progress.GetAsync(r, v["instanceId"], ct))
            .Get("scan/logs", (r, _, ct) => logs.GetPendingAsync(r, ct))
            .Get("organize/files", (r, _, ct) => organize.FilesAsync(r, ct))
            .Get("organize/progress", (r, _, ct) => organize.ProgressAsync(r, ct))
            .Post("organize/plan", (r, _, ct) => organize.PlanAsync(r, ct))
            .Post("organize/apply", (r, _, ct) => organize.ApplyAsync(r, ct))
            .Post("organize/remove", (r, _, ct) => organize.RemoveAsync(r, ct))
            .Post("organize/undo/{batchId}", (r, v, ct) => organize.UndoAsync(r, v["batchId"], ct))
            .Get("organize/batches", (r, _, ct) => organize.BatchesAsync(r, ct))
            .Get("organize/files/{id}/thumbnail", (r, v, ct) => organize.ThumbnailAsync(r, v["id"], ct))
            .Get("organize/files/{id}/image", (r, v, ct) => organize.ImageAsync(r, v["id"], ct))
            .Post("organize/files/{id}/open", (r, v, ct) => organize.OpenAsync(r, v["id"], ct))
            .Get("removed", (r, _, ct) => removed.FindAsync(r, ct))
            .Post("removed/erase", (r, _, ct) => removed.EraseAsync(r, ct))
            .Post("send/start", (r, _, ct) => transfer.StartSendingAsync(r, ct))
            .Get("send/state", (r, _, ct) => transfer.SendingAsync(r, ct))
            .Post("send/confirm", (r, _, ct) => transfer.ConfirmSendingAsync(r, ct))
            .Post("send/stop", (r, _, ct) => transfer.StopSendingAsync(r, ct))
            .Post("send/delete-sent", (r, _, ct) => transfer.DeleteSentAsync(r, ct))
            .Get("phones", (r, _, ct) => transfer.PhonesAsync(r, ct))
            .Post("receive/start", (r, _, ct) => transfer.StartReceivingAsync(r, ct))
            .Get("receive/state", (r, _, ct) => transfer.ReceivingAsync(r, ct))
            .Post("receive/confirm", (r, _, ct) => transfer.ConfirmReceivingAsync(r, ct))
            .Post("receive/stop", (r, _, ct) => transfer.StopReceivingAsync(r, ct));
    }

    /// <summary>The addresses, for the program that received a request to ask for its answer.</summary>
    public ApiRouter Router { get; }
}
