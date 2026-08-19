using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.API.Security;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Models;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public ApiArchiveService ArchiveService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IArchiveService>();
            services.AddSingleton<IArchiveService>(ArchiveService);
        });
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Test-Anonymous"))
            return Task.FromResult(AuthenticateResult.NoResult());

        string[] permissions = Request.Headers.TryGetValue("X-Test-Permissions", out var configured)
            ? configured.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["archive.read", "archive.write", "archive.delete", "archive.verify"];
        string tenantId = Request.Headers.TryGetValue("X-Test-Tenant", out var tenant) ? tenant.ToString() : "1";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "machine:1"),
            new(CirrusClaimTypes.TenantId, tenantId),
            new(CirrusClaimTypes.Actor, "apikey:machine:1"),
            new(CirrusClaimTypes.Provider, SchemeName)
        };
        claims.AddRange(permissions.Select(value => new Claim(CirrusClaimTypes.Permission, value)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

internal sealed class ApiArchiveService : IArchiveService
{
    public ArchiveFileRequest? LastArchiveRequest { get; private set; }

    public string? LastActor { get; private set; }
    public long? LastTenantId { get; private set; }

    public Func<ArchiveFileRequest, CancellationToken, Task<ArchiveFileResult>>
        ArchiveHandler { get; set; } = (_, _) =>
            Task.FromResult(new ArchiveFileResult(
                42,
                1,
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
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        LastTenantId = tenantId;
        LastActor = actor;
        return DownloadHandler(archiveObjectId, actor, cancellationToken);
    }

    public Task<ArchiveMetadataResult?> GetMetadataAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken = default)
    {
        LastTenantId = tenantId;
        return MetadataHandler(archiveObjectId, cancellationToken);
    }

    public Task<ArchiveSearchResult> SearchAsync(
        long tenantId,
        ArchiveSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        LastTenantId = tenantId;
        return SearchHandler(request, cancellationToken);
    }

    public Task<ArchiveIntegrityResult> VerifyIntegrityAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        LastTenantId = tenantId;
        LastActor = actor;
        return IntegrityHandler(archiveObjectId, actor, cancellationToken);
    }

    public Task<ArchiveIntegrityStatusResult?> GetIntegrityStatusAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken = default)
    {
        LastTenantId = tenantId;
        return IntegrityStatusHandler(archiveObjectId, cancellationToken);
    }

    public Task<ArchiveDeletionRequestResult> RequestDeletionAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        LastTenantId = tenantId;
        LastActor = actor;
        return DeletionHandler(archiveObjectId, actor, cancellationToken);
    }
}
