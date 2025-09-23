using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.Services;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Infrastructure.Events;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Application.Photos.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(s =>
    {
        s.AddSingleton<IPhotoRepository, InMemoryPhotoRepository>();
        s.AddSingleton<IImageHashingService, PerceptualHashingService>();
        s.AddSingleton<IPhotoMetadataExtractor, BasicExifMetadataExtractor>();
        s.AddSingleton<IDuplicateGroupingService, DuplicateGroupingService>();
        s.AddSingleton<IScanRequestPublisher, ScanRequestPublisher>();
        s.AddSingleton<IIntegrationEventPublisher, InMemoryIntegrationEventPublisher>();
        s.AddSingleton<IPhotoDeletionService, FileSystemPhotoDeletionService>();
        s.AddSingleton<IPhotoQueryService, PhotoQueryService>();
    })
    .Build();

await host.RunAsync();
