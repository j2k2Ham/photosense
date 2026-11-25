using Microsoft.Extensions.Hosting;
using Trovolve.Application.Usage;

namespace Trovolve.Server;

/// <summary>While the program runs, sends the uses of Trovolve's features to the usage server whenever they are due; nothing while usage metrics are off.</summary>
/// <param name="usage">Notes the uses, and sends them.</param>
internal sealed class SendUsage(UsageMetrics usage) : BackgroundService
{
    /// <summary>Sends until the program stops.</summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => usage.SendWhileRunningAsync(stoppingToken);
}
