using System.Net;
using System.Net.Sockets;
using Lantern.Networking.Protocol;
using ProtocolMessage = Lantern.Networking.Protocol.Message;

namespace Lantern.Networking;

public sealed class Connection : IDisposable, IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly FramedMessageChannel _channel;
    private readonly EndPoint? _remoteEndPoint;
    private int _isDisposed;

    internal Connection(TcpClient client, ProtocolSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _remoteEndPoint = client.Client.RemoteEndPoint;

        client.NoDelay = true;

        _channel = new FramedMessageChannel(client.GetStream(), serializer ?? new ProtocolSerializer());
    }

    public EndPoint? RemoteEndPoint => _remoteEndPoint;

    public async ValueTask SendAsync(ProtocolMessage message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProtocolMessage> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _channel.Dispose();
        _client.Dispose();
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
