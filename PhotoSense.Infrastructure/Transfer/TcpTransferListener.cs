using System.Net;
using System.Net.Sockets;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Transfer;

/// <summary>
/// A phone waiting for computers to connect, on every network it is on. Over a cable a computer can only reach a port
/// it knows, so the phone waits on <see cref="Port"/>; should another app have it, on any free port, which the network
/// is told of all the same.
/// </summary>
/// <param name="port">The port to wait on when it is free.</param>
internal sealed class TcpTransferListener(int port = TcpTransferListener.Port) : ITransferListener
{
    /// <summary>The port a phone waits on, where a computer looks for it through a cable.</summary>
    public const int Port = 47615;

    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private readonly List<TcpClient> _open = [];

    /// <inheritdoc />
    public int Start(Func<Stream, CancellationToken, Task> serve)
    {
        Stop();
        var stop = _stop = new CancellationTokenSource();
        var listener = Listen(port) ?? Listen(0)!;
        _listener = listener;
        _ = AcceptAsync(listener, serve, stop.Token);
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <inheritdoc />
    public void Stop()
    {
        _stop?.Cancel();
        _listener?.Stop();
        _listener = null;
        lock (_open)
        {
            foreach (var client in _open) client.Dispose();
            _open.Clear();
        }
    }

    private static TcpListener? Listen(int at)
    {
        var listener = new TcpListener(IPAddress.Any, at);
        try
        {
            listener.Start();
            return listener;
        }
        catch (SocketException) when (at != 0)
        {
            listener.Dispose();
            return null;
        }
    }

    private async Task AcceptAsync(TcpListener listener, Func<Stream, CancellationToken, Task> serve, CancellationToken ct)
    {
        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            // Stopped: there is nothing more to wait for.
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            lock (_open) _open.Add(client);
            _ = Task.Run(() => ServeAsync(client, serve, ct), CancellationToken.None);
        }
    }

    /// <summary>Serves one connection, and lets go of it however it ends.</summary>
    private async Task ServeAsync(TcpClient client, Func<Stream, CancellationToken, Task> serve, CancellationToken ct)
    {
        try { _ = await ServedAsync(client, serve, ct); }
        finally
        {
            lock (_open) _open.Remove(client);
            client.Dispose();
        }
    }

    /// <summary>
    /// Whether a connection was served to its end. One that broke off is that connection's own: serve says why, and
    /// the next is waited for.
    /// </summary>
    private static async Task<bool> ServedAsync(TcpClient client, Func<Stream, CancellationToken, Task> serve, CancellationToken ct)
    {
        try
        {
            await using var stream = client.GetStream();
            await serve(stream, ct);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        return true;
    }
}
