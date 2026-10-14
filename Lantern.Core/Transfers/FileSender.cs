using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Lantern.Models;

namespace Lantern.Transfers;

/// <summary>Bounded, encrypted file transfers. Each incoming batch requires explicit consent.</summary>
public sealed class FileSender(LocalDevice localDevice, string receiveFolder, X509Certificate2 certificate) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Session> _sessions = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _shutdown;
    private Task? _acceptTask;
    public event EventHandler<TransferInfo>? TransferChanged;
    public event EventHandler<TransferRequest>? TransferRequested;
    public int? ListeningPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port;
    public string Fingerprint => TransferCertificate.Fingerprint(certificate);

    public Task StartAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_listener is not null) throw new InvalidOperationException("Transfers are already listening.");
            var listener = new TcpListener(IPAddress.Any, localDevice.Port);
            listener.Start();
            _listener = listener;
            _shutdown = new CancellationTokenSource();
            _acceptTask = AcceptAsync(listener, _shutdown.Token);
        }
        return Task.CompletedTask;
    }

    public Task SendAsync(Device device, IReadOnlyList<string> paths,
        Func<string, CancellationToken, Task<bool>> verifyCertificate, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(verifyCertificate);
        var files = paths.ToArray();
        lock (_sync)
        {
            if (_shutdown is null) throw new InvalidOperationException("Start the transfer service before sending.");
            if (_sessions.Count >= 4) throw new InvalidOperationException("Four transfers are already active. Wait for one to finish.");
            var session = NewSession(device.Name, TransferDirection.Send, token);
            session.Task = Task.Run(() => RunAsync(session, () => SendCoreAsync(session, device, files, verifyCertificate)));
            return session.Task;
        }
    }

    public void Cancel(Guid id)
    {
        lock (_sync) { if (_sessions.TryGetValue(id, out var session)) session.Cancellation.Cancel(); }
    }

    private Session NewSession(string peer, TransferDirection direction, CancellationToken token)
    {
        var session = new Session(new TransferInfo(Guid.NewGuid(), peer, "Preparing files…", direction,
            TransferState.Preparing, 0, 0, DateTimeOffset.UtcNow), CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown!.Token));
        _sessions.Add(session.Info.Id, session);
        return session;
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (token.IsCancellationRequested && ex is OperationCanceledException or SocketException or ObjectDisposedException) { break; }
            lock (_sync)
            {
                if (token.IsCancellationRequested || _sessions.Count >= 4) { client.Dispose(); continue; }
                var session = NewSession(client.Client.RemoteEndPoint?.ToString() ?? "Incoming device", TransferDirection.Receive, token);
                session.Task = Task.Run(() => RunAsync(session, () => ReceiveCoreAsync(session, client)));
            }
        }
    }

    private async Task RunAsync(Session session, Func<Task> action)
    {
        try { Report(session); await action().ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            SetState(session, session.Cancellation.IsCancellationRequested ? TransferState.Canceled : TransferState.Failed,
                session.Cancellation.IsCancellationRequested ? null : "The peer did not respond before the timeout.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException or AuthenticationException or CryptographicException or
            UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        {
            SetState(session, TransferState.Failed, ex.Message);
        }
        finally
        {
            lock (_sync) _sessions.Remove(session.Info.Id);
            session.Cancellation.Dispose();
        }
    }

    private async Task SendCoreAsync(Session session, Device device, string[] paths, Func<string, CancellationToken, Task<bool>> verify)
    {
        var token = session.Cancellation.Token;
        if (paths.Length is < 1 or > TransferWire.MaximumFiles) throw new InvalidDataException("Select between 1 and 100 files.");
        var offered = new List<OfferedFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            TransferWire.ValidateFileName(Path.GetFileName(path));
            if (!names.Add(Path.GetFileName(path))) throw new InvalidDataException("A transfer cannot contain duplicate file names.");
            await using var file = OpenRead(path);
            if (file.Length > TransferWire.MaximumBytes - total) throw new InvalidDataException("A transfer cannot exceed 1 TB.");
            total += file.Length;
            var hash = await SHA256.HashDataAsync(file, token).ConfigureAwait(false);
            offered.Add(new OfferedFile(Path.GetFileName(path), file.Length, Convert.ToHexString(hash)));
        }
        var offer = new TransferOffer(1, session.Info.Id, localDevice.Name, offered.ToArray());
        TransferWire.Validate(offer);
        session.Info = session.Info with { Files = string.Join(", ", offered.Select(f => f.Name)), TotalBytes = offered.Sum(f => f.Size) };
        SetState(session, TransferState.Connecting);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        ResetTimeout(timeout);
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(device.IpAddress, device.Port, timeout.Token).ConfigureAwait(false);
        // Only TLS negotiation happens before the user verifies the certificate. No offer or file data is sent yet.
        using var stream = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null);
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "Lantern", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, timeout.Token).ConfigureAwait(false);
        timeout.CancelAfter(Timeout.InfiniteTimeSpan);
        SetState(session, TransferState.AwaitingApproval);
        using (var verificationTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            verificationTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            if (!await verify(TransferCertificate.Fingerprint(stream.RemoteCertificate!), verificationTimeout.Token).ConfigureAwait(false))
            { SetState(session, TransferState.Rejected, "Device verification was declined."); return; }
        }
        ResetTimeout(timeout);
        await TransferWire.WriteAsync(stream, offer, timeout.Token).ConfigureAwait(false);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var reply = await TransferWire.ReadAsync<TransferReply>(stream, timeout.Token).ConfigureAwait(false);
        if (!reply.Accepted) { SetState(session, TransferState.Rejected, reply.Error ?? "The recipient declined the transfer."); return; }
        SetState(session, TransferState.Sending);
        var buffer = new byte[TransferWire.ChunkSize];
        for (var i = 0; i < paths.Length; i++)
        {
            await using var file = OpenRead(paths[i]);
            if (file.Length != offered[i].Size) throw new IOException("A selected file changed. Select it again and retry.");
            long remaining = file.Length;
            while (remaining > 0)
            {
                ResetTimeout(timeout);
                var count = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), timeout.Token).ConfigureAwait(false);
                if (count == 0) throw new IOException("A selected file changed during the transfer.");
                await stream.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                remaining -= count;
                Advance(session, count);
            }
        }
        ResetTimeout(timeout);
        var completed = await TransferWire.ReadAsync<TransferReply>(stream, timeout.Token).ConfigureAwait(false);
        if (!completed.Accepted) throw new IOException(completed.Error ?? "The recipient could not verify the files.");
        SetState(session, TransferState.Completed);
    }

    private async Task ReceiveCoreAsync(Session session, TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token))
        using (var stream = new SslStream(client.GetStream(), false))
        {
            ResetTimeout(timeout);
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificateRequired = false
            }, timeout.Token).ConfigureAwait(false);
            // Allow time for the sender to compare the recipient's fingerprint.
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var offer = await TransferWire.ReadAsync<TransferOffer>(stream, timeout.Token).ConfigureAwait(false);
            TransferWire.Validate(offer);
            session.Info = session.Info with { Peer = offer.Sender, Files = string.Join(", ", offer.Files.Select(f => f.Name)), TotalBytes = offer.Files.Sum(f => f.Size) };
            SetState(session, TransferState.AwaitingApproval);
            var request = new TransferRequest(session.Info.Id, offer.Sender,
                ((IPEndPoint)client.Client.RemoteEndPoint!).Address,
                offer.Files.Select(f => new TransferFile(f.Name, f.Size, "", f.Hash)).ToArray());
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            TransferRequested?.Invoke(this, request);
            var accepted = await request.WaitAsync(timeout.Token).ConfigureAwait(false);
            ResetTimeout(timeout);
            await TransferWire.WriteAsync(stream, new TransferReply(accepted), timeout.Token).ConfigureAwait(false);
            if (!accepted) { SetState(session, TransferState.Rejected); return; }
            SetState(session, TransferState.Receiving);
            Directory.CreateDirectory(receiveFolder);
            if ((File.GetAttributes(receiveFolder) & FileAttributes.ReparsePoint) != 0) throw new IOException("The receive folder cannot be a symbolic link.");
            var batchName = $"Lantern-{DateTime.Now:yyyyMMdd-HHmmss}-{session.Info.Id:N}";
            var staging = Path.Combine(receiveFolder, "." + batchName + ".partial");
            var destination = Path.Combine(receiveFolder, batchName);
            Directory.CreateDirectory(staging);
            try
            {
                var buffer = new byte[TransferWire.ChunkSize];
                foreach (var file in offer.Files)
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    await using (var output = new FileStream(Path.Combine(staging, file.Name), FileMode.CreateNew, FileAccess.Write, FileShare.None, TransferWire.ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        long remaining = file.Size;
                        while (remaining > 0)
                        {
                            ResetTimeout(timeout);
                            var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), timeout.Token).ConfigureAwait(false);
                            if (count == 0) throw new EndOfStreamException("The sender disconnected before all files arrived.");
                            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                            hash.AppendData(buffer, 0, count);
                            remaining -= count;
                            Advance(session, count);
                        }
                    }
                    if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(file.Hash)))
                        throw new InvalidDataException("File verification failed. The incomplete batch was discarded.");
                }
                session.Cancellation.Token.ThrowIfCancellationRequested();
                Directory.Move(staging, destination);
                session.Info = session.Info with { SavedFolder = destination };
                ResetTimeout(timeout);
                await TransferWire.WriteAsync(stream, new TransferReply(true), timeout.Token).ConfigureAwait(false);
                SetState(session, TransferState.Completed);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, TransferWire.ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
    private static void ResetTimeout(CancellationTokenSource source) => source.CancelAfter(TimeSpan.FromSeconds(30));
    private void Advance(Session session, int count)
    {
        session.Info = session.Info with { BytesTransferred = session.Info.BytesTransferred + count };
        if (session.Info.BytesTransferred != count && session.ProgressClock.ElapsedMilliseconds < 100) return;
        session.ProgressClock.Restart();
        Report(session);
    }
    private void SetState(Session session, TransferState state, string? error = null)
    { session.Info = session.Info with { State = state, Error = error }; Report(session); }
    private void Report(Session session) => TransferChanged?.Invoke(this, session.Info);

    public async ValueTask DisposeAsync()
    {
        Task? accept;
        Task[] transfers;
        CancellationTokenSource? shutdown;
        lock (_sync)
        {
            shutdown = _shutdown;
            if (shutdown is null) return;
            shutdown.Cancel();
            _listener?.Stop();
            _listener = null;
            _shutdown = null;
            accept = _acceptTask;
            transfers = _sessions.Values.Select(s => s.Task).ToArray();
        }
        try
        {
            if (accept is not null) await accept.ConfigureAwait(false);
            await Task.WhenAll(transfers).ConfigureAwait(false);
        }
        finally { shutdown.Dispose(); }
    }

    private sealed class Session(TransferInfo info, CancellationTokenSource cancellation)
    {
        public TransferInfo Info = info;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task = Task.CompletedTask;
        public Stopwatch ProgressClock { get; } = Stopwatch.StartNew();
    }
}
