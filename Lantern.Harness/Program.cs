using System.Net;
using Lantern.Networking;
using Lantern.Networking.Protocol;

namespace Lantern.Harness;

internal static class Program
{
    private const int ProtocolVersion = 1;
    private const string ApplicationName = "Lantern.Harness";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            return args[0].ToLowerInvariant() switch
            {
                "server" => await RunServerAsync(ParsePort(args, 1)),
                "client" => await RunClientAsync(ParseAddress(args, 2), ParsePort(args, 1)),
                _ => PrintUsageAndFail()
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> RunServerAsync(int port)
    {
        await using var server = new NetworkServer(port);
        var acceptedConnectionSource = new TaskCompletionSource<Connection>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ConnectionAccepted += (_, e) => acceptedConnectionSource.TrySetResult(e.Connection);

        await server.StartAsync();
        Console.WriteLine($"Server listening on {server.ListeningPort}");

        await using var connection = await acceptedConnectionSource.Task.WaitAsync(TimeSpan.FromMinutes(1));
        var hello = await connection.ReceiveAsync();
        WriteMessage("Server received", hello);

        var response = Message.CreateHello(new HelloPayload("Lantern.Harness.Server", ProtocolVersion));
        await connection.SendAsync(response);
        WriteMessage("Server sent", response);

        await server.StopAsync();
        return 0;
    }

    private static async Task<int> RunClientAsync(IPAddress address, int port)
    {
        var client = new NetworkClient();
        await using var connection = await client.ConnectAsync(address, port);

        var hello = Message.CreateHello(new HelloPayload("Lantern.Harness.Client", ProtocolVersion));
        await connection.SendAsync(hello);
        WriteMessage("Client sent", hello);

        var response = await connection.ReceiveAsync();
        WriteMessage("Client received", response);
        return 0;
    }

    private static IPAddress ParseAddress(string[] args, int index)
    {
        if (args.Length <= index)
        {
            return IPAddress.Loopback;
        }

        if (!IPAddress.TryParse(args[index], out var address))
        {
            throw new ArgumentException($"'{args[index]}' is not a valid IP address.");
        }

        return address;
    }

    private static int ParsePort(string[] args, int index)
    {
        if (args.Length <= index || !int.TryParse(args[index], out var port) || port is < 1 or > IPEndPoint.MaxPort)
        {
            throw new ArgumentException("A TCP port between 1 and 65535 must be supplied.");
        }

        return port;
    }

    private static int PrintUsageAndFail()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Lantern.Harness server <port>");
        Console.WriteLine("  Lantern.Harness client <port> [ip-address]");
    }

    private static void WriteMessage(string prefix, Message message)
    {
        switch (message.Payload)
        {
            case HelloPayload hello:
                Console.WriteLine($"{prefix}: {message.Type} {message.Id} {hello.ApplicationName} v{hello.ProtocolVersion}");
                break;
            case ErrorPayload error:
                Console.WriteLine($"{prefix}: {message.Type} {message.Id} {error.Code} - {error.Description}");
                break;
        }
    }
}

