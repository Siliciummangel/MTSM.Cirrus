using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Models;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public ApiArchiveService ArchiveService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IArchiveService>();
            services.AddSingleton<IArchiveService>(ArchiveService);
        });
    }
}

internal sealed class ApiArchiveService : IArchiveService
{
    public ArchiveFileRequest? LastArchiveRequest { get; private set; }

    public string? LastActor { get; private set; }

    public Func<ArchiveFileRequest, CancellationToken, Task<ArchiveFileResult>>
        ArchiveHandler { get; set; } = (_, _) =>
            Task.FromResult(new ArchiveFileResult(
                42,
                "objects/42",
                new string('a', 64),
                7,
                DateTimeOffset.Parse("2026-08-19T00:00:00Z")));

    public Func<long, string, CancellationToken, Task<ArchiveDownloadResult>>
        DownloadHandler { get; set; } = (id, _, _) =>
            Task.FromResult(new ArchiveDownloadResult(
                id,
                "payload.txt",
                "text/plain",
                7,
                new string('a', 64),
                new MemoryStream("payload"u8.ToArray())));

    public Func<long, CancellationToken, Task<ArchiveMetadataResult?>>
        MetadataHandler { get; set; } = (_, _) =>
            Task.FromResult<ArchiveMetadataResult?>(null);

    public Func<ArchiveSearchRequest, CancellationToken, Task<ArchiveSearchResult>>
        SearchHandler { get; set; } = (_, _) =>
            Task.FromResult(new ArchiveSearchResult([], 1, 50, 0, 0));

    public Func<long, string, CancellationToken, Task<ArchiveIntegrityResult>>
        IntegrityHandler { get; set; } = (id, _, _) =>
            Task.FromResult(new ArchiveIntegrityResult(
                id,
                true,
                new string('a', 64),
                new string('a', 64),
                7,
                7,
                DateTimeOffset.Parse("2026-08-19T00:00:00Z")));

    public Func<long, CancellationToken, Task<ArchiveIntegrityStatusResult?>>
        IntegrityStatusHandler { get; set; } = (_, _) =>
            Task.FromResult<ArchiveIntegrityStatusResult?>(null);

    public Func<long, string, CancellationToken, Task<ArchiveDeletionRequestResult>>
        DeletionHandler { get; set; } = (id, actor, _) =>
            Task.FromResult(new ArchiveDeletionRequestResult(
                id,
                Core.Enums.ArchiveStatus.DeletionRequested,
                DateTimeOffset.Parse("2026-08-19T00:00:00Z"),
                actor,
                null,
                true));

    public Task<ArchiveFileResult> ArchiveAsync(
        ArchiveFileRequest request,
        CancellationToken cancellationToken = default)
    {
        LastArchiveRequest = request;
        return ArchiveHandler(request, cancellationToken);
    }

    public Task<ArchiveDownloadResult> DownloadAsync(
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        LastActor = actor;
        return DownloadHandler(archiveObjectId, actor, cancellationToken);
    }

    public Task<ArchiveMetadataResult?> GetMetadataAsync(
        long archiveObjectId,
        CancellationToken cancellationToken = default) =>
        MetadataHandler(archiveObjectId, cancellationToken);

    public Task<ArchiveSearchResult> SearchAsync(
        ArchiveSearchRequest request,
        CancellationToken cancellationToken = default) =>
        SearchHandler(request, cancellationToken);

    public Task<ArchiveIntegrityResult> VerifyIntegrityAsync(
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        LastActor = actor;
        return IntegrityHandler(archiveObjectId, actor, cancellationToken);
    }

    public Task<ArchiveIntegrityStatusResult?> GetIntegrityStatusAsync(
        long archiveObjectId,
        CancellationToken cancellationToken = default) =>
        IntegrityStatusHandler(archiveObjectId, cancellationToken);

    public Task<ArchiveDeletionRequestResult> RequestDeletionAsync(
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        LastActor = actor;
        return DeletionHandler(archiveObjectId, actor, cancellationToken);
    }
}
