using MTSM.Cirrus.Core.Models;

namespace MTSM.Cirrus.Core.Abstractions;

public interface IArchiveService
{
    Task<ArchiveFileResult> ArchiveAsync(
        ArchiveFileRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveDownloadResult> DownloadAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<ArchiveMetadataResult?> GetMetadataAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken = default);

    Task<ArchiveSearchResult> SearchAsync(
        long tenantId,
        ArchiveSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveIntegrityResult> VerifyIntegrityAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<ArchiveIntegrityStatusResult?> GetIntegrityStatusAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken = default);

    Task<ArchiveDeletionRequestResult> RequestDeletionAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default);
}
