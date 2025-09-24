using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Application.Scanning.Services;
using PhotoSense.Core.Domain.Repositories;
using PhotoSense.Core.Domain.Services;
using PhotoSense.Infrastructure.Hashing;
using PhotoSense.Infrastructure.Metadata;
using PhotoSense.Infrastructure.Persistence;
using PhotoSense.Infrastructure.Events;
using PhotoSense.Infrastructure.Deletion;
using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Application.Photos.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddSingleton<IPhotoRepository>(_ => new LiteDbPhotoRepository());
builder.Services.AddSingleton<IImageHashingService, PerceptualHashingService>();
builder.Services.AddSingleton<IPhotoMetadataExtractor, BasicExifMetadataExtractor>();
builder.Services.AddSingleton<IDuplicateGroupingService, DuplicateGroupingService>();
builder.Services.AddSingleton<INearDuplicateService, NearDuplicateService>();
builder.Services.AddSingleton<IScanRequestPublisher, ScanRequestPublisher>();
builder.Services.AddSingleton<IIntegrationEventPublisher, InMemoryIntegrationEventPublisher>();
builder.Services.AddSingleton<IPhotoDeletionService, FileSystemPhotoDeletionService>();
builder.Services.AddSingleton<IPhotoQueryService, PhotoQueryService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
