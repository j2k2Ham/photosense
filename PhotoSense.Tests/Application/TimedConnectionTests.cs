using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Application.Transfer;

namespace PhotoSense.Tests.Application;

/// <summary>A connection between a phone and a computer that gives up on a silence, and a device that says it is still there.</summary>
public class TimedConnectionTests
{
    [Test]
    public async Task A_Connection_Is_Read_And_Written_Only_Asynchronously_And_Has_No_Length_Or_Position(CancellationToken ct)
    {
        using var line = new MemoryStream();
        await using var timed = new TimedConnection(line, TimeProvider.System);
        await Assert.That((timed.CanRead, timed.CanWrite, timed.CanSeek)).IsEqualTo((true, true, false));
        foreach (var call in new Action[] { () => timed.ReadExactly(new byte[1]), () => timed.Write([1], 0, 1), timed.Flush })
            await Assert.That(Assert.Throws<NotSupportedException>(call)!.Message).IsEqualTo("A connection to another device is only read and written asynchronously.");
        foreach (var call in new Action[] { () => _ = timed.Length, () => _ = timed.Position, () => timed.Position = 1, () => timed.Seek(0, SeekOrigin.Begin), () => timed.SetLength(1) })
            await Assert.That(Assert.Throws<NotSupportedException>(call)!.Message).IsEqualTo("A connection to another device has no length or position.");

        // A wait on the person at the other end is made on the connection itself, which has no limit.
        await Assert.That(TimedConnection.Unlimited(timed)).IsSameReferenceAs(line);
        await Assert.That(TimedConnection.Unlimited(line)).IsSameReferenceAs(line);
    }

    [Test]
    public async Task A_Silence_Is_Given_Up_On_But_A_Wait_Stopped_By_The_Caller_Is_Said_To_Be_Stopped(CancellationToken ct)
    {
        var (near, far) = await Loopback.PairAsync(ct);
        await using (near)
        await using (far)
        {
            var timed = new TimedConnection(near, HurriedClock.Minute);
            var silence = await Assert.ThrowsAsync<TransferException>(async () => await timed.ReadExactlyAsync(new byte[1], ct));
            await Assert.That(silence!.Message).IsEqualTo("The other device stopped answering. Try again.");

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var reading = timed.ReadExactlyAsync(new byte[1], stop.Token).AsTask();
            await stop.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => reading);
        }
    }

    [Test]
    public async Task A_Device_Busy_With_Work_Of_Its_Own_Says_Now_And_Then_That_It_Is_Still_There(CancellationToken ct)
    {
        var line = new MemoryStream();
        var (one, other) = (RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        using var busy = new SecureChannel(line, one, other);
        using var waiting = new SecureChannel(line, other, one);

        // Work that takes a minute and a half on the clock: it is said twice meanwhile that the device is still there.
        var answer = await busy.MeanwhileAsync(async stop =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stop);
            return 42;
        }, HurriedClock.Minute, ct);
        await busy.SendAsync(new JsonObject { ["t"] = "file" }, ct);
        var said = line.Length;

        line.Position = 0;
        await Assert.That((answer, Wire.Text(await waiting.ReceiveAsync(ct, "file"), "t"), line.Position == said)).IsEqualTo((42, "file", true));
        // Work done at once says nothing meanwhile.
        line.SetLength(0);
        await Assert.That(await busy.MeanwhileAsync(_ => Task.FromResult(7), HurriedClock.Minute, ct)).IsEqualTo(7);
        await Assert.That(line.Length).IsEqualTo(0);
    }
}
