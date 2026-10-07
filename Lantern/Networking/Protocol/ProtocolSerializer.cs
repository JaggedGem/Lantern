using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lantern.Networking.Protocol;

public sealed class ProtocolSerializer
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ProtocolSerializer()
    {
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
#if NET10_0_OR_GREATER
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
#endif
            Encoder = JavaScriptEncoder.Default
        };

        _jsonOptions.Converters.Add(new MessageIdJsonConverter());
    }

    public byte[] Serialize(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
            Encoder = JavaScriptEncoder.Default
        });

        writer.WriteStartObject();
        writer.WriteNumber("type", (int)message.Type);
        writer.WritePropertyName("id");
        writer.WriteStringValue(message.Id.Value);
        writer.WritePropertyName("payload");
        WritePayload(writer, message.Payload);
        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenMemory.ToArray();
    }

    public Message Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
        }
        catch (JsonException ex)
        {
            throw new JsonException("The protocol message is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                throw new JsonException("The message is missing the 'type' property.");
            }

            if (!root.TryGetProperty("id", out var idElement))
            {
                throw new JsonException("The message is missing the 'id' property.");
            }

            if (!root.TryGetProperty("payload", out var payloadElement))
            {
                throw new JsonException("The message is missing the 'payload' property.");
            }

            var type = ReadMessageType(typeElement);
            var id = ReadMessageId(idElement);
            var payload = ReadPayload(type, payloadElement);

            return new Message(type, id, payload);
        }
    }

    private void WritePayload(Utf8JsonWriter writer, MessagePayload payload)
    {
        switch (payload)
        {
            case HelloPayload helloPayload:
                JsonSerializer.Serialize(writer, helloPayload, typeof(HelloPayload), _jsonOptions);
                return;
            case ErrorPayload errorPayload:
                JsonSerializer.Serialize(writer, errorPayload, typeof(ErrorPayload), _jsonOptions);
                return;
            default:
                throw new NotSupportedException($"Unsupported payload type '{payload.GetType().FullName}'.");
        }
    }

    private static MessageType ReadMessageType(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            throw new JsonException("The message type must be a number.");
        }

        var value = element.GetInt32();
        return value switch
        {
            (int)MessageType.Hello => MessageType.Hello,
            (int)MessageType.Error => MessageType.Error,
            _ => throw new JsonException($"The message type '{value}' is not supported.")
        };
    }

    private static MessageId ReadMessageId(JsonElement element)
    {
        try
        {
            return new MessageId(element.GetGuid());
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            throw new JsonException("The message identifier is not a valid GUID.", ex);
        }
    }

    private MessagePayload ReadPayload(MessageType type, JsonElement payloadElement)
        => type switch
        {
            MessageType.Hello => DeserializePayload<HelloPayload>(payloadElement),
            MessageType.Error => DeserializePayload<ErrorPayload>(payloadElement),
            _ => throw new JsonException($"The message type '{type}' is not supported.")
        };

    private MessagePayload DeserializePayload<TPayload>(JsonElement payloadElement)
        where TPayload : MessagePayload
    {
        try
        {
            var payload = payloadElement.Deserialize<TPayload>(_jsonOptions);
            return payload ?? throw new JsonException($"The payload could not be deserialized as '{typeof(TPayload).Name}'.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new JsonException($"The payload could not be deserialized as '{typeof(TPayload).Name}'.", ex);
        }
    }
}





