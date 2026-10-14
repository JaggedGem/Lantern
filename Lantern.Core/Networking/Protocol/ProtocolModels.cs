using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lantern.Networking.Protocol;

public enum MessageType
{
    Hello = 1,
    Error = 2,
    TransferRequest = 3,
    TransferAccepted = 4,
    TransferRejected = 5,
    FileChunk = 6,
    FileComplete = 7,
    TransferComplete = 8,
    TransferAcknowledged = 9
}

[JsonConverter(typeof(MessageIdJsonConverter))]
public readonly record struct MessageId
{
    public MessageId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The message identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static MessageId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public abstract record MessagePayload;

public sealed record HelloPayload : MessagePayload
{
    public HelloPayload(string applicationName, int protocolVersion, Guid? deviceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        if (protocolVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolVersion), protocolVersion, "The protocol version must be greater than zero.");
        }

        if (deviceId == Guid.Empty) throw new ArgumentException("Empty peer identity.", nameof(deviceId));
        DeviceId = deviceId;
        ApplicationName = applicationName;
        ProtocolVersion = protocolVersion;
    }

    public Guid? DeviceId { get; }

    public string ApplicationName { get; }

    public int ProtocolVersion { get; }
}

public sealed record ErrorPayload : MessagePayload
{
    public ErrorPayload(string code, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Code = code;
        Description = description;
    }

    public string Code { get; }

    public string Description { get; }
}

public sealed record Message
{
    public Message(MessageType type, MessageId id, MessagePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("The message identifier cannot be empty.", nameof(id));
        }

        if (!MessagePayloadKindHelper.IsCompatible(type, payload))
        {
            throw new ArgumentException($"The payload type '{payload.GetType().Name}' is not valid for message type '{type}'.", nameof(payload));
        }

        Type = type;
        Id = id;
        Payload = payload;
    }

    public MessageType Type { get; }

    public MessageId Id { get; }

    public MessagePayload Payload { get; }

    public static Message CreateHello(HelloPayload payload, MessageId? id = null)
        => new(MessageType.Hello, id ?? MessageId.NewId(), payload);

    public static Message CreateError(ErrorPayload payload, MessageId? id = null)
        => new(MessageType.Error, id ?? MessageId.NewId(), payload);
}

internal static class MessagePayloadKindHelper
{
    public static MessageType GetMessageType(MessagePayload payload)
        => payload switch
        {
            HelloPayload => MessageType.Hello,
            ErrorPayload => MessageType.Error,
            TransferRequestPayload => MessageType.TransferRequest,
            TransferAcceptedPayload => MessageType.TransferAccepted,
            TransferRejectedPayload => MessageType.TransferRejected,
            FileChunkPayload => MessageType.FileChunk,
            FileCompletePayload => MessageType.FileComplete,
            TransferCompletePayload => MessageType.TransferComplete,
            TransferAcknowledgedPayload => MessageType.TransferAcknowledged,
            _ => throw new NotSupportedException($"Unsupported payload type '{payload.GetType().FullName}'.")
        };

    public static bool IsCompatible(MessageType type, MessagePayload payload) => type == GetMessageType(payload);
}

internal sealed class MessageIdJsonConverter : JsonConverter<MessageId>
{
    public override MessageId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("The message identifier must be a string.");
        }

        try
        {
            return new MessageId(reader.GetGuid());
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            throw new JsonException("The message identifier is not a valid GUID.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, MessageId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}



