using System.Text;
using System.Text.Json;
using Lantern.Networking.Protocol;
using Xunit;

namespace Lantern.Tests;

public sealed class TransferProtocolTests
{
    public static IEnumerable<object[]> Payloads()
    {
        var id = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        yield return [new TransferRequestPayload(id, Guid.NewGuid(), Guid.NewGuid(), [new TransferFileMetadata(fileId, "Trip/file.bin", 5)])];
        yield return [new TransferAcceptedPayload(id)];
        yield return [new TransferRejectedPayload(id, "Declined")];
        yield return [new FileChunkPayload(id, fileId, 0, new byte[64 * 1024])];
        yield return [new FileCompletePayload(id, fileId)];
        yield return [new TransferCompletePayload(id)];
        yield return [new TransferAcknowledgedPayload(id)];
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void TypedTransferPayloadsRoundTripWithinTheFrameLimit(MessagePayload payload)
    {
        var serializer = new ProtocolSerializer();
        var message = new Message(MessagePayloadKindHelper.GetMessageType(payload), MessageId.NewId(), payload);
        var bytes = serializer.Serialize(message);
        var copy = serializer.Deserialize(bytes);
        Assert.Equal(message.Id, copy.Id);
        Assert.Equal(message.Type, copy.Type);
        Assert.Equal(payload.GetType(), copy.Payload.GetType());
        Assert.Equal(bytes, serializer.Serialize(copy));
        Assert.InRange(bytes.Length, 1, ProtocolLimits.MaximumMessageSizeBytes);
        Assert.DoesNotContain("sourcePath", Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void MissingConstructorFieldsCannotDefaultToAValidTransferPayload(int type)
    {
        var json = $"{{\"type\":{type},\"id\":\"11111111-1111-1111-1111-111111111111\",\"payload\":{{}}}}";
        Assert.Throws<JsonException>(() => new ProtocolSerializer().Deserialize(Encoding.UTF8.GetBytes(json)));
    }
}
