using Trovolve.Domain.Entities;

namespace Trovolve.Domain.Repositories;

/// <summary>The uses of Trovolve's features noted on this device and not yet sent, oldest first.</summary>
public interface IUsageStore
{
    /// <summary>This installation's id: made at random the first time it is asked for, and the same ever after.</summary>
    Task<Guid> InstallAsync(CancellationToken ct);

    /// <summary>Notes a use, numbered after every use noted before it, forgetting the oldest past <paramref name="keepAtMost"/>.</summary>
    /// <param name="at">When it was used.</param>
    /// <param name="feature">Which feature.</param>
    /// <param name="status">How it ended.</param>
    /// <param name="session">The run of the program it was used in.</param>
    /// <param name="keepAtMost">The most uses kept; the oldest go first.</param>
    /// <param name="ct">Checked before anything is written.</param>
    /// <returns>The use as kept, with its number.</returns>
    Task<FeatureUse> AddAsync(DateTimeOffset at, string feature, int status, Guid session, int keepAtMost, CancellationToken ct);

    /// <summary>The oldest uses kept, up to <paramref name="max"/>, oldest first.</summary>
    Task<IReadOnlyList<FeatureUse>> OldestAsync(int max, CancellationToken ct);

    /// <summary>How many uses are kept.</summary>
    Task<int> CountAsync(CancellationToken ct);

    /// <summary>Forgets every use up to and including the one numbered <paramref name="sequence"/>, once they are sent.</summary>
    Task ForgetThroughAsync(long sequence, CancellationToken ct);
}
