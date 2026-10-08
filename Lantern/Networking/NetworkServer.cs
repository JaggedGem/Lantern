using System.Net;
using System.Net.Sockets;
using Lantern.Networking.Protocol;

namespace Lantern.Networking;

/// <summary>Accepted connections belong to the subscriber after a successful event handoff.</summary>
public sealed class NetworkServer : IDisposable, IAsyncDisposable
{
    private readonly object _syncRoot = new();
    private readonly ProtocolSerializer _serializer = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _shutdown;
    private Task? _acceptLoop;
    private Task? _stopTask;
    private int? _listeningPort;
    private bool _disposed;

    public NetworkServer(int port)
    {
        if (port is < 0 or > IPEndPoint.MaxPort) throw new ArgumentOutOfRangeException(nameof(port));
        Port = port;
    }

    public int Port { get; }
    public int? ListeningPort { get { lock (_syncRoot) return _listeningPort; } }
    public bool IsRunning { get { lock (_syncRoot) return _listener != null && _stopTask == null && _acceptLoop?.IsCompleted == false; } }
    /// <summary>Allows the owner to observe unexpected listener/subscriber failures.</summary>
    public Task Completion { get { lock (_syncRoot) return _acceptLoop ?? Task.CompletedTask; } }
    public event EventHandler<ConnectionAcceptedEventArgs>? ConnectionAccepted;

    /// <summary>The token cancels startup, not the server lifetime. StopAsync drains that lifetime.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listener != null) throw new InvalidOperationException("The server is running or stopping; await StopAsync before restarting.");
            var listener = new TcpListener(IPAddress.Any, Port);
            try { listener.Start(); }
            catch { listener.Dispose(); throw; }
            _listener = listener;
            _listeningPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            _shutdown = new CancellationTokenSource();
            _stopTask = null;
            _acceptLoop = AcceptAsync(listener, _shutdown.Token);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_syncRoot)
        {
            if (_listener == null) return Task.CompletedTask;
            _listeningPort = null;
            return _stopTask ??= StopCoreAsync(_listener, _shutdown!, _acceptLoop!);
        }
    }

    private async Task StopCoreAsync(TcpListener listener, CancellationTokenSource shutdown, Task loop)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            shutdown.Cancel();
            listener.Stop();
            await loop.ConfigureAwait(false);
        }
        finally
        {
            listener.Dispose();
            shutdown.Dispose();
            lock (_syncRoot)
            {
                _listener = null;
                _shutdown = null;
                _acceptLoop = null;
                _stopTask = null;
            }
        }
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken token)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding); // Never run application callbacks under the lifecycle lock.
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                Connection? connection = null;
                try
                {
                    connection = new Connection(client, _serializer);
                    var handler = ConnectionAccepted;
                    if (handler == null) connection.Dispose();
                    else handler(this, new ConnectionAcceptedEventArgs(connection));
                }
                catch
                {
                    if (connection != null) connection.Dispose();
                    else client.Dispose();
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        finally { listener.Stop(); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_syncRoot) _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
