using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Exceptions;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Core.Services;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using System.Security.Cryptography;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ArchiveServiceIntegrationTests(PostgresFixture fixture)
{
    [PostgresFact]
    public async Task ArchiveAsync_PersistsNormalizedMetadataHashSizeAndEvents()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        byte[] content = "archive payload"u8.ToArray();
        int referenceTypeId = await GetReferenceTypeIdAsync(dbContext, "document-id");

        ArchiveFileResult result = await service.ArchiveAsync(
            CreateRequest(
                content,
                fileName: "  invoice.pdf  ",
                fileType: "  invoice  ",
                sourceSystem: "  erp  ",
                partner: "  partner-a  ",
                actor: "  integration-suite  ",
                references:
                [
                    new ArchiveBusinessReferenceInput(
                        referenceTypeId,
                        "  DOC-42  ",
                        "  invoice  ",
                        "  tenant-a  ")
                ]));

        dbContext.ChangeTracker.Clear();
        ArchiveObject persisted = await dbContext.ArchiveObjects
            .Include(item => item.BusinessReferences)
            .Include(item => item.Events)
            .SingleAsync(item => item.ArchiveObjectId == result.ArchiveObjectId);

        Assert.Equal(ArchiveStatus.Active, persisted.ArchiveStatus);
        Assert.Equal("invoice.pdf", persisted.OriginalFilename);
        Assert.Equal("invoice", persisted.FileType);
        Assert.Equal("erp", persisted.SourceSystem);
        Assert.Equal("partner-a", persisted.Partner);
        Assert.Equal("integration-suite", persisted.CreatedBy);
        Assert.Equal(content.Length, persisted.SizeBytes);
        Assert.Equal(Sha256(content), persisted.Sha256Hash);
        Assert.Equal("version-1", persisted.StorageVersionId);
        Assert.Equal("DOC-42", Assert.Single(persisted.BusinessReferences).ReferenceValue);
        Assert.Equal(
            [ArchiveEventType.Created, ArchiveEventType.Archived],
            persisted.Events.OrderBy(item => item.EventTimestamp).Select(item => item.EventType));
        Assert.True(await storage.ExistsAsync(persisted.BucketName, persisted.ObjectKey));

        ArchiveMetadataResult? metadata = await service.GetMetadataAsync(result.ArchiveObjectId);
        Assert.NotNull(metadata);
        Assert.Equal(result.Sha256Hash, metadata.Sha256Hash);
        Assert.Equal(2, metadata.Events.Count);
    }

    [PostgresFact]
    public async Task ArchiveAsync_WhenStorageFails_PersistsStableNonSensitiveErrorState()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage
        {
            WriteException = new ObjectStorageException(
                "provider-secret-detail",
                new InvalidOperationException("credential and endpoint detail"))
        };
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);

        await Assert.ThrowsAsync<ArchiveException>(() =>
            service.ArchiveAsync(CreateRequest("content"u8.ToArray())));

        dbContext.ChangeTracker.Clear();
        ArchiveObject persisted = await dbContext.ArchiveObjects
            .Include(item => item.Errors)
            .Include(item => item.Events)
            .OrderByDescending(item => item.ArchiveObjectId)
            .FirstAsync();

        Assert.Equal(ArchiveStatus.Error, persisted.ArchiveStatus);
        ArchiveErrorQueueItem error = Assert.Single(persisted.Errors);
        Assert.Equal("ARCHIVE_FAILED", error.ErrorType);
        Assert.Equal("The archive content could not be stored or finalized.", error.LastErrorMessage);
        Assert.DoesNotContain("secret", error.LastErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(persisted.Events, item => item.EventType == ArchiveEventType.ErrorOccurred);
    }

    [PostgresFact]
    public async Task ArchiveAsync_WhenCancelledDuringUpload_MarksObjectAsErrorAndPropagatesCancellation()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        using var cancellation = new CancellationTokenSource();
        var storage = new InMemoryObjectStorage
        {
            BeforeWriteCompletesAsync = _ =>
            {
                cancellation.Cancel();
                return Task.FromException(new OperationCanceledException(cancellation.Token));
            }
        };
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ArchiveAsync(
                CreateRequest("cancelled"u8.ToArray()),
                cancellation.Token));

        dbContext.ChangeTracker.Clear();
        ArchiveObject persisted = await dbContext.ArchiveObjects
            .Include(item => item.Errors)
            .OrderByDescending(item => item.ArchiveObjectId)
            .FirstAsync();

        Assert.Equal(ArchiveStatus.Error, persisted.ArchiveStatus);
        Assert.Equal("UPLOAD_CANCELLED", Assert.Single(persisted.Errors).ErrorType);
    }

    [PostgresFact]
    public async Task ArchiveAsync_WhenFinalDatabaseWriteFails_ThrowsAndLeavesRecoverablePendingRecord()
    {
        string connectionString = fixture.GetRequiredConnectionString();
        var storage = new InMemoryObjectStorage();
        var dbContext = CoreTestFactory.CreateDbContext(connectionString);
        storage.BeforeWriteCompletesAsync = _ => dbContext.DisposeAsync().AsTask();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);

        await Assert.ThrowsAsync<ArchiveException>(() =>
            service.ArchiveAsync(CreateRequest("database failure"u8.ToArray())));

        await using CirrusDbContext verificationContext = CreateDbContext();
        ArchiveObject persisted = await verificationContext.ArchiveObjects
            .OrderByDescending(item => item.ArchiveObjectId)
            .FirstAsync();

        Assert.Equal(ArchiveStatus.Pending, persisted.ArchiveStatus);
        Assert.Null(persisted.Sha256Hash);
        Assert.True(await storage.ExistsAsync(persisted.BucketName, persisted.ObjectKey));
    }

    [PostgresFact]
    public async Task DownloadAsync_ReturnsReadableOwnedStreamAndRecordsOpeningEvent()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        byte[] content = "download payload"u8.ToArray();
        ArchiveFileResult archived = await service.ArchiveAsync(CreateRequest(content));

        ArchiveDownloadResult download = await service.DownloadAsync(
            archived.ArchiveObjectId,
            "  downloader  ");
        await using Stream returnedStream = download.Content;
        await using var buffer = new MemoryStream();
        await returnedStream.CopyToAsync(buffer);

        Assert.Equal(content, buffer.ToArray());
        Assert.Equal(Sha256(content), download.Sha256Hash);

        dbContext.ChangeTracker.Clear();
        ArchiveEvent downloadEvent = await dbContext.ArchiveEvents
            .SingleAsync(item =>
                item.ArchiveObjectId == archived.ArchiveObjectId
                && item.EventType == ArchiveEventType.Downloaded);
        Assert.Equal("downloader", downloadEvent.Actor);
    }

    [PostgresFact]
    public async Task DownloadAsync_RejectsDeletionRequestedObjectBeforeOpeningStorage()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        ArchiveFileResult archived = await service.ArchiveAsync(CreateRequest("content"u8.ToArray()));
        await service.RequestDeletionAsync(archived.ArchiveObjectId, "deleter");
        storage.ReadException = new InvalidOperationException("Storage must not be called.");

        ArchiveObjectUnavailableException exception =
            await Assert.ThrowsAsync<ArchiveObjectUnavailableException>(() =>
                service.DownloadAsync(archived.ArchiveObjectId, "downloader"));

        Assert.Equal(ArchiveStatus.DeletionRequested, exception.ArchiveStatus);
    }

    [PostgresFact]
    public async Task VerifyIntegrityAsync_DetectsMismatchPersistsEventAndDisposesStream()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        ArchiveFileResult archived = await service.ArchiveAsync(CreateRequest("original"u8.ToArray()));
        ArchiveObject location = await dbContext.ArchiveObjects
            .SingleAsync(item => item.ArchiveObjectId == archived.ArchiveObjectId);
        storage.Replace(location.BucketName, location.ObjectKey, "tampered"u8.ToArray());

        ArchiveIntegrityResult result = await service.VerifyIntegrityAsync(
            archived.ArchiveObjectId,
            "verifier");

        Assert.False(result.IsValid);
        Assert.NotEqual(result.ExpectedSha256Hash, result.ActualSha256Hash);
        Assert.True(storage.LastReadStream?.IsDisposed);

        dbContext.ChangeTracker.Clear();
        ArchiveEvent integrityEvent = await dbContext.ArchiveEvents
            .SingleAsync(item =>
                item.ArchiveObjectId == archived.ArchiveObjectId
                && item.EventType == ArchiveEventType.IntegrityCheckFailed);
        Assert.Equal("verifier", integrityEvent.Actor);
        Assert.NotNull(integrityEvent.DetailsJson);
    }

    [PostgresFact]
    public async Task VerifyIntegrityAsync_WhenContentMatches_RecordsValidStatus()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        ArchiveFileResult archived = await service.ArchiveAsync(
            CreateRequest("verified content"u8.ToArray()));

        ArchiveIntegrityResult verification = await service.VerifyIntegrityAsync(
            archived.ArchiveObjectId,
            "integrity-operator");
        ArchiveIntegrityStatusResult? status = await service.GetIntegrityStatusAsync(
            archived.ArchiveObjectId);

        Assert.True(verification.IsValid);
        Assert.NotNull(status);
        Assert.True(status.LastCheckIsValid);
        Assert.Equal("integrity-operator", status.LastCheckActor);
        Assert.False(status.IsCheckInProgress);
        Assert.True(storage.LastReadStream?.IsDisposed);
    }

    [PostgresFact]
    public async Task VerifyIntegrityAsync_WhenStorageReadFails_WrapsFailureWithoutWritingResultEvent()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        ArchiveFileResult archived = await service.ArchiveAsync(
            CreateRequest("unavailable content"u8.ToArray()));
        storage.ReadException = new ObjectStorageException(
            "storage unavailable",
            new IOException("provider details"));

        await Assert.ThrowsAsync<ArchiveException>(() =>
            service.VerifyIntegrityAsync(archived.ArchiveObjectId, "verifier"));

        dbContext.ChangeTracker.Clear();
        int resultEvents = await dbContext.ArchiveEvents.CountAsync(item =>
            item.ArchiveObjectId == archived.ArchiveObjectId
            && (item.EventType == ArchiveEventType.IntegrityVerified
                || item.EventType == ArchiveEventType.IntegrityCheckFailed));
        Assert.Equal(0, resultEvents);
    }

    [PostgresFact]
    public async Task SearchAsync_DefaultScopeExcludesDeletionRequestedObjects()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        string source = $"status-search-{Guid.NewGuid():N}";
        ArchiveFileResult archived = await service.ArchiveAsync(
            CreateRequest("content"u8.ToArray(), sourceSystem: source));
        await service.RequestDeletionAsync(archived.ArchiveObjectId, "deleter");

        ArchiveSearchResult defaultResult = await service.SearchAsync(
            new ArchiveSearchRequest { SourceSystem = source });
        ArchiveSearchResult explicitResult = await service.SearchAsync(
            new ArchiveSearchRequest
            {
                SourceSystem = source,
                ArchiveStatus = ArchiveStatus.DeletionRequested
            });

        Assert.Empty(defaultResult.Items);
        Assert.Equal(archived.ArchiveObjectId, Assert.Single(explicitResult.Items).ArchiveObjectId);
    }

    [PostgresFact]
    public async Task SearchAsync_RequiresCombinedReferenceFiltersToMatchSameReference()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        int documentTypeId = await GetReferenceTypeIdAsync(dbContext, "document-id");
        int caseTypeId = await GetReferenceTypeIdAsync(dbContext, "case-id");
        string source = $"search-{Guid.NewGuid():N}";
        ArchiveFileResult archived = await service.ArchiveAsync(
            CreateRequest(
                "content"u8.ToArray(),
                sourceSystem: source,
                references:
                [
                    new ArchiveBusinessReferenceInput(documentTypeId, "DOC-1", "invoice", "tenant-a"),
                    new ArchiveBusinessReferenceInput(caseTypeId, "CASE-1", "case", "tenant-b")
                ]));

        ArchiveSearchResult mismatched = await service.SearchAsync(new ArchiveSearchRequest
        {
            SourceSystem = source,
            Tenant = "tenant-a",
            BusinessReferenceValue = "CASE-1"
        });
        ArchiveSearchResult matched = await service.SearchAsync(new ArchiveSearchRequest
        {
            SourceSystem = source,
            Tenant = "tenant-b",
            BusinessReferenceValue = "CASE-1",
            BusinessReferenceTypeId = caseTypeId
        });

        Assert.Empty(mismatched.Items);
        Assert.Equal(archived.ArchiveObjectId, Assert.Single(matched.Items).ArchiveObjectId);
    }

    [PostgresFact]
    public async Task SearchAsync_TreatsLikeWildcardsLiterallyAndPaginatesDeterministically()
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        var storage = new InMemoryObjectStorage();
        ArchiveService service = CoreTestFactory.CreateService(dbContext, storage);
        string source = $"paging-{Guid.NewGuid():N}";
        await service.ArchiveAsync(CreateRequest("one"u8.ToArray(), "literal%_name.txt", sourceSystem: source));
        await service.ArchiveAsync(CreateRequest("two"u8.ToArray(), "literalXXname.txt", sourceSystem: source));
        await service.ArchiveAsync(CreateRequest("three"u8.ToArray(), "third.txt", sourceSystem: source));

        ArchiveSearchResult literal = await service.SearchAsync(new ArchiveSearchRequest
        {
            SourceSystem = source,
            OriginalFilename = "%_",
            PageSize = 10
        });
        ArchiveSearchResult firstPage = await service.SearchAsync(new ArchiveSearchRequest
        {
            SourceSystem = source,
            PageNumber = 1,
            PageSize = 2
        });
        ArchiveSearchResult secondPage = await service.SearchAsync(new ArchiveSearchRequest
        {
            SourceSystem = source,
            PageNumber = 2,
            PageSize = 2
        });

        Assert.Equal("literal%_name.txt", Assert.Single(literal.Items).OriginalFilename);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage.Items);
        Assert.Empty(firstPage.Items.Select(item => item.ArchiveObjectId)
            .Intersect(secondPage.Items.Select(item => item.ArchiveObjectId)));
    }

    [PostgresFact]
    public async Task RequestDeletionAsync_ConcurrentRequestsCreateExactlyOneTransition()
    {
        var storage = new InMemoryObjectStorage();
        long archiveObjectId;

        await using (CirrusDbContext setupContext = CreateDbContext())
        {
            ArchiveService setupService = CoreTestFactory.CreateService(setupContext, storage);
            archiveObjectId = (await setupService.ArchiveAsync(
                CreateRequest("delete me"u8.ToArray()))).ArchiveObjectId;
        }

        await using CirrusDbContext firstContext = CreateDbContext();
        await using CirrusDbContext secondContext = CreateDbContext();
        ArchiveService firstService = CoreTestFactory.CreateService(firstContext, storage);
        ArchiveService secondService = CoreTestFactory.CreateService(secondContext, storage);

        ArchiveDeletionRequestResult[] results = await Task.WhenAll(
            firstService.RequestDeletionAsync(archiveObjectId, "actor-one"),
            secondService.RequestDeletionAsync(archiveObjectId, "actor-two"));

        Assert.Single(results, result => result.StateChanged);
        Assert.Single(results, result => !result.StateChanged);

        await using CirrusDbContext verificationContext = CreateDbContext();
        ArchiveObject persisted = await verificationContext.ArchiveObjects
            .Include(item => item.Events)
            .SingleAsync(item => item.ArchiveObjectId == archiveObjectId);
        Assert.Equal(ArchiveStatus.DeletionRequested, persisted.ArchiveStatus);
        Assert.Single(
            persisted.Events,
            item => item.EventType == ArchiveEventType.DeletionRequested);
    }

    private CirrusDbContext CreateDbContext()
    {
        return CoreTestFactory.CreateDbContext(fixture.GetRequiredConnectionString());
    }

    private static async Task<int> GetReferenceTypeIdAsync(
        CirrusDbContext dbContext,
        string key)
    {
        return await dbContext.BusinessReferenceTypes
            .Where(item => item.ReferenceTypeKey == key)
            .Select(item => item.BusinessReferenceTypeId)
            .SingleAsync();
    }

    private static ArchiveFileRequest CreateRequest(
        byte[] content,
        string fileName = "file.txt",
        string? sourceSystem = null,
        string fileType = "document",
        string? partner = null,
        string actor = "integration-suite",
        IReadOnlyCollection<ArchiveBusinessReferenceInput>? references = null)
    {
        return new ArchiveFileRequest
        {
            Content = new MemoryStream(content),
            OriginalFilename = fileName,
            FileType = fileType,
            MimeType = "application/octet-stream",
            SourceSystem = sourceSystem ?? $"source-{Guid.NewGuid():N}",
            Partner = partner,
            Tenant = "tenant-a",
            ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedBy = actor,
            RetentionUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(10)),
            BusinessReferences = references ?? []
        };
    }

    private static string Sha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }
}
