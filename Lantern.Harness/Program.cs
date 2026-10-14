using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Lantern.Application;
using Lantern.Configuration;
using Lantern.Models;
using Lantern.Networking;
using Lantern.Networking.Protocol;
using Lantern.Transfers;

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
                "identity" => await IdentityAsync(args),
                "trust" => await TrustAsync(args, false),
                "untrust" => await TrustAsync(args, true),
                "settings" => await SettingsAsync(args),
                "history" => await InspectAsync(args, false),
                "diagnostics" => await InspectAsync(args, true),
                "benchmark" => await BenchmarkAsync(args),
                "receive" => await RunReceiverAsync(args),
                "send" => await RunSenderAsync(args),
                _ => PrintUsageAndFail(),
            };
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> RunServerAsync(int port)
    {
        await using var server = new NetworkServer(port);
        var acceptedConnectionSource = new TaskCompletionSource<Connection>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        server.ConnectionAccepted += (_, e) => acceptedConnectionSource.TrySetResult(e.Connection);

        await server.StartAsync();
        Console.WriteLine($"Server listening on {server.ListeningPort}");

        await using var connection = await acceptedConnectionSource.Task.WaitAsync(
            TimeSpan.FromMinutes(1)
        );
        var hello = await connection.ReceiveAsync();
        WriteMessage("Server received", hello);

        var response = Message.CreateHello(
            new HelloPayload("Lantern.Harness.Server", ProtocolVersion)
        );
        await connection.SendAsync(response);
        WriteMessage("Server sent", response);

        await server.StopAsync();
        return 0;
    }

    private static async Task<int> RunClientAsync(IPAddress address, int port)
    {
        var client = new NetworkClient();
        await using var connection = await client.ConnectAsync(address, port);

        var hello = Message.CreateHello(
            new HelloPayload("Lantern.Harness.Client", ProtocolVersion)
        );
        await connection.SendAsync(hello);
        WriteMessage("Client sent", hello);

        var response = await connection.ReceiveAsync();
        WriteMessage("Client received", response);
        return 0;
    }

    private static async Task<int> IdentityAsync(string[] args)
    {
        RequireCount(args, 2);
        await using var application = LanternApplication.Open(args[1]);
        Console.WriteLine($"Device ID: {application.DeviceId:D}");
        Console.WriteLine($"SHA-256 fingerprint: {application.Fingerprint}");
        return 0;
    }

    private static async Task<int> TrustAsync(string[] args, bool remove)
    {
        RequireCount(args, remove ? 3 : 4);
        await using var application = LanternApplication.Open(args[1]);
        var peer = Guid.Parse(args[2]);
        if (remove)
            application.Trust.RemovePeer(peer);
        else
            application.Trust.TrustPeer(peer, args[3]);
        Console.WriteLine(
            remove
                ? "Peer removed."
                : "Verified fingerprint saved. Pair this installation on the other peer too."
        );
        return 0;
    }

    private static Task<int> SettingsAsync(string[] args)
    {
        if (args.Length is not (2 or 4))
            throw new ArgumentException(
                "settings requires a data directory and optionally a device name and download directory."
            );
        var store = new SettingsStore(Path.Combine(Path.GetFullPath(args[1]), "settings.json"));
        var settings = store.Load();
        if (args.Length == 4)
            store.Save(
                settings = settings with
                {
                    DeviceName = args[2],
                    DownloadDirectory = Path.GetFullPath(args[3]),
                }
            );
        Console.WriteLine(
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })
        );
        return Task.FromResult(0);
    }

    private static async Task<int> InspectAsync(string[] args, bool diagnostics)
    {
        RequireCount(args, 2);
        await using var application = LanternApplication.Open(args[1]);
        Console.WriteLine(
            diagnostics
                ? JsonSerializer.Serialize(application.GetDiagnostics())
                : JsonSerializer.Serialize(application.History.GetEntries())
        );
        return 0;
    }

    private static async Task<int> RunReceiverAsync(string[] args)
    {
        if (args.Length is not (3 or 4))
            throw new ArgumentException(
                "receive requires data directory, destination folder and optional port."
            );
        var settings = new SettingsStore(
            Path.Combine(Path.GetFullPath(args[1]), "settings.json")
        ).Load() with
        {
            DownloadDirectory = Path.GetFullPath(args[2]),
        };
        if (args.Length == 4)
            settings = settings with { TcpPort = ParsePort(args, 3) };
        Directory.CreateDirectory(settings.DownloadDirectory);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            await using var application = new LanternApplication(
                settings.DeviceName,
                identityPath: Path.Combine(Path.GetFullPath(args[1]), "device-id.json"),
                settings: settings
            );
            var requests = Channel.CreateBounded<IncomingTransferRequestEventArgs>(
                settings.MaximumConcurrentTransfers
            );
            application.Transfers.IncomingTransferRequested += (_, request) =>
            {
                if (!requests.Writer.TryWrite(request))
                    request.Reject();
            };
            application.Transfers.TransferChanged += (_, e) =>
            {
                if (e.Transfer.FinishedAt.HasValue)
                    Console.WriteLine($"{e.Transfer.Id}: {e.Transfer.Status}");
            };
            await application.StartAsync(cancellation.Token);
            Console.WriteLine(
                $"Receiving as {application.DeviceId:D} on TCP {application.Transfers.ListeningPort}. Ctrl+C stops the service."
            );
            try
            {
                await foreach (var request in requests.Reader.ReadAllAsync(cancellation.Token))
                {
                    Console.WriteLine(
                        $"Peer {request.Transfer.SourceDeviceId:D} offers {request.Transfer.Files.Count} files ({request.Transfer.TotalBytes} bytes):"
                    );
                    foreach (var file in request.Transfer.Files)
                        Console.WriteLine($"  {file.RelativePath}");
                    Console.Write("Accept? [y/N] ");
                    if (
                        string.Equals(
                            await Console.In.ReadLineAsync(cancellation.Token),
                            "y",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        if (!request.Accept(settings.DownloadDirectory))
                            Console.WriteLine("The request has already expired.");
                    }
                    else
                        request.Reject();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static async Task<int> RunSenderAsync(string[] args)
    {
        if (args.Length < 6)
            throw new ArgumentException(
                "send requires data directory, receiver ID, IP address, port and files."
            );
        var directory = Path.GetFullPath(args[1]);
        var settings = new SettingsStore(Path.Combine(directory, "settings.json")).Load() with
        {
            TcpPort = 0,
            DiscoveryEnabled = false,
        };
        await using var application = new LanternApplication(
            settings.DeviceName,
            identityPath: Path.Combine(directory, "device-id.json"),
            settings: settings
        );
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            await application.StartAsync(cancellation.Token);
            var peer = new Device(
                Guid.Parse(args[2]),
                "Selected receiver",
                ParseAddress(args, 3),
                ParsePort(args, 4)
            );
            var files = await TransferSelection.FromPathsAsync(
                args.Skip(5),
                settings.ToTransferOptions(),
                cancellation.Token
            );
            var result = await application.Transfers.SendAsync(peer, files, cancellation.Token);
            Console.WriteLine($"{result.Id}: {result.Status}");
            if (result.Error != null)
                Console.Error.WriteLine(result.Error.Description);
            return result.Status == TransferStatus.Completed ? 0
                : result.Status == TransferStatus.Cancelled ? 130
                : 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static async Task<int> BenchmarkAsync(string[] args)
    {
        var size = args.Length == 2 ? int.Parse(args[1]) : 32;
        if (args.Length > 2 || size is < 1 or > 1024)
            throw new ArgumentException("benchmark accepts 1–1024 MiB.");
        var root = Path.Combine(
            Path.GetTempPath(),
            "lantern-benchmark-" + Guid.NewGuid().ToString("N")
        );
        var destination = Path.Combine(root, "received");
        Directory.CreateDirectory(destination);
        try
        {
            var settings = new LanternSettings
            {
                TcpPort = 0,
                DiscoveryEnabled = false,
                DownloadDirectory = destination,
            };
            await using var sender = new LanternApplication(
                "sender",
                identityPath: Path.Combine(root, "sender", "device-id.json"),
                settings: settings
            );
            await using var receiver = new LanternApplication(
                "receiver",
                identityPath: Path.Combine(root, "receiver", "device-id.json"),
                settings: settings
            );
            sender.Trust.TrustPeer(receiver.DeviceId, receiver.Fingerprint);
            receiver.Trust.TrustPeer(sender.DeviceId, sender.Fingerprint);
            receiver.Transfers.IncomingTransferRequested += (_, request) =>
                request.Accept(destination);
            await sender.StartAsync();
            await receiver.StartAsync();
            var source = Path.Combine(root, "source.bin");
            await using (var stream = File.Create(source))
                stream.SetLength(size * 1024L * 1024);
            var watch = Stopwatch.StartNew();
            var result = await sender.Transfers.SendAsync(
                new Device(
                    receiver.DeviceId,
                    "receiver",
                    IPAddress.Loopback,
                    receiver.Transfers.ListeningPort!.Value
                ),
                [TransferSourceFile.FromPath(source)]
            );
            watch.Stop();
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        result.Status,
                        MiB = size,
                        seconds = watch.Elapsed.TotalSeconds,
                        MiBPerSecond = size / watch.Elapsed.TotalSeconds,
                        peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                    }
                )
            );
            return result.Status == TransferStatus.Completed ? 0 : 1;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void RequireCount(string[] args, int count)
    {
        if (args.Length != count)
            throw new ArgumentException("Invalid argument count; run without arguments for usage.");
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
        if (
            args.Length <= index
            || !int.TryParse(args[index], out var port)
            || port is < 1 or > IPEndPoint.MaxPort
        )
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
        Console.WriteLine("  Lantern.Harness identity <data-directory>");
        Console.WriteLine(
            "  Lantern.Harness trust <data-directory> <peer-id> <verified-sha256-fingerprint>"
        );
        Console.WriteLine("  Lantern.Harness untrust <data-directory> <peer-id>");
        Console.WriteLine("  Lantern.Harness receive <data-directory> <destination-folder> [port]");
        Console.WriteLine(
            "  Lantern.Harness send <data-directory> <receiver-id> <ip-address> <port> <files...>"
        );
        Console.WriteLine(
            "  Lantern.Harness settings <data-directory> [device-name download-directory]"
        );
        Console.WriteLine("  Lantern.Harness history|diagnostics <data-directory>");
        Console.WriteLine("  Lantern.Harness benchmark [MiB]");
    }

    private static void WriteMessage(string prefix, Message message)
    {
        switch (message.Payload)
        {
            case HelloPayload hello:
                Console.WriteLine(
                    $"{prefix}: {message.Type} {message.Id} {hello.ApplicationName} v{hello.ProtocolVersion}"
                );
                break;
            case ErrorPayload error:
                Console.WriteLine(
                    $"{prefix}: {message.Type} {message.Id} {error.Code} - {error.Description}"
                );
                break;
        }
    }
}
