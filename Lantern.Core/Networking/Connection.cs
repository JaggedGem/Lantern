using System.Net;
using System.Net.Sockets;
using Lantern.Networking.Protocol;
using ProtocolMessage = Lantern.Networking.Protocol.Message;

namespace Lantern.Networking;

public sealed class Connection : IDisposable, IAsyncDisposable
{
    private readonly TcpClient _client;
    private FramedMessageChannel _channel;
    private int _framingStarted;
    private readonly EndPoint? _remoteEndPoint;
    private int _isDisposed;

    internal Connection(TcpClient client, ProtocolSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _remoteEndPoint = client.Client.RemoteEndPoint;

        client.NoDelay = true;

        _channel = new FramedMessageChannel(
            client.GetStream(),
            serializer ?? new ProtocolSerializer()
        );
    }

    internal Stream TransportStream
    {
        get
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _framingStarted) != 0)
                throw new InvalidOperationException("TLS must precede framed traffic.");
            return _client.GetStream();
        }
    }

    internal void UseAuthenticatedStream(Stream stream)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _framingStarted) != 0)
            throw new InvalidOperationException("TLS must precede framed traffic.");
        // The SSL stream now owns the original network stream. No framed I/O has occurred yet.
        _channel = new FramedMessageChannel(stream, new ProtocolSerializer());
    }

    public EndPoint? RemoteEndPoint => _remoteEndPoint;

    public async ValueTask SendAsync(
        ProtocolMessage message,
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        Interlocked.Exchange(ref _framingStarted, 1);
        await _channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProtocolMessage> ReceiveAsync(
        CancellationToken cancellationToken = default
    )
    {
        ThrowIfDisposed();
        Interlocked.Exchange(ref _framingStarted, 1);
        return await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            _channel.Dispose();
        }
        finally
        {
            _client.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            throw new ObjectDisposedException(nameof(Connection));
        }
    }
}
