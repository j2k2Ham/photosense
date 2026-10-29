using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>Tells no network anything; says what it was asked to tell.</summary>
internal sealed class FakeAdvertiser : ITransferAdvertiser
{
    /// <summary>What it is telling now: the phone's id, name and port; none while stopped.</summary>
    public (string Id, string Name, int Port)? Telling { get; private set; }

    public void Start(string id, string name, int port) => Telling = (id, name, port);

    public void Stop() => Telling = null;
}
