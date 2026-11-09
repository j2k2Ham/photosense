using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Transfer;

/// <summary>The phones every way of finding them finds, looked for all at once.</summary>
/// <param name="finders">The ways of finding them.</param>
public sealed class PhoneFinders(params IPhoneFinder[] finders) : IPhoneFinder
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<FoundPhone>> FindAsync(CancellationToken ct)
        => [.. (await Task.WhenAll(finders.Select(f => f.FindAsync(ct)))).SelectMany(found => found)];
}
