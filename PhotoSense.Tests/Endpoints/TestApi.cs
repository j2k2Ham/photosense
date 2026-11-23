using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PhotoSense.Api;
using PhotoSense.Api.About;
using PhotoSense.Api.Http;
using PhotoSense.Application;
using PhotoSense.Desktop.Imaging;
using PhotoSense.Domain.Configuration;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure;
using PhotoSense.Infrastructure.Files;

namespace PhotoSense.Tests.Endpoints;

/// <summary>
/// The service's addresses as a program has them, with every endpoint behind them: a request is answered through the
/// router and the request guard, as a host hands it on. What a test does not give is a stand-in that touches nothing:
/// stores and previews in memory, a clock that stands still, and this computer's disks.
/// </summary>
internal sealed class TestApi
{
    private readonly ApiRouter _router;

    /// <summary>The addresses, with the test's own services in place of the stand-ins.</summary>
    /// <param name="with">Registers what the test gives; each replaces what was there for its type.</param>
    public TestApi(Action<IServiceCollection>? with = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new PhotoStorageOptions()))
            .AddSingleton<TimeProvider>(new TestClock())
            .AddSingleton<IPhotoFileSystem>(TestFiles.Disk)
            .AddSingleton<IImageAnalyzer>(new MagickImageAnalyzer())
            .AddSingleton(Mock.Of<ISystemViewer>())
            .AddSingleton(Mock.Of<IFolderBrowser>())
            .AddSingleton<IPhotoAccess>(new FolderAccess())
            .AddSingleton(ProgramInfo.From(typeof(TestApi).Assembly, "server"))
            .AddPhotoSenseApplication()
            .AddPhotoSenseCore()
            .AddStandInStores()
            .AddSingleton<IThumbnailStore>(new InMemoryThumbnailStore())
            .AddPhotoSenseApi();
        with?.Invoke(services);
        Services = services.BuildServiceProvider();
        _router = Services.GetRequiredService<PhotoSenseRoutes>().Router;
    }

    /// <summary>The program's services, as the endpoints were given them.</summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Answers a request as the router does, under the address given (with the request's own query) or the request's own.
    /// </summary>
    /// <exception cref="InvalidOperationException">No address answers it, which a host would answer as not found.</exception>
    public async Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken ct, string? address = null)
    {
        var url = address is null ? request.Url : new Uri($"http://localhost:7071/api/{address}{request.Url.Query}");
        var asked = new ApiRequest(request.Method, url, request.Body, ct);
        foreach (var (name, values) in request.Headers) asked.Headers.TryAddWithoutValidation(name, values);
        return await (_router.Answer(asked) ?? throw new InvalidOperationException($"No address answers {request.Method} {url.AbsolutePath}."));
    }

    /// <summary>A value as it is written into an address.</summary>
    public static string Part(string value) => Uri.EscapeDataString(value);
}

/// <summary>The addresses of the health check, About, the folders and the phone's library, by the names of the endpoints behind them.</summary>
internal sealed class AboutApi(TestApi api)
{
    /// <summary>GET /health.</summary>
    public Task<ApiResponse> HealthAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "health");
    /// <summary>GET /about.</summary>
    public Task<ApiResponse> GetAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "about");
    /// <summary>GET /about/icon.</summary>
    public Task<ApiResponse> IconAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "about/icon");
    /// <summary>GET /about/{document}.</summary>
    public Task<ApiResponse> TextAsync(ApiRequest r, string document, CancellationToken ct) => api.SendAsync(r, ct, $"about/{TestApi.Part(document)}");
    /// <summary>GET /folders.</summary>
    public Task<ApiResponse> BrowseAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "folders");
    /// <summary>GET /library/access.</summary>
    public Task<ApiResponse> AccessAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "library/access");
    /// <summary>POST /library/ask.</summary>
    public Task<ApiResponse> AskAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "library/ask");
    /// <summary>POST /library/choose-more.</summary>
    public Task<ApiResponse> ChooseMoreAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "library/choose-more");
    /// <summary>POST /library/open-settings.</summary>
    public Task<ApiResponse> OpenSettingsAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "library/open-settings");
}

/// <summary>The addresses of Clean up's photos, by the names of the endpoint behind them.</summary>
internal sealed class PhotosApi(TestApi api)
{
    /// <summary>GET /photos/{id}/thumbnail.</summary>
    public Task<ApiResponse> GetThumbnailAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"photos/{TestApi.Part(id)}/thumbnail");
    /// <summary>GET /photos/{id}/image.</summary>
    public Task<ApiResponse> GetImageAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"photos/{TestApi.Part(id)}/image");
    /// <summary>GET /photos/{id}/video.</summary>
    public Task<ApiResponse> GetVideoAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"photos/{TestApi.Part(id)}/video");
    /// <summary>POST /photos/{id}/open.</summary>
    public Task<ApiResponse> OpenPhotoAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"photos/{TestApi.Part(id)}/open");
    /// <summary>DELETE /photos/{id}.</summary>
    public Task<ApiResponse> DeletePhotoAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"photos/{TestApi.Part(id)}");
    /// <summary>POST /photos/{id}/keep.</summary>
    public Task<ApiResponse> KeepPhotoAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"photos/{TestApi.Part(id)}/keep");
    /// <summary>POST /photos/bulk/remove-duplicates.</summary>
    public Task<ApiResponse> RemoveDuplicatesAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "photos/bulk/remove-duplicates");
}

/// <summary>The addresses of scanning, by the names of the endpoints behind them.</summary>
internal sealed class ScanApi(TestApi api)
{
    /// <summary>GET /scan/status.</summary>
    public Task<ApiResponse> GetStatusAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "scan/status");
    /// <summary>GET /scan/groups.</summary>
    public Task<ApiResponse> GetDuplicateGroupsAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "scan/groups");
    /// <summary>POST /scan/start.</summary>
    public Task<ApiResponse> StartAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "scan/start");
    /// <summary>POST /scan/stop/{instanceId}.</summary>
    public Task<ApiResponse> StopAsync(ApiRequest r, string instanceId, CancellationToken ct) => api.SendAsync(r, ct, $"scan/stop/{TestApi.Part(instanceId)}");
    /// <summary>POST /scan/reset.</summary>
    public Task<ApiResponse> ResetAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "scan/reset");
    /// <summary>GET /scan/progress/{instanceId}.</summary>
    public Task<ApiResponse> GetAsync(ApiRequest r, string instanceId, CancellationToken ct) => api.SendAsync(r, ct, $"scan/progress/{TestApi.Part(instanceId)}");
    /// <summary>GET /scan/logs.</summary>
    public Task<ApiResponse> GetPendingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "scan/logs");
}

/// <summary>The addresses of Organize and of what has been removed, by the names of the endpoints behind them.</summary>
internal sealed class OrganizeApi(TestApi api)
{
    /// <summary>GET /organize/files.</summary>
    public Task<ApiResponse> FilesAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "organize/files");
    /// <summary>GET /organize/progress.</summary>
    public Task<ApiResponse> ProgressAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "organize/progress");
    /// <summary>POST /organize/plan.</summary>
    public Task<ApiResponse> PlanAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "organize/plan");
    /// <summary>POST /organize/apply.</summary>
    public Task<ApiResponse> ApplyAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "organize/apply");
    /// <summary>POST /organize/remove.</summary>
    public Task<ApiResponse> RemoveAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "organize/remove");
    /// <summary>POST /organize/undo/{batchId}.</summary>
    public Task<ApiResponse> UndoAsync(ApiRequest r, string batchId, CancellationToken ct) => api.SendAsync(r, ct, $"organize/undo/{TestApi.Part(batchId)}");
    /// <summary>GET /organize/batches.</summary>
    public Task<ApiResponse> BatchesAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "organize/batches");
    /// <summary>GET /organize/files/{id}/thumbnail.</summary>
    public Task<ApiResponse> ThumbnailAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"organize/files/{TestApi.Part(id)}/thumbnail");
    /// <summary>GET /organize/files/{id}/image.</summary>
    public Task<ApiResponse> ImageAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"organize/files/{TestApi.Part(id)}/image");
    /// <summary>POST /organize/files/{id}/open.</summary>
    public Task<ApiResponse> OpenAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"organize/files/{TestApi.Part(id)}/open");
    /// <summary>GET /removed.</summary>
    public Task<ApiResponse> FindAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "removed");
    /// <summary>POST /removed/erase.</summary>
    public Task<ApiResponse> EraseAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "removed/erase");
}

/// <summary>The addresses of sending from a phone and receiving on a computer, by the names of the endpoint behind them.</summary>
internal sealed class TransferApi(TestApi api)
{
    /// <summary>POST /send/start.</summary>
    public Task<ApiResponse> StartSendingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "send/start");
    /// <summary>GET /send/state.</summary>
    public Task<ApiResponse> SendingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "send/state");
    /// <summary>POST /send/confirm.</summary>
    public Task<ApiResponse> ConfirmSendingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "send/confirm");
    /// <summary>POST /send/stop.</summary>
    public Task<ApiResponse> StopSendingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "send/stop");
    /// <summary>POST /send/delete-sent.</summary>
    public Task<ApiResponse> DeleteSentAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "send/delete-sent");
    /// <summary>GET /phones.</summary>
    public Task<ApiResponse> PhonesAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "phones");
    /// <summary>POST /receive/start.</summary>
    public Task<ApiResponse> StartReceivingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "receive/start");
    /// <summary>GET /receive/state.</summary>
    public Task<ApiResponse> ReceivingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "receive/state");
    /// <summary>POST /receive/confirm.</summary>
    public Task<ApiResponse> ConfirmReceivingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "receive/confirm");
    /// <summary>POST /receive/stop.</summary>
    public Task<ApiResponse> StopReceivingAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "receive/stop");
}

/// <summary>The addresses of the plan, by the names of the endpoints behind them.</summary>
internal sealed class PlanApi(TestApi api)
{
    /// <summary>GET /plan.</summary>
    public Task<ApiResponse> GetAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "plan");
    /// <summary>POST /plan/buy.</summary>
    public Task<ApiResponse> BuyAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "plan/buy");
    /// <summary>POST /plan/restore.</summary>
    public Task<ApiResponse> RestoreAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "plan/restore");
    /// <summary>POST /plan/manage.</summary>
    public Task<ApiResponse> ManageAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "plan/manage");
}

/// <summary>The addresses of pairing a phone with a computer, and of what a scan found, by the names of the endpoints behind them.</summary>
internal sealed class PairingApi(TestApi api)
{
    /// <summary>POST /pairing/open.</summary>
    public Task<ApiResponse> OpenAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "pairing/open");
    /// <summary>POST /pairing/name.</summary>
    public Task<ApiResponse> NameAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "pairing/name");
    /// <summary>POST /pairing/close.</summary>
    public Task<ApiResponse> CloseAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "pairing/close");
    /// <summary>POST /pairing/scan.</summary>
    public Task<ApiResponse> ScanAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "pairing/scan");
    /// <summary>POST /pairing/code.</summary>
    public Task<ApiResponse> CodeAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "pairing/code");
    /// <summary>GET /pairing/state.</summary>
    public Task<ApiResponse> StateAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "pairing/state");
    /// <summary>POST /pairing/confirm.</summary>
    public Task<ApiResponse> ConfirmAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "pairing/confirm");
    /// <summary>POST /pairing/stop.</summary>
    public Task<ApiResponse> StopAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "pairing/stop");
    /// <summary>GET /scan/found.</summary>
    public Task<ApiResponse> FoundAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "scan/found");
}

/// <summary>The backup addresses, by the names of the endpoints that answer them.</summary>
internal sealed class BackupApi(TestApi api)
{
    /// <summary>GET /backup.</summary>
    public Task<ApiResponse> StateAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup");
    /// <summary>POST /backup/start.</summary>
    public Task<ApiResponse> StartAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup/start");
    /// <summary>POST /backup/pause.</summary>
    public Task<ApiResponse> PauseAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup/pause");
    /// <summary>POST /backup/auto.</summary>
    public Task<ApiResponse> AutomaticAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup/auto");
    /// <summary>POST /backup/settings.</summary>
    public Task<ApiResponse> SettingsAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup/settings");
    /// <summary>GET /backup/library.</summary>
    public Task<ApiResponse> LibraryAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup/library");
    /// <summary>GET /backup/safe.</summary>
    public Task<ApiResponse> SafeAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup/safe");
    /// <summary>POST /backup/remove.</summary>
    public Task<ApiResponse> RemoveAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup/remove");
    /// <summary>GET /backup/phones.</summary>
    public Task<ApiResponse> PhonesAsync(ApiRequest r, CancellationToken ct) => api.SendAsync(r, ct, "backup/phones");
    /// <summary>POST /backup/phones/{id}/folder.</summary>
    public Task<ApiResponse> FolderAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"backup/phones/{TestApi.Part(id)}/folder");
    /// <summary>GET /backup/phones/{id}/names.</summary>
    public Task<ApiResponse> RenamingAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"backup/phones/{TestApi.Part(id)}/names");
    /// <summary>POST /backup/phones/{id}/names.</summary>
    public Task<ApiResponse> RenameAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"backup/phones/{TestApi.Part(id)}/names");
    /// <summary>POST /backup/phones/{id}/trips.</summary>
    public Task<ApiResponse> TripsFolderAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"backup/phones/{TestApi.Part(id)}/trips");
    /// <summary>POST /backup/phones/{id}/pause.</summary>
    public Task<ApiResponse> PausePhoneAsync(ApiRequest r, string id, CancellationToken ct) => api.SendAsync(r, ct, $"backup/phones/{TestApi.Part(id)}/pause");
}
