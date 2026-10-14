using System.Net;
using System.Net.Sockets;
using Lantern.Networking;
using Lantern.Networking.Protocol;
using Lantern.Transfers;
using Xunit;

namespace Lantern.Tests;

public sealed class TransferTimeoutTests
{
    [Fact]
    public async Task ControlWriteDeadlineClosesAConnectionWhosePeerStopsReading()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient { SendBufferSize = 1024 };
        var accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var remote = await accept;
        remote.ReceiveBufferSize = 1024;
        using var connection = new Connection(client);
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            for (int index = 0; index < 64; index++)
                await TransferMessages.SendAsync(
                    connection,
                    new ErrorPayload("test", new string('x', 512 * 1024)),
                    overall.Token,
                    TimeSpan.FromMilliseconds(100)
                );
        });
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            connection.SendAsync(Message.CreateHello(new HelloPayload("test", 1))).AsTask()
        );
    }
}
