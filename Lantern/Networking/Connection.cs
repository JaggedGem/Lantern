using System.Net;
using System.Net.Sockets;

namespace Lantern.Networking;

public sealed class Connection : EventArgs, IDisposable
{
    private readonly TcpClient _client;

    internal Connection(TcpClient client) {
        _client = client;
    }

    public EndPoint? RemoteEndpoint => _client.Client.RemoteEndPoint;

    public NetworkStream Stream => _client.GetStream();

    public void Close() {
        _client.Close();
    }

    public void Dispose() {
        _client.Dispose();
    }
}
