using System.Net;
using System.Net.Sockets;

namespace Lantern.Networking;

public sealed class NetworkServer
{
    private readonly object _syncRoot = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellationSource;
    private bool _isRunning;

    public NetworkServer(int port) {
        if (port is < 1 or > IPEndPoint.MaxPort) {
            throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be between 1 and 65535.");
        }

        Port = port;
    }

    public int Port { get; }

    public bool IsRunning {
        get {
            lock (_syncRoot) {
                return _isRunning;
            }
        }
    }

    public event EventHandler<Connection>? ConnectionAccepted;

    public void Start() {
        lock (_syncRoot) {
            if (_isRunning) {
                throw new InvalidOperationException("The network server is already running.");
            }

            var listener = new TcpListener(IPAddress.Any, Port);
            var cancellationSource = new CancellationTokenSource();

            try {
                listener.Start();
            } catch {
                cancellationSource.Dispose();
                throw;
            }

            _listener = listener;
            _cancellationSource = cancellationSource;
            _isRunning = true;

            _ = AcceptConnectionsAsync(listener, cancellationSource);
        }
    }

    public void Stop() {
        TcpListener? listener;
        CancellationTokenSource? cancellationSource;

        lock (_syncRoot) {
            if (!_isRunning) {
                return;
            }

            _isRunning = false;
            listener = _listener;
            cancellationSource = _cancellationSource;
            _listener = null;
            _cancellationSource = null;
        }

        cancellationSource?.Cancel();
        listener?.Stop();
    }

    private async Task AcceptConnectionsAsync(
        TcpListener listener,
        CancellationTokenSource cancellationSource) {
        try {
            while (!cancellationSource.IsCancellationRequested) {
                TcpClient client;

                try {
                    client = await listener.AcceptTcpClientAsync(cancellationSource.Token);
                }
                catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested) {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationSource.IsCancellationRequested) {
                    break;
                }

                var connection = new Connection(client);
                ConnectionAccepted?.Invoke(this, connection);
            }
        }
        finally {
            lock (_syncRoot) {
                if (ReferenceEquals(_cancellationSource, cancellationSource)) {
                    _isRunning = false;
                    _listener = null;
                    _cancellationSource = null;
                }
            }

            listener.Stop();
            cancellationSource.Dispose();
        }
    }
}