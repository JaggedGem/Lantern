namespace Lantern.Networking.Protocol;

// Wire metadata deliberately excludes absolute local paths and implementation objects.
public sealed record TransferFileMetadata(Guid Id, string RelativePath, long SizeBytes, DateTimeOffset? ModifiedAt = null);
public sealed record TransferRequestPayload(Guid TransferId, Guid SourceDeviceId, Guid DestinationDeviceId, IReadOnlyList<TransferFileMetadata> Files) : MessagePayload;
public sealed record TransferAcceptedPayload(Guid TransferId) : MessagePayload;
public sealed record TransferRejectedPayload(Guid TransferId, string Reason) : MessagePayload;
public sealed record FileChunkPayload(Guid TransferId, Guid FileId, long Offset, byte[] Data) : MessagePayload;
public sealed record FileCompletePayload(Guid TransferId, Guid FileId) : MessagePayload;
public sealed record TransferCompletePayload(Guid TransferId) : MessagePayload;
public sealed record TransferAcknowledgedPayload(Guid TransferId) : MessagePayload;
