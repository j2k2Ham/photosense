using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
using PhotoSense.Core.Configuration;
using PhotoSense.Application.Scanning;
using LiteDB;

var host = new HostBuilder()
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.AddJsonFile("appsettings.json", optional: true)
           .AddEnvironmentVariables();
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, s) =>
    {
        s.Configure<PhotoStorageOptions>(ctx.Configuration.GetSection("PhotoStorage"));
        s.AddSingleton<LiteDatabase>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PhotoStorageOptions>>().Value;
            return new LiteDatabase(opts.DatabasePath);
        });

        s.AddSingleton<IPhotoRepository>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PhotoStorageOptions>>().Value;
            return new LiteDbPhotoRepository(opts.DatabasePath);
        });

        s.AddSingleton<IImageHashingService, PerceptualHashingService>();
        s.AddSingleton<IPhotoMetadataExtractor, BasicExifMetadataExtractor>();
        s.AddSingleton<IDuplicateGroupingService, DuplicateGroupingService>();
        s.AddSingleton<INearDuplicateService, NearDuplicateService>();
        s.AddSingleton<IScanRequestPublisher, ScanRequestPublisher>();
        s.AddSingleton<IOutboxStore, LiteDbOutboxStore>();
        s.AddSingleton<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        s.AddSingleton<IPhotoDeletionService, FileSystemPhotoDeletionService>();
        s.AddSingleton<IPhotoQueryService, PhotoQueryService>();
        s.AddSingleton<IPhotoSearchService, PhotoSearchService>();
        s.AddSingleton<IScanProgressStore, InMemoryScanProgressStore>();
        s.AddSingleton<IScanExecutionService, ScanExecutionService>();
    })
    .Build();

await host.RunAsync();
