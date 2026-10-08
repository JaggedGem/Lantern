using Message = Lantern.Networking.Protocol.Message;
using System.Net.Sockets;
using System.Text.Json;
using Lantern.Models;
using Lantern.Networking;
using Lantern.Networking.Protocol;

namespace Lantern.Transfers;

/// <summary>Owns per-transfer connections and current immutable state. One operation per TCP connection.</summary>
public sealed class TransferManager : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Guid _localId;
    private readonly TransferOptions _options;
    private readonly NetworkServer _server;
    private readonly Dictionary<Guid, Transfer> _transfers = new();
    private readonly Dictionary<Guid, Operation> _operations = new();
    private readonly HashSet<Task> _workers = new();
    private readonly HashSet<Connection> _connections = new();
    private CancellationTokenSource? _shutdown;
    private Task? _stop;
    private bool _disposed;

    public TransferManager(Guid localDeviceId, int tcpPort = 0, TransferOptions? options = null)
    {
        if (localDeviceId == Guid.Empty) throw new ArgumentException("Empty installation ID.", nameof(localDeviceId));
        _localId = localDeviceId;
        _options = options ?? new TransferOptions();
        _options.Validate();
        _server = new NetworkServer(tcpPort);
        _server.ConnectionAccepted += OnAccepted;
    }

    public int? ListeningPort => _server.ListeningPort;
    public event EventHandler<IncomingTransferRequestEventArgs>? IncomingTransferRequested;
    public event EventHandler<TransferChangedEventArgs>? TransferChanged;
    public event EventHandler<Exception>? Error;
    public IReadOnlyList<Transfer> GetTransfers() { lock (_sync) return _transfers.Values.ToList().AsReadOnly(); }

    public Task StartAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_shutdown != null) throw new InvalidOperationException("The transfer manager is running or stopping.");
            _server.StartAsync(token).GetAwaiter().GetResult();
            _shutdown = new CancellationTokenSource();
        }
        return Task.CompletedTask;
    }

    public Task<Transfer> SendAsync(Device destination, IEnumerable<TransferSourceFile> sourceFiles, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        token.ThrowIfCancellationRequested();
        var files = sourceFiles.ToArray();
        if (files.Length is < 1 or > TransferOptions.MaximumFileCount || files.Any(file => file == null))
            throw new ArgumentException("Choose between 1 and 1024 source files.", nameof(sourceFiles));
        // Capture the endpoint; a caller may mutate its detached discovery snapshot later.
        var peer = destination.Snapshot();
        var transfer = Transfer.CreateSending(_localId, peer.Id, files.Select(file => file.Metadata), DateTimeOffset.UtcNow);
        lock (_sync)
        {
            EnsureRunning();
            if (_workers.Count(worker => !worker.IsCompleted) >= _options.MaximumConcurrentTransfers) throw new InvalidOperationException("The transfer concurrency limit has been reached.");
            var operation = new Operation(transfer, CancellationTokenSource.CreateLinkedTokenSource(_shutdown!.Token, token));
            Register(operation);
            var worker = SendCoreAsync(operation, peer, files);
            Track(worker);
            return worker;
        }
    }

    public bool Cancel(Guid transferId)
    {
        lock (_sync)
        {
            if (!_operations.TryGetValue(transferId, out var operation) || IsTerminal(operation.State)) return false;
            operation.Cancellation.Cancel();
            return true;
        }
    }

    private void OnAccepted(object? sender, ConnectionAcceptedEventArgs args)
    {
        lock (_sync)
        {
            if (_shutdown == null || _stop != null || _workers.Count(worker => !worker.IsCompleted) >= _options.MaximumConcurrentTransfers)
            { args.Connection.Dispose(); return; }
            _connections.Add(args.Connection);
            Track(ReceiveCoreAsync(args.Connection, _shutdown.Token));
        }
    }

    private void Track(Task worker)
    {
        _workers.Add(worker);
        _ = ObserveWorkerAsync(worker);
    }

    private async Task ObserveWorkerAsync(Task worker)
    {
        try { await worker.ConfigureAwait(false); }
        catch (Exception exception) { ReportError(exception); } // supervised task failure, never silently lost
        finally { lock (_sync) _workers.Remove(worker); }
    }

    private async Task<Transfer> SendCoreAsync(Operation operation, Device peer, TransferSourceFile[] files)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        var token = operation.Cancellation.Token;
        Connection? connection = null;
        try
        {
            Publish(operation);
            Update(operation, state => state.MarkConnecting());
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
            connect.CancelAfter(_options.InactivityTimeout);
            connection = await new NetworkClient().ConnectAsync(peer.IpAddress, peer.Port, connect.Token).ConfigureAwait(false);
            lock (_sync) _connections.Add(connection);
            using var close = token.Register(connection.Dispose);
            await connection.SendAsync(Message.CreateHello(new HelloPayload("Lantern", ProtocolLimits.Version, _localId)), token).ConfigureAwait(false);
            var hello = await TransferMessages.ReceiveAsync<HelloPayload>(connection, _options.InactivityTimeout, token).ConfigureAwait(false);
            if (hello.ProtocolVersion != ProtocolLimits.Version || hello.DeviceId != peer.Id)
                throw new InvalidDataException("The connected peer does not match the selected installation/protocol.");
            var manifest = files.Select(file => new TransferFileMetadata(file.Metadata.Id, file.Metadata.RelativePath,
                file.Metadata.SizeBytes, file.Metadata.ModifiedAt)).ToArray();
            await TransferMessages.SendAsync(connection, new TransferRequestPayload(operation.State.Id, _localId, peer.Id, manifest), token).ConfigureAwait(false);
            Update(operation, state => state.MarkWaitingForAcceptance());
            var response = await TransferMessages.ReceiveAsync(connection, _options.RequestTimeout, token).ConfigureAwait(false);
            if (response is TransferRejectedPayload rejected && rejected.TransferId == operation.State.Id)
            {
                Update(operation, state => state.Reject(EventTime(state)));
                return operation.State;
            }
            if (response is not TransferAcceptedPayload accepted || accepted.TransferId != operation.State.Id)
                throw new InvalidDataException("Invalid transfer acceptance.");
            Update(operation, state => state.Start(EventTime(state)));
            // Each write also has a bounded inactivity deadline; progress resets it.
            using var writes = CancellationTokenSource.CreateLinkedTokenSource(token);
            writes.CancelAfter(_options.InactivityTimeout);
            await new FileSender().SendAsync(connection, operation.State.Id, files, bytes =>
            {
                Update(operation, state => state.ReportProgress(bytes), isProgress: true);
                writes.CancelAfter(_options.InactivityTimeout);
            }, writes.Token).ConfigureAwait(false);
            var acknowledgment = await TransferMessages.ReceiveAsync<TransferAcknowledgedPayload>(connection, _options.InactivityTimeout, token).ConfigureAwait(false);
            if (acknowledgment.TransferId != operation.State.Id) throw new InvalidDataException("Invalid completion acknowledgment.");
            Update(operation, state => state.Complete(EventTime(state)));
        }
        catch (Exception exception) when (IsOperational(exception)) { FinishFailure(operation, exception); }
        catch (Exception exception) { FinishFailure(operation, exception); throw; }
        finally
        {
            connection?.Dispose();
            lock (_sync) { if (connection != null) _connections.Remove(connection); _operations.Remove(operation.State.Id); }
            operation.Cancellation.Dispose();
        }
        return operation.State;
    }

    private async Task ReceiveCoreAsync(Connection connection, CancellationToken lifetime)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        Operation? operation = null;
        IncomingTransferRequestEventArgs? request = null;
        try
        {
            using var closeLifetime = lifetime.Register(connection.Dispose);
            var hello = await TransferMessages.ReceiveAsync<HelloPayload>(connection, _options.InactivityTimeout, lifetime).ConfigureAwait(false);
            if (hello.ProtocolVersion != ProtocolLimits.Version || hello.DeviceId is null || hello.DeviceId == Guid.Empty)
                throw new InvalidDataException("Unsupported or unidentified peer.");
            await connection.SendAsync(Message.CreateHello(new HelloPayload("Lantern", ProtocolLimits.Version, _localId)), lifetime).ConfigureAwait(false);
            var payload = await TransferMessages.ReceiveAsync<TransferRequestPayload>(connection, _options.InactivityTimeout, lifetime).ConfigureAwait(false);
            if (payload.DestinationDeviceId != _localId || payload.SourceDeviceId != hello.DeviceId
                || payload.Files == null || payload.Files.Count is < 1 or > TransferOptions.MaximumFileCount)
                throw new InvalidDataException("Invalid request participants or manifest bounds.");
            var files = payload.Files.Select(file => file == null ? throw new InvalidDataException("Null file metadata.")
                : new TransferFile(file.Id, file.RelativePath, file.SizeBytes, file.ModifiedAt)).ToArray();
            var transfer = Transfer.CreateReceiving(payload.TransferId, payload.SourceDeviceId, _localId, files, DateTimeOffset.UtcNow);
            lock (_sync)
            {
                if (_transfers.ContainsKey(transfer.Id)) throw new InvalidDataException("Duplicate transfer identifier.");
                operation = new Operation(transfer, CancellationTokenSource.CreateLinkedTokenSource(lifetime));
                Register(operation);
            }
            using var closeOperation = operation.Cancellation.Token.Register(connection.Dispose);
            Update(operation, state => state.MarkWaitingForAcceptance());
            request = new IncomingTransferRequestEventArgs(operation.State);
            var handler = IncomingTransferRequested;
            if (handler == null) request.Reject();
            else handler(this, request);
            string? destination;
            try { destination = await request.Decision.WaitAsync(_options.RequestTimeout, operation.Cancellation.Token).ConfigureAwait(false); }
            catch (TimeoutException) { request.Reject(); destination = null; }
            if (destination == null)
            {
                await TransferMessages.SendAsync(connection, new TransferRejectedPayload(transfer.Id, "The recipient declined or did not answer in time."), operation.Cancellation.Token).ConfigureAwait(false);
                Update(operation, state => state.Reject(EventTime(state)));
                return;
            }
            await TransferMessages.SendAsync(connection, new TransferAcceptedPayload(transfer.Id), operation.Cancellation.Token).ConfigureAwait(false);
            Update(operation, state => state.Start(EventTime(state)));
            await new FileReceiver().ReceiveAsync(connection, operation.State, destination, _options.InactivityTimeout,
                bytes => Update(operation, state => state.ReportProgress(bytes), isProgress: true), operation.Cancellation.Token).ConfigureAwait(false);
            // Published files are complete locally even if the sender later loses this acknowledgment.
            Update(operation, state => state.Complete(EventTime(state)));
            await TransferMessages.SendAsync(connection, new TransferAcknowledgedPayload(transfer.Id), operation.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsOperational(exception))
        {
            if (operation != null) FinishFailure(operation, exception);
            else if (!lifetime.IsCancellationRequested) ReportError(exception);
        }
        catch (Exception exception)
        {
            if (operation != null) FinishFailure(operation, exception);
            throw;
        }
        finally
        {
            request?.Reject(); // Expired requests cannot later be accepted by a delayed UI callback.
            connection.Dispose();
            lock (_sync)
            {
                _connections.Remove(connection);
                if (operation != null) _operations.Remove(operation.State.Id);
            }
            operation?.Cancellation.Dispose();
        }
    }

    private void Register(Operation operation)
    {
        if (_transfers.Count >= _options.MaximumRetainedTransfers)
        {
            var oldest = _transfers.FirstOrDefault(pair => IsTerminal(pair.Value));
            if (oldest.Value != null) _transfers.Remove(oldest.Key);
            else throw new InvalidOperationException("All retained transfer slots are active.");
        }
        _operations.Add(operation.State.Id, operation);
        _transfers.Add(operation.State.Id, operation.State);
    }

    private void Update(Operation operation, Func<Transfer, Transfer> transition, bool isProgress = false)
    {
        lock (_sync)
        {
            operation.State = transition(operation.State);
            _transfers[operation.State.Id] = operation.State;
            if (isProgress)
            {
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (operation.LastProgressNotification != 0
                    && System.Diagnostics.Stopwatch.GetElapsedTime(operation.LastProgressNotification, now) < TimeSpan.FromMilliseconds(100)) return;
                operation.LastProgressNotification = now;
            }
        }
        Publish(operation);
    }

    private void Publish(Operation operation)
    {
        Transfer state;
        lock (_sync) state = operation.State;
        var handler = TransferChanged;
        if (handler == null) return;
        foreach (EventHandler<TransferChangedEventArgs> callback in handler.GetInvocationList())
        {
            try { callback(this, new TransferChangedEventArgs(state)); }
            catch (Exception exception) { ReportError(exception); }
        }
    }

    private void FinishFailure(Operation operation, Exception exception)
    {
        if (IsTerminal(operation.State)) { ReportError(exception); return; }
        if (operation.Cancellation.IsCancellationRequested)
            Update(operation, state => state.Cancel(EventTime(state)));
        else
        {
            var kind = exception switch
            {
                InvalidDataException or JsonException or ArgumentException => TransferFailureKind.Protocol,
                SocketException or TimeoutException or EndOfStreamException or OperationCanceledException => TransferFailureKind.Connection,
                IOException io when io.InnerException is SocketException => TransferFailureKind.Connection,
                IOException or UnauthorizedAccessException => TransferFailureKind.FileSystem,
                _ => TransferFailureKind.Unexpected
            };
            Update(operation, state => state.Fail(new TransferError(kind, string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message), EventTime(state)));
        }
    }

    private static DateTimeOffset EventTime(Transfer state)
    {
        var now = DateTimeOffset.UtcNow;
        var minimum = state.StartedAt ?? state.CreatedAt;
        return now > minimum ? now : minimum;
    }
    private static bool IsTerminal(Transfer state) => state.Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Cancelled or TransferStatus.Rejected;
    private static bool IsOperational(Exception error) => error is InvalidDataException or IOException or SocketException or UnauthorizedAccessException
        or TimeoutException or OperationCanceledException or JsonException or ArgumentException or ObjectDisposedException;
    private void ReportError(Exception error)
    {
        System.Diagnostics.Trace.TraceError(error.ToString());
        var handler = Error;
        if (handler == null) return;
        foreach (EventHandler<Exception> callback in handler.GetInvocationList())
        {
            try { callback(this, error); }
            catch (Exception observerFailure) { System.Diagnostics.Trace.TraceError(observerFailure.ToString()); }
        }
    }
    private void EnsureRunning()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_shutdown == null || _stop != null) throw new InvalidOperationException("Start the transfer manager before using it.");
    }

    public Task StopAsync()
    {
        lock (_sync) return _shutdown == null ? Task.CompletedTask : _stop ??= StopCoreAsync(_shutdown);
    }

    private async Task StopCoreAsync(CancellationTokenSource shutdown)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            shutdown.Cancel();
            var serverStop = _server.StopAsync();
            Task[] workers;
            lock (_sync) { foreach (var connection in _connections) connection.Dispose(); workers = _workers.ToArray(); }
            await Task.WhenAll(workers.Append(serverStop)).ConfigureAwait(false);
        }
        finally
        {
            shutdown.Dispose();
            lock (_sync) { _shutdown = null; _stop = null; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync) _disposed = true;
        try { await StopAsync().ConfigureAwait(false); }
        finally { await _server.DisposeAsync().ConfigureAwait(false); }
    }

    private sealed class Operation(Transfer state, CancellationTokenSource cancellation)
    {
        public Transfer State { get; set; } = state;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public long LastProgressNotification { get; set; }
    }
}
