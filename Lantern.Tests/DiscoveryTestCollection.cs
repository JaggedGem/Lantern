using System.Net;
using System.Net.Sockets;
using Lantern.Networking.Discovery;
using Xunit;

namespace Lantern.Tests;

// Keep discovery socket lifetimes separate, including tests that restart a service.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiscoveryTestCollection
{
    public const string Name = "LAN discovery with shared UDP port";

    public static DiscoveryOptions AvailablePortOptions()
    {
        // The production port may belong to a Windows excluded UDP range.
        // Let the OS choose an available port for isolated discovery tests.
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        return new DiscoveryOptions { UdpPort = ((IPEndPoint)socket.Client.LocalEndPoint!).Port };
    }
}
