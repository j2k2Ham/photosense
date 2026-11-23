namespace Trovolve.Domain.Configuration;

/// <summary>
/// How the product is set up for everyone: settings the person cannot change, set by those who make Trovolve. They are
/// written in the code now; <see cref="IProductSettingsSource"/> lets them come from a configuration service later.
/// </summary>
/// <param name="FreeRemovalsPerDay">How many photos and videos the free plan removes each day.</param>
public sealed record ProductSettings(int FreeRemovalsPerDay);

/// <summary>Where the product's settings come from: the code, or a configuration service such as AWS AppConfig or Azure App Configuration.</summary>
public interface IProductSettingsSource
{
    /// <summary>The settings as they are now.</summary>
    ValueTask<ProductSettings> GetAsync(CancellationToken ct);
}
