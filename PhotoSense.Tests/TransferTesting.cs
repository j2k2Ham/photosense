using System.Net;
using System.Net.Sockets;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Tests;

/// <summary>Pairings kept in memory only, under an id of the test's choosing.</summary>
internal sealed class MemoryPairs(string self) : IPairingStore
{
    private readonly Dictionary<string, PairedDevice> _paired = [];

    public IReadOnlyCollection<PairedDevice> Paired => _paired.Values;

    public Task<string> SelfIdAsync() => Task.FromResult(self);

    public Task<PairedDevice?> GetAsync(string id) => Task.FromResult(_paired.GetValueOrDefault(id));

    public Task SaveAsync(PairedDevice device)
    {
        _paired[device.Id] = device;
        return Task.CompletedTask;
    }
}

/// <summary>Two ends of one connection over this computer's own network.</summary>
internal static class Loopback
{
    public static async Task<(Stream Near, Stream Far)> PairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accepting = listener.AcceptTcpClientAsync();
            var near = await ConnectAsync(((IPEndPoint)listener.LocalEndpoint).Port, CancellationToken.None);
            return (near, (await accepting).GetStream());
        }
        finally { listener.Stop(); }
    }

    public static async Task<Stream> ConnectAsync(int port, CancellationToken ct)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        return client.GetStream();
    }
}

/// <summary>A connection whose other end says what the test wrote down, and keeps what is said to it.</summary>
internal sealed class ScriptedStream(byte[] answers) : Stream
{
    private readonly MemoryStream _answers = new(answers);

    public MemoryStream Written { get; } = new();
    public bool Disposed { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _answers.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
