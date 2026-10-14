using System.Net;
using System.Net.Sockets;
using Lantern.Networking.Protocol;

namespace Lantern.Networking;

public sealed class NetworkServer : IDisposable, IAsyncDisposable
{
    private readonly object _syncRoot = new();
    private readonly int _configuredPort;
    private readonly ProtocolSerializer _protocolSerializer = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _shutdownSource;
    private Task? _acceptLoopTask;
    private bool _isRunning;

    public NetworkServer(int port)
    {
        if (port is < 0 or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be between 0 and 65535.");
        }

        _configuredPort = port;
    }

    public int Port => _configuredPort;

    public int? ListeningPort
    {
        get
        {
            lock (_syncRoot)
            {
                return _listener is null ? null : ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _isRunning;
            }
        }
    }

    public event EventHandler<ConnectionAcceptedEventArgs>? ConnectionAccepted;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("The network server is already running.");
            }

            var listener = new TcpListener(IPAddress.Any, _configuredPort);
            var shutdownSource = new CancellationTokenSource();

            try
            {
                listener.Start();
            }
            catch
            {
                shutdownSource.Dispose();
                listener.Dispose();
                throw;
            }

            _listener = listener;
            _shutdownSource = shutdownSource;
            _isRunning = true;
            _acceptLoopTask = AcceptConnectionsAsync(listener, shutdownSource);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? acceptLoopTask;
        CancellationTokenSource? shutdownSource;
        TcpListener? listener;

        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            acceptLoopTask = _acceptLoopTask;
            shutdownSource = _shutdownSource;
            listener = _listener;
            _acceptLoopTask = null;
            _shutdownSource = null;
            _listener = null;
        }

        try
        {
            shutdownSource?.Cancel();
            listener?.Stop();

            if (acceptLoopTask is not null)
            {
                await acceptLoopTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdownSource?.IsCancellationRequested == true)
        {
        }
        catch (ObjectDisposedException) when (shutdownSource?.IsCancellationRequested == true)
        {
        }
        finally
        {
            shutdownSource?.Dispose();
            listener?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, CancellationTokenSource shutdownSource)
    {
        var cancellationToken = shutdownSource.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                client.NoDelay = true;
                var connection = new Connection(client, _protocolSerializer);
                ConnectionAccepted?.Invoke(this, new ConnectionAcceptedEventArgs(connection));
            }
        }
        finally
        {
            lock (_syncRoot)
            {
                if (ReferenceEquals(_listener, listener))
                {
                    _isRunning = false;
                    _listener = null;
                    _shutdownSource = null;
                    _acceptLoopTask = null;
                }
            }

            listener.Stop();
            shutdownSource.Dispose();
        }
    }
}