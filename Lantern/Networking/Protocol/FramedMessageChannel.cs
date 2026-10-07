using System.Buffers;
using System.Buffers.Binary;

namespace Lantern.Networking.Protocol;

internal sealed class FramedMessageChannel : IDisposable
{
    private const int LengthPrefixSize = sizeof(int);

    private readonly Stream _stream;
    private readonly ProtocolSerializer _serializer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private readonly int _maximumMessageSizeBytes;
    private int _isDisposed;

    public FramedMessageChannel(Stream stream, ProtocolSerializer serializer, int maximumMessageSizeBytes = 1_048_576)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(serializer);

        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException("The stream must support both reading and writing.", nameof(stream));
        }

        if (maximumMessageSizeBytes < LengthPrefixSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMessageSizeBytes), maximumMessageSizeBytes, "The maximum message size must be large enough to hold a frame payload.");
        }

        _stream = stream;
        _serializer = serializer;
        _maximumMessageSizeBytes = maximumMessageSizeBytes;
    }

    public async ValueTask SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var payload = _serializer.Serialize(message);
        if (payload.Length > _maximumMessageSizeBytes)
        {
            throw new InvalidDataException($"The serialized message size {payload.Length} exceeds the maximum allowed size of {_maximumMessageSizeBytes} bytes.");
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var lengthPrefix = new byte[LengthPrefixSize];
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, payload.Length);

            await _stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask<Message> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            var lengthPrefix = new byte[LengthPrefixSize];
            await ReadExactlyAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);

            var messageLength = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
            if (messageLength <= 0)
            {
                throw new InvalidDataException($"The framed message length {messageLength} is invalid.");
            }

            if (messageLength > _maximumMessageSizeBytes)
            {
                throw new InvalidDataException($"The framed message length {messageLength} exceeds the maximum allowed size of {_maximumMessageSizeBytes} bytes.");
            }

            var payloadBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
            try
            {
                await ReadExactlyAsync(payloadBuffer.AsMemory(0, messageLength), cancellationToken).ConfigureAwait(false);
                return _serializer.Deserialize(payloadBuffer.AsSpan(0, messageLength));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payloadBuffer);
            }
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _sendLock.Dispose();
        _receiveLock.Dispose();
        _stream.Dispose();
    }

    private async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var remaining = buffer;
        while (!remaining.IsEmpty)
        {
            var bytesRead = await _stream.ReadAsync(remaining, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("The remote peer closed the connection before the expected message bytes were received.");
            }

            remaining = remaining.Slice(bytesRead);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            throw new ObjectDisposedException(nameof(FramedMessageChannel));
        }
    }
}



