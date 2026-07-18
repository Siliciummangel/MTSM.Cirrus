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
            result.StorageVersionId,
            result.EncryptionKeyId,
            result.IsWormProtected,
            result.CreatedBy,
            references,
            events);
    }

    private static JsonElement? CloneJsonElement(
        JsonDocument? document)
    {
        return document?.RootElement.Clone();
    }
}