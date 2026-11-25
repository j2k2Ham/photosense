using Microsoft.Extensions.Hosting;
using Trovolve.Application.Transfer;

namespace Trovolve.Server;

/// <summary>While the program runs, waits for the phones paired with this computer to back up, and says so on the network.</summary>
internal sealed class OpenToPhones : IHostedService
{
    private readonly ComputerPairing _door;

    /// <summary>Opens <paramref name="door"/> as the program starts, and closes it as it stops.</summary>
    public OpenToPhones(ComputerPairing door) => _door = door;

    /// <summary>Starts waiting for phones.</summary>
    public Task StartAsync(CancellationToken cancellationToken) => _door.ListenAsync(cancellationToken);

    /// <summary>Stops waiting for phones; a backup arriving is cut short, and the phone carries on next time.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _door.StopListening();
        return Task.CompletedTask;
    }
}
