namespace PhotoSense.Application.Transfer;

/// <summary>
/// A connection between a phone and a computer that gives up once the other device has said nothing, and taken
/// nothing, for <see cref="Silence"/>: a phone that went to sleep or off the network is not waited for without end.
/// A device busy with work of its own says so now and then (<see cref="SecureChannel.MeanwhileAsync"/>), and a wait
/// on the person at the other end, comparing the numbers, has no limit (<see cref="Unlimited"/>).
/// </summary>
public sealed class TimedConnection : Stream
{
    /// <summary>How long one device waits on the other before it gives up.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(60);

    private const string NoAnswer = "The other device stopped answering. Try again.";
    private readonly Stream _inner;
    private readonly TimeProvider _clock;

    /// <summary>The connection given, timed by the clock given.</summary>
    /// <param name="inner">The connection itself, which stays its maker's to close.</param>
    /// <param name="clock">Times the silences.</param>
    public TimedConnection(Stream inner, TimeProvider clock)
    {
        _inner = inner;
        _clock = clock;
    }

    /// <summary>The connection with no limit on waiting, for a wait on the person at the other end.</summary>
    public static Stream Unlimited(Stream connection) => connection is TimedConnection timed ? timed._inner : connection;

    /// <summary>Does one step with the other device, given up once it has gone <see cref="Silence"/> without an answer.</summary>
    /// <param name="step">The step, which stops when the token it is given does.</param>
    /// <param name="clock">Times the silence.</param>
    /// <param name="ct">Stops the step.</param>
    /// <exception cref="TransferException">The other device did not answer in time.</exception>
    public static async Task<T> WithinAsync<T>(Func<CancellationToken, Task<T>> step, TimeProvider clock, CancellationToken ct)
    {
        using var silence = new CancellationTokenSource(Silence, clock);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(ct, silence.Token);
        try { return await step(either.Token); }
        // Not stopped by the caller, so stopped by the silence.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TransferException(NoAnswer); }
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => await WithinAsync(async ct => await _inner.ReadAsync(buffer, ct), _clock, cancellationToken);

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => await WithinAsync(async ct =>
        {
            await _inner.WriteAsync(buffer, ct);
            return true;
        }, _clock, cancellationToken);

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    // Only what is waited for asynchronously can be given up on, so nothing is read or written any other way.
    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw Asynchronous;

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw Asynchronous;

    /// <inheritdoc />
    public override void Flush() => throw Asynchronous;

    /// <inheritdoc />
    public override long Length => throw Unseekable;

    /// <inheritdoc />
    public override long Position
    {
        get => throw Unseekable;
        set => throw Unseekable;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw Unseekable;

    /// <inheritdoc />
    public override void SetLength(long value) => throw Unseekable;

    private static NotSupportedException Asynchronous => new("A connection to another device is only read and written asynchronously.");

    private static NotSupportedException Unseekable => new("A connection to another device has no length or position.");
}
