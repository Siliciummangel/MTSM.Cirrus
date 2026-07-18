using MTSM.Cirrus.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace MTSM.Cirrus.Core.Models;

public sealed record ObjectStorageWriteResult(
    string? VersionId,
    string? ETag);

public sealed record ArchiveBusinessReferenceInput(
    int BusinessReferenceTypeId,
    string ReferenceValue,
    string BusinessType,
    string Tenant);

public sealed record ArchiveFileResult(
    long ArchiveObjectId,
    string ObjectKey,
    string Sha256Hash,
    long SizeBytes,
    DateTimeOffset ArchivedAt);

public sealed record ArchiveDownloadResult(
    long ArchiveObjectId,
    string OriginalFilename,
    string? MimeType,
    long SizeBytes,
    string Sha256Hash,
    Stream Content);

public sealed record ArchiveBusinessReferenceResult(
    int BusinessReferenceTypeId,
    string ReferenceValue,
    string BusinessType,
    string Tenant,
    DateTimeOffset CreatedAt);

public sealed record ArchiveEventResult(
    long ArchiveEventId,
    ArchiveEventType EventType,
    DateTimeOffset EventTimestamp,
    string Actor,
    JsonDocument? DetailsJson);

public sealed record ArchiveMetadataResult(
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
    IReadOnlyCollection<ArchiveBusinessReferenceResult> BusinessReferences,
    IReadOnlyCollection<ArchiveEventResult> Events);

public sealed record ArchiveSearchItem(
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
    IReadOnlyCollection<ArchiveBusinessReferenceResult> BusinessReferences);

public sealed record ArchiveSearchResult(
    IReadOnlyCollection<ArchiveSearchItem> Items,
    int PageNumber,
    int PageSize,
    long TotalCount,
    int TotalPages);

public sealed record ArchiveIntegrityResult(
    long ArchiveObjectId,
    bool IsValid,
    string ExpectedSha256Hash,
    string ActualSha256Hash,
    long ExpectedSizeBytes,
    long ActualSizeBytes,
    DateTimeOffset VerifiedAt);