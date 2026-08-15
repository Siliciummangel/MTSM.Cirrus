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
    DateTimeOffset? DeletionRequestedAt,
    string? DeletionRequestedBy,
    DateTimeOffset? PurgedAt,
    string? StorageVersionId,
    string? EncryptionKeyId,
    bool IsWormProtected,
    string CreatedBy,
    IReadOnlyCollection<ArchiveBusinessReferenceResponse> BusinessReferences,
    IReadOnlyCollection<ArchiveEventResponse> Events);

public sealed record ArchiveSearchItemResponse(
    long ArchiveObjectId,
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
    ArchiveStatus ArchiveStatus,
    DateTimeOffset? DeletionRequestedAt,
    DateTimeOffset? PurgedAt,
    IReadOnlyCollection<ArchiveBusinessReferenceResponse> BusinessReferences);

public sealed record ArchiveSearchResponse(
    IReadOnlyCollection<ArchiveSearchItemResponse> Items,
    int PageNumber,
    int PageSize,
    long TotalCount,
    int TotalPages);

public sealed record ArchiveIntegrityResponse(
    long ArchiveObjectId,
    bool IsValid,
    string ExpectedSha256Hash,
    string ActualSha256Hash,
    long ExpectedSizeBytes,
    long ActualSizeBytes,
    DateTimeOffset VerifiedAt);

public sealed record ArchiveIntegrityStatusResponse(
    long ArchiveObjectId,
    DateTimeOffset? LastCheckedAt,
    bool? LastCheckIsValid,
    string? LastCheckActor,
    DateTimeOffset? NextCheckAt,
    bool IsCheckInProgress,
    string? LeaseOwner,
    DateTimeOffset? LeaseUntil);

public sealed record ArchiveDeletionRequestResponse(
    long ArchiveObjectId,
    ArchiveStatus ArchiveStatus,
    DateTimeOffset? DeletionRequestedAt,
    string? DeletionRequestedBy,
    DateTimeOffset? PurgedAt,
    bool StateChanged);
