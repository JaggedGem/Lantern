using System.Net;
using System.Net.Sockets;
using Lantern.Networking.Protocol;

namespace Lantern.Networking;

public sealed class NetworkClient
{
    public async Task<Connection> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (port is < 1 or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be between 1 and 65535.");
        }

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            client.NoDelay = true;
            return new Connection(client, new ProtocolSerializer());
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}

