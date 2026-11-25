using Trovolve.Domain.Entities;

namespace Trovolve.Domain.Services;

/// <summary>Sends the uses of Trovolve's features to the usage server.</summary>
public interface IUsageSender
{
    /// <summary>Sends <paramref name="report"/> to <paramref name="to"/>, returning once the server has taken it.</summary>
    /// <exception cref="IOException">The server could not be reached, or did not take it; nothing is to be forgotten then.</exception>
    Task SendAsync(Uri to, UsageReport report, CancellationToken ct);
}
