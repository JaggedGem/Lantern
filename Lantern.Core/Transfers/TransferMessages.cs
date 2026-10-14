using Message = Lantern.Networking.Protocol.Message;
using Lantern.Networking;
using Lantern.Networking.Protocol;

namespace Lantern.Transfers;

internal static class TransferMessages
{
    public static ValueTask SendAsync(Connection connection, MessagePayload payload, CancellationToken token)
        => connection.SendAsync(new Message(MessagePayloadKindHelper.GetMessageType(payload), MessageId.NewId(), payload), token);

    public static async Task<T> ReceiveAsync<T>(Connection connection, TimeSpan timeout, CancellationToken token) where T : MessagePayload
    {
        var payload = await ReceiveAsync(connection, timeout, token).ConfigureAwait(false);
        return payload as T ?? throw new InvalidDataException($"Expected {typeof(T).Name}, received {payload.GetType().Name}.");
    }

    public static async Task<MessagePayload> ReceiveAsync(Connection connection, TimeSpan timeout, CancellationToken token)
    {
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(token);
        timed.CancelAfter(timeout);
        try { return (await connection.ReceiveAsync(timed.Token).ConfigureAwait(false)).Payload; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("The peer did not respond before the transfer timeout."); }
    }
}
