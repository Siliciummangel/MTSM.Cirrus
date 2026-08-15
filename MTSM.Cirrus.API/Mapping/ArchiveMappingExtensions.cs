using MTSM.Cirrus.API.Contracts.Responses;
using MTSM.Cirrus.Core.Models;
using System.Text.Json;

namespace MTSM.Cirrus.API.Mapping;

public static class ArchiveResponseMapper
{
    public static ArchiveFileResponse Map(
        ArchiveFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ArchiveFileResponse(
            result.ArchiveObjectId,
            result.ObjectKey,
            result.Sha256Hash,
            result.SizeBytes,
            result.ArchivedAt);
    }

    public static ArchiveMetadataResponse Map(
        ArchiveMetadataResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ArchiveBusinessReferenceResponse[] references =
            result.BusinessReferences
                .Select(reference =>
                    new ArchiveBusinessReferenceResponse(
                        reference.BusinessReferenceTypeId,
                        reference.ReferenceValue,
                        reference.BusinessType,
                        reference.Tenant,
                        reference.CreatedAt))
                .ToArray();

        ArchiveEventResponse[] events =
            result.Events
                .Select(archiveEvent =>
                    new ArchiveEventResponse(
                        archiveEvent.ArchiveEventId,
                        archiveEvent.EventType,
                        archiveEvent.EventTimestamp,
                        archiveEvent.Actor,
                        CloneJsonElement(
                            archiveEvent.DetailsJson)))
                .ToArray();

        return new ArchiveMetadataResponse(
            result.ArchiveObjectId,
            result.ObjectKey,
            result.BucketName,
            result.FileType,
            result.MimeType,
            result.SourceSystem,
            result.Partner,
            result.OriginalFilename,
            result.Sha256Hash,
            result.SizeBytes,
            result.ReceivedAt,
            result.ArchivedAt,
            result.RetentionUntil,
            result.RetentionPolicyId,
            result.ArchiveStatus,
            result.DeletionRequestedAt,
            result.DeletionRequestedBy,
            result.PurgedAt,
            result.StorageVersionId,
            result.EncryptionKeyId,
            result.IsWormProtected,
            result.CreatedBy,
            references,
            events);
    }

    public static ArchiveDeletionRequestResponse Map(
        ArchiveDeletionRequestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ArchiveDeletionRequestResponse(
            result.ArchiveObjectId,
            result.ArchiveStatus,
            result.DeletionRequestedAt,
            result.DeletionRequestedBy,
            result.PurgedAt,
            result.StateChanged);
    }

    public static ArchiveSearchResponse Map(
        ArchiveSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ArchiveSearchItemResponse[] items =
            result.Items
                .Select(item =>
                    new ArchiveSearchItemResponse(
                        item.ArchiveObjectId,
                        item.FileType,
                        item.MimeType,
                        item.SourceSystem,
                        item.Partner,
                        item.OriginalFilename,
                        item.Sha256Hash,
                        item.SizeBytes,
                        item.ReceivedAt,
                        item.ArchivedAt,
                        item.RetentionUntil,
                        item.ArchiveStatus,
                        item.DeletionRequestedAt,
                        item.PurgedAt,
                        item.BusinessReferences
                            .Select(reference =>
                                new ArchiveBusinessReferenceResponse(
                                    reference.BusinessReferenceTypeId,
                                    reference.ReferenceValue,
                                    reference.BusinessType,
                                    reference.Tenant,
                                    reference.CreatedAt))
                            .ToArray()))
                .ToArray();

        return new ArchiveSearchResponse(
            items,
            result.PageNumber,
            result.PageSize,
            result.TotalCount,
            result.TotalPages);
    }

    public static ArchiveIntegrityResponse Map(
        ArchiveIntegrityResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ArchiveIntegrityResponse(
            result.ArchiveObjectId,
            result.IsValid,
            result.ExpectedSha256Hash,
            result.ActualSha256Hash,
            result.ExpectedSizeBytes,
            result.ActualSizeBytes,
            result.VerifiedAt);
    }

    public static ArchiveIntegrityStatusResponse Map(
        ArchiveIntegrityStatusResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ArchiveIntegrityStatusResponse(
            result.ArchiveObjectId,
            result.LastCheckedAt,
            result.LastCheckIsValid,
            result.LastCheckActor,
            result.NextCheckAt,
            result.IsCheckInProgress,
            result.LeaseOwner,
            result.LeaseUntil);
    }

    private static JsonElement? CloneJsonElement(
        JsonDocument? document)
    {
        return document?.RootElement.Clone();
    }
}
