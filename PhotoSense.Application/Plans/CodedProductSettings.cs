using PhotoSense.Domain.Configuration;

namespace PhotoSense.Application.Plans;

/// <summary>
/// The product's settings as written here, in the code, where only those who make PhotoSense change them (Jamie,
/// 2026-11-13). A configuration service such as AWS AppConfig or Azure App Configuration can take its place by
/// registering another <see cref="IProductSettingsSource"/>.
/// </summary>
public sealed class CodedProductSettings : IProductSettingsSource
{
    /// <summary>How many photos and videos the free plan removes each day.</summary>
    public const int FreeRemovalsPerDay = 50;

    private static readonly ProductSettings Settings = new(FreeRemovalsPerDay);

    /// <inheritdoc />
    public ValueTask<ProductSettings> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Settings);
    }
}
