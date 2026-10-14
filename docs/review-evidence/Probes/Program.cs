using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lantern.Models;
using Lantern.Networking;
using Lantern.Networking.Protocol;
using Lantern.Networking.Discovery;

var results = new List<object>();
void Record(string name, bool reproduced, object? evidence) => results.Add(new { name, reproduced, evidence });
var packetMethod = typeof(DeviceDiscovery).GetMethod("ProcessDiscoveryPacket", BindingFlags.Instance | BindingFlags.NonPublic)!;
void Process(DeviceDiscovery discovery, string json) => packetMethod.Invoke(discovery, [Encoding.UTF8.GetBytes(json), new IPEndPoint(IPAddress.Parse("192.168.1.20"), 12345)]);
string Packet(Guid id, int type = 1, string name = "Peer") => JsonSerializer.Serialize(new { v = 1, t = type, id, n = name, p = 5000 });
DeviceDiscovery NewDiscovery() => new(new LocalDevice(Guid.NewGuid(), "Local", 5001));

using (var discovery = NewDiscovery()) {
    Process(discovery, Packet(Guid.NewGuid(), 999));
    Record("D03_unknown_type_accepted", discovery.GetDiscoveredDevices().Count == 1, discovery.GetDiscoveredDevices().Count);
}
using (var discovery = NewDiscovery()) {
    Process(discovery, Packet(Guid.Empty));
    Record("D03_empty_id_accepted", discovery.GetDiscoveredDevices().Single().Id == Guid.Empty, discovery.GetDiscoveredDevices().Single().Id);
}
using (var discovery = NewDiscovery()) {
    Process(discovery, JsonSerializer.Serialize(new { n = "Peer", p = 5000 }));
    Record("D03_missing_version_type_id_accepted", discovery.GetDiscoveredDevices().Count == 1, discovery.GetDiscoveredDevices().Count);
}
using (var discovery = NewDiscovery()) {
    Process(discovery, Packet(Guid.NewGuid(), name: new string('a', 3500)));
    Record("D03_3500_character_name_accepted", discovery.GetDiscoveredDevices().Count == 1, discovery.GetDiscoveredDevices().Single().Name.Length);
}
using (var discovery = NewDiscovery()) {
    Process(discovery, Packet(Guid.NewGuid()));
    var snapshot = discovery.GetDiscoveredDevices();
    snapshot[0].UpdatePort(6000);
    Record("D02_external_mutation_changes_internal_state", discovery.GetDiscoveredDevices()[0].Port == 6000, discovery.GetDiscoveredDevices()[0].Port);
}
using (var discovery = NewDiscovery()) {
    bool handlerCompleted = false;
    discovery.DeviceDiscovered += (_, _) => { _ = discovery.GetDiscoveredDevices(); handlerCompleted = true; };
    Process(discovery, Packet(Guid.NewGuid()));
    Record("D02_reentrant_snapshot_handler_aborted", !handlerCompleted && discovery.GetDiscoveredDevices().Count == 1, new { handlerCompleted, count = discovery.GetDiscoveredDevices().Count });
}
using (var discovery = NewDiscovery()) {
    var id = Guid.NewGuid();
    int discoveries = 0;
    discovery.DeviceDiscovered += (_, _) => discoveries++;
    Process(discovery, Packet(id));
    var peer = discovery.GetDiscoveredDevices().Single();
    var lastSeen = DateTimeOffset.UtcNow.AddSeconds(-60);
    typeof(Device).GetProperty("LastSeen")!.SetValue(peer, lastSeen);
    typeof(DeviceDiscovery).GetMethod("CheckAndRemoveExpiredDevices", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(discovery, null);
    var afterExpiry = discovery.GetDiscoveredDevices().Count;
    var offlineLastSeenWasChanged = peer.LastSeen > lastSeen;
    Process(discovery, Packet(id));
    Record("D01_offline_removed_and_rediscovered", afterExpiry == 0 && discoveries == 2, new { afterExpiry, discoveries });
    Record("D01_offline_status_refreshes_last_seen", offlineLastSeenWasChanged, new { lastSeen, changed = peer.LastSeen });
}
using (var discovery = NewDiscovery()) {
    var id = Guid.NewGuid();
    int events = 0;
    discovery.DeviceDiscovered += (_, _) => events++;
    discovery.DeviceStatusChanged += (_, _) => events++;
    Process(discovery, Packet(id));
    Process(discovery, JsonSerializer.Serialize(new { v = 1, t = 1, id, n = "Renamed", p = 6000 }));
    Record("D06_metadata_change_has_no_notification", events == 1 && discovery.GetDiscoveredDevices()[0].Port == 6000, new { events, port = discovery.GetDiscoveredDevices()[0].Port });
}

var serializer = new ProtocolSerializer();
foreach (var (name, json) in new[] { ("P04_nonobject_root_exception", "[]"), ("P04_out_of_range_type_exception", "{\"type\":2147483648,\"id\":\"00000000-0000-0000-0000-000000000001\",\"payload\":{}}") }) {
    try { serializer.Deserialize(Encoding.UTF8.GetBytes(json)); Record(name, false, "accepted"); }
    catch (Exception ex) { Record(name, ex is not JsonException, ex.GetType().Name); }
}

var message = Message.CreateHello(new HelloPayload("Probe", 1));
var payload = serializer.Serialize(message);
var frame = new byte[4 + payload.Length];
BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
payload.CopyTo(frame.AsSpan(4));
using (var cts = new CancellationTokenSource())
using (var stream = new CancelAfterTwoBytesStream(frame, cts))
using (var channel = new FramedMessageChannel(stream, serializer)) {
    try { await channel.ReceiveAsync(cts.Token); } catch (OperationCanceledException) { }
    try { await channel.ReceiveAsync(); Record("P01_retry_after_partial_cancellation_desynchronized", false, "received"); }
    catch (Exception ex) { Record("P01_retry_after_partial_cancellation_desynchronized", ex is InvalidDataException, ex.GetType().Name); }
}
using (var stream = new BlockingWriteStream()) {
    var channel = new FramedMessageChannel(stream, serializer);
    var send = channel.SendAsync(message).AsTask();
    await stream.WriteStarted.Task;
    channel.Dispose();
    try { await send; Record("P02_dispose_races_semaphore_release", false, "success"); }
    catch (ObjectDisposedException ex) { Record("P02_dispose_races_semaphore_release", ex.ObjectName == "System.Threading.SemaphoreSlim", new { ex.ObjectName, ex.Message }); }
}
using (var stream = new MemoryStream())
using (var channel = new FramedMessageChannel(stream, serializer)) {
    var messages = Enumerable.Range(0, 64).Select(i => Message.CreateHello(new HelloPayload($"Peer{i}", 1))).ToArray();
    await Task.WhenAll(messages.Select(m => channel.SendAsync(m).AsTask()));
    stream.Position = 0;
    var received = new HashSet<MessageId>();
    for (int i = 0; i < messages.Length; i++) received.Add((await channel.ReceiveAsync()).Id);
    Record("positive_concurrent_send_preserves_64_frames", received.Count == messages.Length, received.Count);
}

var isolatedConfig = Path.Combine(Environment.GetEnvironmentVariable("LANTERN_REVIEW_WORKDIR") ?? throw new InvalidOperationException("Set LANTERN_REVIEW_WORKDIR to an isolated scratch directory."), "probe-config", args.Contains("--sockets") ? "sockets" : "streams");
Directory.CreateDirectory(isolatedConfig);
Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", isolatedConfig);
var configPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
if (configPath != isolatedConfig) throw new InvalidOperationException("Identity probes require the isolated scratch directory.");
var idFile = Path.Combine(configPath, "Lantern", "device-id.json");
if (File.Exists(idFile)) File.Delete(idFile); // only this probe's isolated fixture
Directory.CreateDirectory(idFile); // isolated scratch config: force persistence failure
var firstId = LocalDeviceIdentityProvider.LoadOrCreateDeviceId();
var secondId = LocalDeviceIdentityProvider.LoadOrCreateDeviceId();
Record("D04_persistence_failure_silently_changes_identity", firstId != secondId, new { firstId, secondId });
Directory.Delete(idFile);
File.WriteAllText(idFile, JsonSerializer.Serialize(new { deviceId = Guid.Empty }));
var emptyId = LocalDeviceIdentityProvider.LoadOrCreateDeviceId();
Record("D04_persisted_empty_id_accepted", emptyId == Guid.Empty, emptyId);

if (args.Contains("--sockets")) {
    var server = new NetworkServer(0);
    await server.DisposeAsync();
    await server.StartAsync();
    Record("P03_server_restarts_after_disposal", server.IsRunning, server.ListeningPort);
    await server.StopAsync();
}
Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
return results.Any(r => !(bool)r.GetType().GetProperty("reproduced")!.GetValue(r)!) ? 1 : 0;

sealed class CancelAfterTwoBytesStream(byte[] bytes, CancellationTokenSource cts) : MemoryStream(bytes, writable: true) {
    private int reads;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
        if (++reads == 1) return base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
        if (reads == 2) { cts.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
        return base.ReadAsync(buffer, cancellationToken);
    }
}
sealed class BlockingWriteStream : MemoryStream {
    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
        WriteStarted.TrySetResult();
        await closed.Task;
        throw new ObjectDisposedException(nameof(BlockingWriteStream));
    }
    protected override void Dispose(bool disposing) { closed.TrySetResult(); base.Dispose(disposing); }
}
