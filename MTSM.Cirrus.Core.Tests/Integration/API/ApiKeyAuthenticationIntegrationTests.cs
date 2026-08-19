using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Security;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ApiKeyAuthenticationIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    [PostgresFact]
    public async Task ApiKeyProvider_AuthenticatesHashesRevokesAndEnforcesTenant()
    {
        GeneratedApiKey generated = ApiKeySecret.Generate();
        long credentialId;
        await using (CirrusDbContext db = CoreTestFactory.CreateDbContext(fixture.GetRequiredConnectionString()))
        {
            var machine = new MachineIdentity
            {
                TenantId = 1,
                Name = "api-auth-test",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "integration-test",
                Permissions = [new() { Permission = CirrusPermission.ArchiveRead }]
            };
            var credential = new ApiKeyCredential
            {
                MachineIdentity = machine,
                KeyId = generated.KeyId,
                SecretHash = generated.SecretHash,
                HashAlgorithm = ApiKeySecret.HashAlgorithm,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Add(credential);
            await db.SaveChangesAsync();
            credentialId = credential.ApiKeyCredentialId;
        }

        using var factory = new RealApiKeyFactory(fixture.GetRequiredConnectionString());
        using HttpClient client = factory.CreateClient();

        using var valid = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/1/archive/search");
        valid.Headers.Authorization = new("ApiKey", generated.Value);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(valid)).StatusCode);
        Assert.Equal(1, factory.ArchiveService.LastTenantId);

        using var foreignTenant = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/2/archive/search");
        foreignTenant.Headers.Authorization = new("ApiKey", generated.Value);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(foreignTenant)).StatusCode);

        using var invalid = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/1/archive/search");
        invalid.Headers.Authorization = new("ApiKey", $"cirrus_{generated.KeyId}.wrong-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(invalid)).StatusCode);

        await using (CirrusDbContext db = CoreTestFactory.CreateDbContext(fixture.GetRequiredConnectionString()))
        {
            await db.ApiKeyCredentials.Where(x => x.ApiKeyCredentialId == credentialId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ApiKeyStatus.Revoked)
                    .SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        }

        using var revoked = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/1/archive/search");
        revoked.Headers.Authorization = new("ApiKey", generated.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(revoked)).StatusCode);
    }

    public Task InitializeAsync() => fixture.ResetAndSeedAsync();
    public Task DisposeAsync() => fixture.ResetAndSeedAsync();
}

internal sealed class RealApiKeyFactory(string connectionString) : WebApplicationFactory<Program>
{
    public ApiArchiveService ArchiveService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:ArchiveDatabase"] = connectionString }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IArchiveService>();
            services.AddSingleton<IArchiveService>(ArchiveService);
        });
    }
}
