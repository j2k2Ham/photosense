using System.Net;
using System.Net.Sockets;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Tests;

/// <summary>Pairings kept in memory only, under an id of the test's choosing.</summary>
internal sealed class MemoryPairs(string self) : IPairingStore
{
    private readonly Dictionary<string, PairedDevice> _paired = [];

    /// <summary>The pairings kept.</summary>
    public IReadOnlyCollection<PairedDevice> Paired => _paired.Values;

    /// <inheritdoc />
    public Task<string> SelfIdAsync(CancellationToken ct) => Task.FromResult(self);

    /// <inheritdoc />
    public Task<PairedDevice?> GetAsync(string id, CancellationToken ct) => Task.FromResult(_paired.GetValueOrDefault(id));

    /// <inheritdoc />
    public Task SaveAsync(PairedDevice device, CancellationToken ct)
    {
        _paired[device.Id] = device;
        return Task.CompletedTask;
    }
}

/// <summary>Two ends of one connection over this computer's own network.</summary>
internal static class Loopback
{
    /// <summary>Two ends of one connection over this computer's own network.</summary>
    public static async Task<(Stream Near, Stream Far)> PairAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accepting = listener.AcceptTcpClientAsync(ct);
            var near = await ConnectAsync(((IPEndPoint)listener.LocalEndpoint).Port, ct);
            return (near, (await accepting).GetStream());
        }
        finally { listener.Stop(); }
    }

    /// <summary>A connection to a port of this computer.</summary>
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

    /// <summary>What was said to it.</summary>
    public MemoryStream Written { get; } = new();
    /// <summary>It was let go of.</summary>
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

/// <summary>
/// A clock whose timers go off sooner by the factor given, so that a minute's silence between a phone and a computer
/// passes in a moment. It tells the time as this computer does.
/// </summary>
internal sealed class HurriedClock(double factor) : TimeProvider
{
    /// <summary>A minute in two seconds.</summary>
    public static readonly HurriedClock Minute = new(30);

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new Hurried(TimeProvider.System.CreateTimer(callback, state, Sooner(dueTime), Sooner(period)), this);

    private TimeSpan Sooner(TimeSpan span) => span == Timeout.InfiniteTimeSpan ? span : span / factor;

    /// <summary>A timer set again goes off sooner by the same factor.</summary>
    private sealed class Hurried(ITimer timer, HurriedClock clock) : ITimer
    {
        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(clock.Sooner(dueTime), clock.Sooner(period));
        /// <inheritdoc />
        public void Dispose() => timer.Dispose();
        /// <inheritdoc />
        public ValueTask DisposeAsync() => timer.DisposeAsync();
    }
}
