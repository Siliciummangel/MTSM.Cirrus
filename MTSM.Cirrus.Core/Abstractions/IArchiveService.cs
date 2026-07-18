using MTSM.Cirrus.Core.Models;

namespace MTSM.Cirrus.Core.Abstractions;

public interface IArchiveService
{
    Task<ArchiveFileResult> ArchiveAsync(
        ArchiveFileRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveDownloadResult> DownloadAsync(
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<ArchiveMetadataResult?> GetMetadataAsync(
        long archiveObjectId,
        CancellationToken cancellationToken = default);

    Task<ArchiveSearchResult> SearchAsync(
        ArchiveSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveIntegrityResult> VerifyIntegrityAsync(
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default);
}