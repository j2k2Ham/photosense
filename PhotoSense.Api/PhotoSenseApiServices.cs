using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Application.Transfer;
using PhotoSense.Api.About;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Scanning;

namespace PhotoSense.Api;

public static class PhotoSenseApiServices
{
    /// <summary>The endpoints, the addresses that reach them, and what runs scans in the background.</summary>
    public static IServiceCollection AddPhotoSenseApi(this IServiceCollection s)
    {
        s.AddSingleton<PhotoDtoMapper>();
        s.AddSingleton<ScanGroupingFacade>();
        s.AddSingleton<OrganizeListingMapper>();
        s.AddSingleton<ScanRunner>();
        s.AddSingleton(ProductInfo.Bundled);
        s.AddSingleton<HealthEndpoints>();
        s.AddSingleton<AboutEndpoints>();
        s.AddSingleton<FoldersEndpoints>();
        s.AddSingleton<LibraryEndpoints>();
        s.AddSingleton<PhotosEndpoints>();
        s.AddSingleton<DuplicateGroupsEndpoints>();
        s.AddSingleton<ScanStatusEndpoints>();
        s.AddSingleton<ScanStartEndpoints>();
        s.AddSingleton<ScanResetEndpoints>();
        s.AddSingleton<ScanProgressEndpoints>();
        s.AddSingleton<ScanLogsEndpoints>();
        s.AddSingleton<OrganizeEndpoints>();
        s.AddSingleton<RemovedEndpoints>();
        // A phone sends and a computer receives: each program has the one half.
        s.AddSingleton(sp => new TransferEndpoints(sp.GetService<PhoneSending>(), sp.GetService<PhoneReceiving>()));
        s.AddSingleton<PhotoSenseRoutes>();
        return s;
    }
}
