using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Application.Transfer;
using PhotoSense.Api.About;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Scanning;
using PhotoSense.Application.Scanning;

namespace PhotoSense.Api;

/// <summary>The endpoints, for every host to add to its services.</summary>
public static class PhotoSenseApiServices
{
    /// <summary>The endpoints, the addresses that reach them, and what runs scans in the background.</summary>
    /// <param name="s">The services to add to.</param>
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
        // Made here: the endpoints it is given are this project's own.
        s.AddSingleton(sp => new PhotoSenseRoutes(
            sp.GetRequiredService<HealthEndpoints>(), sp.GetRequiredService<AboutEndpoints>(), sp.GetRequiredService<FoldersEndpoints>(),
            sp.GetRequiredService<PhotosEndpoints>(), sp.GetRequiredService<DuplicateGroupsEndpoints>(), sp.GetRequiredService<ScanStatusEndpoints>(),
            sp.GetRequiredService<ScanStartEndpoints>(), sp.GetRequiredService<ScanResetEndpoints>(), sp.GetRequiredService<ScanProgressEndpoints>(),
            sp.GetRequiredService<ScanLogsEndpoints>(), sp.GetRequiredService<OrganizeEndpoints>(), sp.GetRequiredService<LibraryEndpoints>(),
            sp.GetRequiredService<RemovedEndpoints>(), sp.GetRequiredService<TransferEndpoints>()));
        return s;
    }
}
