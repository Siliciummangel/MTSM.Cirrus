using MTSM.Cirrus.Core.Enums;
using System.Text.Json;

namespace MTSM.Cirrus.API.Contracts.Responses;

public sealed record ArchiveFileResponse(
    long ArchiveObjectId,
    string ObjectKey,
    string Sha256Hash,
    long SizeBytes,
    DateTimeOffset ArchivedAt);

public sealed record ArchiveBusinessReferenceResponse(
    int BusinessReferenceTypeId,
    string ReferenceValue,
    string BusinessType,
    string Tenant,
    DateTimeOffset CreatedAt);

public sealed record ArchiveEventResponse(
    long ArchiveEventId,
    ArchiveEventType EventType,
    DateTimeOffset EventTimestamp,
    string Actor,
    JsonElement? Details);

public sealed record ArchiveMetadataResponse(
    long ArchiveObjectId,
    string ObjectKey,
    string BucketName,
    string FileType,
    string? MimeType,
    string SourceSystem,
    string? Partner,
    string OriginalFilename,
    string? Sha256Hash,
    long SizeBytes,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ArchivedAt,
    DateOnly RetentionUntil,
    int? RetentionPolicyId,
    ArchiveStatus ArchiveStatus,
    string? StorageVersionId,
    string? EncryptionKeyId,
    bool IsWormProtected,
    string CreatedBy,
    IReadOnlyCollection<ArchiveBusinessReferenceResponse> BusinessReferences,
    IReadOnlyCollection<ArchiveEventResponse> Events);

